// Copyright © ABB Ltd. All rights reserved.

using System.Globalization;
using System.Text.Json;
using HtmlLogViewer.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HtmlLogViewer;

/// <summary>
/// ASP.NET Core controller that serves the browser-based log viewer.
/// The route is set at startup by <see cref="LogViewerRouteConvention"/> using
/// <see cref="LogViewerOptions.RoutePrefix"/> — no hard-coded <c>[Route]</c> attribute needed.
/// </summary>
[ApiController]
public sealed class LogViewerController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly LogViewerOptions _options;
    private readonly ILogger<LogViewerController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LogViewerController(
        IConfiguration configuration,
        IOptions<LogViewerOptions> options,
        ILogger<LogViewerController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration    = configuration;
        _options          = options.Value;
        _logger           = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns the full HTML page for the log viewer (initial load).
    /// </summary>
    [HttpGet("")]
    [Produces("text/html")]
    public IActionResult Get(
        [FromQuery] string? file  = null,
        [FromQuery] string? lines = null,
        [FromQuery] string? dirs  = null)
    {
        try
        {
            var d    = ResolveLogData(file, lines, dirs);
            var html = HtmlBuilder.Build(d.LogLines, d.LogFilePath, d.AllFiles,
                                         d.EffectiveLines, d.UserDirs, _options);
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogViewer failed to render page for file={File} lines={Lines} dirs={Dirs}",
                file, lines, dirs);
            return StatusCode(500, BuildErrorHtml(ex));
        }
    }

    /// <summary>
    /// Returns a JSON payload used by the reactive front-end to update the log area
    /// without a full page reload.
    /// </summary>
    [HttpGet("data")]
    [Produces("application/json")]
    public IActionResult GetData(
        [FromQuery] string? file  = null,
        [FromQuery] string? lines = null,
        [FromQuery] string? dirs  = null)
    {
        try
        {
            var d = ResolveLogData(file, lines, dirs);
            return Ok(HtmlBuilder.BuildApiResponse(d.LogLines, d.LogFilePath, d.AllFiles, d.EffectiveLines, _options.AllLinesLimit));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogViewer data endpoint failed for file={File} lines={Lines} dirs={Dirs}",
                file, lines, dirs);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns a lightweight JSON list of all locally available log files.
    /// Used by remote instances to discover this node's files.
    /// </summary>
    [HttpGet("files")]
    [Produces("application/json")]
    public IActionResult GetFiles([FromQuery] string? dirs = null)
    {
        try
        {
            var d = ResolveLogData(null, null, dirs);
            return Ok(new
            {
                files = d.AllFiles.Select(f => new { name = f.Name, path = f.FullName })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogViewer files endpoint failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Fetches the log-file list from each remote host (semicolon-separated <paramref name="hosts"/>)
    /// and returns a merged list with the node name prepended to each file name.
    /// Acts as a server-side proxy to avoid browser CORS restrictions.
    /// </summary>
    [HttpGet("remote/files")]
    [Produces("application/json")]
    public async Task<IActionResult> GetRemoteFiles(
        [FromQuery] string? hosts = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hosts))
            return Ok(new { files = Array.Empty<object>(), errors = Array.Empty<string>() });

        var hostList = hosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allFiles = new List<object>();
        var errors   = new List<string>();
        var http     = _httpClientFactory.CreateClient("logviewer-remote");

        foreach (var hostEntry in hostList)
        {
            var spec = ParseHostSpec(hostEntry);
            try
            {
                var url  = $"{spec.BaseUrl}/{spec.RoutePrefix}/files";
                var resp = await http.GetAsync(url, cancellationToken);
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("files", out var filesArr))
                {
                    foreach (var file in filesArr.EnumerateArray())
                    {
                        var name = file.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var path = file.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                        allFiles.Add(new
                        {
                            // Display: "[node1] App20250115.log"
                            name = $"[{spec.DisplayName}] {name}",
                            // Value encodes original entry + remote path for the proxy round-trip
                            path = $"__remote__::{hostEntry}::{path}"
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not fetch files from remote host {Host}", hostEntry);
                errors.Add($"{hostEntry}: {ex.Message}");
            }
        }

        return Ok(new { files = allFiles, errors });
    }

    /// <summary>
    /// Proxies a log-data request to a single remote <paramref name="host"/>.
    /// The client sends <c>__remote__::{host}::{path}</c> as the selected file;
    /// the browser then calls this endpoint which forwards the request server-side.
    /// </summary>
    [HttpGet("remote/data")]
    [Produces("application/json")]
    public async Task<IActionResult> GetRemoteData(
        [FromQuery] string? host  = null,
        [FromQuery] string? file  = null,
        [FromQuery] string? lines = null,
        [FromQuery] string? dirs  = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return BadRequest(new { error = "host parameter is required" });

        try
        {
            var queryParts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(file))  queryParts.Add("file="  + Uri.EscapeDataString(file));
            if (!string.IsNullOrWhiteSpace(lines)) queryParts.Add("lines=" + Uri.EscapeDataString(lines));
            if (!string.IsNullOrWhiteSpace(dirs))  queryParts.Add("dirs="  + Uri.EscapeDataString(dirs));

            var qs        = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
            var spec      = ParseHostSpec(host);
            var remoteUrl = $"{spec.BaseUrl}/{spec.RoutePrefix}/data{qs}";
            var http      = _httpClientFactory.CreateClient("logviewer-remote");
            var resp      = await http.GetAsync(remoteUrl, cancellationToken);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return Content(json, "application/json");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Remote data proxy failed for host={Host}", host);
            return StatusCode(502, new { error = $"Failed to reach {host}: {ex.Message}" });
        }
    }

    // ── Shared helpers ────────────────────────────────────────────

    private record LogData(
        string[]  LogLines,
        string    LogFilePath,
        FileInfo[] AllFiles,
        string    EffectiveLines,
        string    UserDirs);

    private LogData ResolveLogData(string? file, string? lines, string? dirs)
    {
        var effectiveLines = lines ?? _options.DefaultLineCount.ToString(CultureInfo.InvariantCulture);

        var userDirs = string.IsNullOrWhiteSpace(dirs)
            ? []
            : dirs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var serilogPaths = LogFileReader.FindSerilogLogPaths(_configuration);
        var allPaths = serilogPaths
            .Concat(_options.ExtraPaths)
            .Concat(userDirs)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var allFiles         = LogFileReader.GetAllLogFiles(allPaths);
        var resolvedFilePath = LogFileReader.ResolveSelectedFile(allFiles, file);
        var lineCount        = ParseLineCount(effectiveLines);
        var logLines         = LogFileReader.ReadLines(resolvedFilePath, lineCount);

        return new LogData(logLines, resolvedFilePath, allFiles, effectiveLines, dirs ?? string.Empty);
    }

    private int? ParseLineCount(string lines)
    {
        if (lines.Equals("All", StringComparison.OrdinalIgnoreCase))
            return _options.AllLinesLimit > 0 ? _options.AllLinesLimit : (int?)null;
        return int.TryParse(lines, out var n) && n > 0 ? n : _options.DefaultLineCount;
    }

    // ── Remote host spec parsing ──────────────────────────────────────────────

    private readonly record struct RemoteHostSpec(string BaseUrl, string RoutePrefix, string DisplayName);

    /// <summary>
    /// Parses a user-supplied remote host entry into its components.
    /// <list type="table">
    ///   <listheader><term>Input</term><description>Resolved to</description></listheader>
    ///   <item><term><c>node1:8932</c></term>
    ///         <description>https://node1:8932 / <see cref="LogViewerOptions.RoutePrefix"/></description></item>
    ///   <item><term><c>node1:8932/customlogs</c></term>
    ///         <description>https://node1:8932 / customlogs</description></item>
    ///   <item><term><c>http://node1:8932</c></term>
    ///         <description>http://node1:8932  / <see cref="LogViewerOptions.RoutePrefix"/></description></item>
    ///   <item><term><c>https://node1:8932/api/logs</c></term>
    ///         <description>https://node1:8932 / api/logs</description></item>
    /// </list>
    /// HTTPS is the default scheme when none is provided.
    /// </summary>
    private RemoteHostSpec ParseHostSpec(string entry)
    {
        entry = entry.Trim();

        // Detect and strip explicit scheme; default to https.
        string scheme, rest;
        if (entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "http";
            rest   = entry[7..];
        }
        else if (entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "https";
            rest   = entry[8..];
        }
        else
        {
            scheme = "https";   // default: secure
            rest   = entry;
        }

        // Split host:port from optional /route-prefix
        var slashIdx    = rest.IndexOf('/');
        string hostPort = slashIdx > 0 ? rest[..slashIdx]               : rest;
        string prefix   = slashIdx > 0 ? rest[(slashIdx + 1)..].Trim('/') : "";

        if (string.IsNullOrWhiteSpace(prefix))
            prefix = _options.RoutePrefix;   // fall back to this instance's prefix

        // Display name: strip port from host
        var colonIdx    = hostPort.LastIndexOf(':');
        var displayName = colonIdx > 0 ? hostPort[..colonIdx] : hostPort;

        return new RemoteHostSpec($"{scheme}://{hostPort}", prefix, displayName);
    }

    private static string BuildErrorHtml(Exception ex) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <title>Log Viewer — Error</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body { font-family: 'Segoe UI', sans-serif; background: #1e1e1e; color: #d4d4d4; padding: 32px; }
                h1   { color: #f48771; font-size: 1.3rem; margin-bottom: 16px; }
                h2   { color: #aaa; font-size: 0.9rem; margin: 20px 0 6px; }
                pre  { background: #0d0d0d; border: 1px solid #333; border-radius: 6px;
                       padding: 14px 16px; font-size: 0.8rem; line-height: 1.6;
                       white-space: pre-wrap; word-break: break-all; color: #f48771; }
            </style>
        </head>
        <body>
            <h1>&#9888;&#160;Log Viewer encountered an error</h1>
            <h2>Exception</h2>
            <pre>{{System.Web.HttpUtility.HtmlEncode(ex.GetType().FullName)}}: {{System.Web.HttpUtility.HtmlEncode(ex.Message)}}</pre>
            <h2>Stack Trace</h2>
            <pre>{{System.Web.HttpUtility.HtmlEncode(ex.StackTrace ?? "(none)")}}</pre>
        </body>
        </html>
        """;
}

