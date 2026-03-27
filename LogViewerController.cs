// Copyright © ABB Ltd. All rights reserved.

using System.Globalization;
using HtmlLogViewer.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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

    public LogViewerController(IConfiguration configuration, IOptions<LogViewerOptions> options)
    {
        _configuration = configuration;
        _options = options.Value;
    }

    /// <summary>
    /// Returns an HTML page displaying log lines from the selected rolling log file.
    /// </summary>
    /// <param name="file">
    /// Optional log filename (basename only, e.g. <c>GDS20250115.log</c>).
    /// Defaults to the most recently written file in the log directory.
    /// </param>
    /// <param name="lines">
    /// Number of lines to display: one of the configured options or <c>"All"</c>.
    /// Defaults to <see cref="LogViewerOptions.DefaultLineCount"/>.
    /// </param>
    [HttpGet("")]
    [Produces("text/html")]
    public IActionResult Get(
        [FromQuery] string? file  = null,
        [FromQuery] string? lines = null,
        [FromQuery] string? dirs  = null)
    {
        var effectiveLines = lines ?? _options.DefaultLineCount.ToString(CultureInfo.InvariantCulture);

        // User-supplied directories from the UI text field (semicolon-separated).
        var userDirs = string.IsNullOrWhiteSpace(dirs)
            ? []
            : dirs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Collect log file paths from three sources, then merge and de-duplicate:
        //  1. Auto-discovered by walking the entire Serilog config section as JSON.
        //  2. Explicitly listed in LogViewer:ExtraPaths in appsettings.
        //  3. Entered at runtime in the "Log Directories" UI text field.
        var serilogPaths = LogFileReader.FindSerilogLogPaths(_configuration);
        var allPaths = serilogPaths
            .Concat(_options.ExtraPaths)
            .Concat(userDirs)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var allFiles        = LogFileReader.GetAllLogFiles(allPaths);
        var resolvedFilePath = LogFileReader.ResolveSelectedFile(allFiles, file);
        var lineCount       = ParseLineCount(effectiveLines);
        var logLines        = LogFileReader.ReadLines(resolvedFilePath, lineCount);
        var html            = HtmlBuilder.Build(logLines, resolvedFilePath, allFiles,
                                               effectiveLines, dirs ?? string.Empty, _options);
        return Content(html, "text/html");
    }

    private int? ParseLineCount(string lines)
    {
        if (lines.Equals("All", StringComparison.OrdinalIgnoreCase))
            return null;
        return int.TryParse(lines, out var n) && n > 0 ? n : _options.DefaultLineCount;
    }
}
