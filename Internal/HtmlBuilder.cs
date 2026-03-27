// Copyright © ABB Ltd. All rights reserved.

using System.Globalization;
using System.Text;
using System.Web;

namespace HtmlLogViewer.Internal;

/// <summary>
/// Builds the HTML page returned by <see cref="LogViewerController"/> by loading three
/// embedded resource templates (<c>viewer.html</c>, <c>viewer.css</c>, <c>viewer.js</c>)
/// and substituting <c>{{TOKEN}}</c> placeholders with per-request dynamic content.
/// The resources are loaded once at class initialisation and cached for the lifetime of
/// the application.
/// </summary>
internal static class HtmlBuilder
{
    // ── Embedded templates — loaded once at class initialisation ─────────────
    private static readonly string HtmlTemplate = LoadResource("viewer.html");
    private static readonly string CssContent   = LoadResource("viewer.css");
    private static readonly string JsContent    = LoadResource("viewer.js");

    private static string LoadResource(string fileName)
    {
        var assembly     = typeof(HtmlBuilder).Assembly;
        var resourceName = $"ABB.GDS.LogViewer.Resources.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'. " +
                $"Available: [{string.Join(", ", assembly.GetManifestResourceNames())}]");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── Per-request page generation ───────────────────────────────────────────
    internal static string Build(
        string[] logLines,
        string logFilePath,
        FileInfo[] allFiles,
        string selectedLines,
        string userDirs,
        LogViewerOptions options)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return HtmlTemplate
            .Replace("{{CSS}}",            CssContent,                                               StringComparison.Ordinal)
            .Replace("{{JS}}",             JsContent,                                                StringComparison.Ordinal)
            .Replace("{{PAGE_TITLE}}",     HttpUtility.HtmlEncode(options.PageTitle),                StringComparison.Ordinal)
            .Replace("{{FILE_OPTIONS}}",   BuildFileOptions(allFiles, logFilePath),                  StringComparison.Ordinal)
            .Replace("{{LINES_OPTIONS}}", BuildLinesOptions(options.LineCountOptions, selectedLines), StringComparison.Ordinal)
            .Replace("{{DIRS_VALUE}}",     HttpUtility.HtmlAttributeEncode(userDirs),                StringComparison.Ordinal)
            .Replace("{{STATUS_LABEL}}",   BuildStatusLabel(selectedLines, logLines.Length),         StringComparison.Ordinal)
            .Replace("{{GENERATED_TIME}}", HttpUtility.HtmlEncode(now),                              StringComparison.Ordinal)
            .Replace("{{LOG_LINES}}",      BuildLogLines(logLines),                                  StringComparison.Ordinal);
    }

    // ── Fragment builders ─────────────────────────────────────────────────────

    private static string BuildFileOptions(FileInfo[] allFiles, string logFilePath)
    {
        // When the same filename appears in multiple source directories,
        // append the parent directory name to disambiguate.
        var duplicateNames = allFiles
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        foreach (var fi in allFiles)
        {
            var sel = string.Equals(fi.FullName, logFilePath, StringComparison.OrdinalIgnoreCase)
                ? " selected" : string.Empty;
            var display = duplicateNames.Contains(fi.Name)
                ? $"{fi.Name} ({fi.Directory?.Name ?? string.Empty})"
                : fi.Name;
            sb.Append(
                $"<option value=\"{{HttpUtility.HtmlAttributeEncode(fi.FullName)}}\"{sel}>" +
                $"{HttpUtility.HtmlEncode(display)}</option>\n");
        }
        return sb.ToString();
    }

    private static string BuildLinesOptions(int[] lineCountOptions, string selectedLines)
    {
        var sb = new StringBuilder();
        foreach (var count in lineCountOptions)
        {
            var countStr = count.ToString(CultureInfo.InvariantCulture);
            var sel = selectedLines.Equals(countStr, StringComparison.OrdinalIgnoreCase)
                ? " selected" : string.Empty;
            sb.Append($"<option value=\"{{countStr}}\"{sel}>{{countStr}}</option>\n");
        }
        var allSel = selectedLines.Equals("All", StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
        sb.Append($"<option value=\"All\"{allSel}>All</option>\n");
        return sb.ToString();
    }

    private static string BuildStatusLabel(string selectedLines, int lineCount)
    {
        var countStr = lineCount.ToString(CultureInfo.InvariantCulture);
        return selectedLines.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? $"All {{countStr}} lines"
            : $"Last {{selectedLines}} lines ({{countStr}} shown)";
    }

    private static string BuildLogLines(string[] logLines)
    {
        var sb = new StringBuilder(capacity: logLines.Length * 120);
        foreach (var logLine in logLines)
        {
            var escaped = HttpUtility.HtmlEncode(logLine);
            var css = logLine.Contains("[ERR]", StringComparison.Ordinal) ? "line-err"
                : logLine.Contains("[WRN]", StringComparison.Ordinal)     ? "line-wrn"
                : logLine.Contains("[FTL]", StringComparison.Ordinal)     ? "line-ftl"
                : logLine.Contains("[DBG]", StringComparison.Ordinal)     ? "line-dbg"
                : "line-inf";
            sb.Append(CultureInfo.InvariantCulture, $"<span class=\"{{css}}\">{{escaped}}</span>\n");
        }
        return sb.ToString();
    }
}
