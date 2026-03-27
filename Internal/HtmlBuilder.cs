// Copyright © ABB Ltd. All rights reserved.

using System.Globalization;
using System.Text;
using System.Text.Json;
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
    // ── Embedded templates — loaded lazily on first use ──────────────────────
    private static readonly Lazy<string> HtmlTemplate = new(static () => LoadResource("viewer.html"));
    private static readonly Lazy<string> CssContent   = new(static () => LoadResource("viewer.css"));
    private static readonly Lazy<string> JsContent    = new(static () => LoadResource("viewer.js"));

    private static string LoadResource(string fileName)
    {
        var assembly     = typeof(HtmlBuilder).Assembly;
        var resourceName = $"HtmlLogViewer.Resources.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'. " +
                $"Available: [{string.Join(", ", assembly.GetManifestResourceNames())}]");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── Per-request HTML page (initial full render) ───────────────────────────
    internal static string Build(
        string[] logLines,
        string logFilePath,
        FileInfo[] allFiles,
        string selectedLines,
        string userDirs,
        LogViewerOptions options)
    {
        var now         = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var statusLabel = BuildStatusLabel(selectedLines, logLines.Length);

        return HtmlTemplate.Value
            .Replace("{{CSS}}",            CssContent.Value,                                              StringComparison.Ordinal)
            .Replace("{{JS}}",             JsContent.Value,                                               StringComparison.Ordinal)
            .Replace("{{PAGE_TITLE}}",     HttpUtility.HtmlEncode(options.PageTitle),                 StringComparison.Ordinal)
            .Replace("{{FILE_OPTIONS}}",   BuildFileOptions(allFiles, logFilePath),                   StringComparison.Ordinal)
            .Replace("{{LINES_OPTIONS}}",  BuildLinesOptions(options.LineCountOptions, selectedLines), StringComparison.Ordinal)
            .Replace("{{LOG_LINES}}",      BuildLogLines(logLines),                                   StringComparison.Ordinal)
            .Replace("{{BOOTSTRAP_JSON}}", BuildBootstrapJson(logFilePath, selectedLines,
                                               userDirs, statusLabel, now),                           StringComparison.Ordinal);
    }

    // ── JSON response for the reactive data endpoint ──────────────────────────
    internal static object BuildApiResponse(
        string[] logLines,
        string logFilePath,
        FileInfo[] allFiles,
        string selectedLines)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return new
        {
            selectedFile  = logFilePath,
            filesHtml     = BuildFileOptions(allFiles, logFilePath),
            logHtml       = BuildLogLines(logLines),
            statusLabel   = BuildStatusLabel(selectedLines, logLines.Length),
            generatedTime = now
        };
    }

    // ── Fragment builders ─────────────────────────────────────────────────────

    private static string BuildBootstrapJson(
        string logFilePath,
        string selectedLines,
        string userDirs,
        string statusLabel,
        string generatedTime)
    {
        var data = new
        {
            selectedFile  = logFilePath,
            selectedLines,
            dirs          = userDirs,
            statusLabel,
            generatedTime
        };

        return JsonSerializer.Serialize(data);
    }

    private record struct FileEntry(string Path, string Name);

    private static string BuildFileOptions(FileInfo[] allFiles, string logFilePath)
    {
        var entries = GetFileEntries(allFiles);
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            var sel = string.Equals(e.Path, logFilePath, StringComparison.OrdinalIgnoreCase)
                ? " selected" : string.Empty;
            sb.Append(
                $"<option value=\"{HttpUtility.HtmlAttributeEncode(e.Path)}\"{sel}>" +
                $"{HttpUtility.HtmlEncode(e.Name)}</option>\n");
        }
        return sb.ToString();
    }

    private static string BuildLinesOptions(int[] lineCountOptions, string selectedLines)
    {
        var sb = new StringBuilder();
        foreach (var count in lineCountOptions)
        {
            var countStr = count.ToString(CultureInfo.InvariantCulture);
            var sel = selectedLines.Equals(countStr, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
            sb.Append($"<option value=\"{countStr}\"{sel}>{countStr}</option>\n");
        }
        var allSel = selectedLines.Equals("All", StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
        sb.Append($"<option value=\"All\"{allSel}>All</option>\n");
        return sb.ToString();
    }

    private static List<FileEntry> GetFileEntries(FileInfo[] allFiles)
    {
        var duplicateNames = allFiles
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allFiles.Select(fi =>
        {
            var display = duplicateNames.Contains(fi.Name)
                ? $"{fi.Name} ({fi.Directory?.Name ?? string.Empty})"
                : fi.Name;
            return new FileEntry(fi.FullName, display);
        }).ToList();
    }

    private static string BuildStatusLabel(string selectedLines, int lineCount)
    {
        var countStr = lineCount.ToString(CultureInfo.InvariantCulture);
        return selectedLines.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? $"All {countStr} lines"
            : $"Last {selectedLines} lines ({countStr} shown)";
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
            sb.Append(CultureInfo.InvariantCulture, $"<span class=\"{css}\">{escaped}</span>\n");
        }
        return sb.ToString();
    }
}

