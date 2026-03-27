// Copyright © ABB Ltd. All rights reserved.

using System.Globalization;
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

    public LogViewerController(
        IConfiguration configuration,
        IOptions<LogViewerOptions> options,
        ILogger<LogViewerController> logger)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
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
            return Ok(HtmlBuilder.BuildApiResponse(d.LogLines, d.LogFilePath, d.AllFiles, d.EffectiveLines));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogViewer data endpoint failed for file={File} lines={Lines} dirs={Dirs}",
                file, lines, dirs);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

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
            return null;
        return int.TryParse(lines, out var n) && n > 0 ? n : _options.DefaultLineCount;
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

