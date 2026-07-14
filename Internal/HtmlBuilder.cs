// Copyright © Puneeth DC Ltd. All rights reserved.

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
    private static readonly Lazy<string> AlpineJs     = new(static () => LoadResource("alpine.min.js"));

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
        var statusLabel = BuildStatusLabel(selectedLines, logLines.Length, options.AllLinesLimit);

        return HtmlTemplate.Value
            .Replace("{{CSS}}",            CssContent.Value,                                              StringComparison.Ordinal)
            .Replace("{{ALPINE_JS}}",      AlpineJs.Value,                                                StringComparison.Ordinal)
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
        string selectedLines,
        int allLinesLimit = 0)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return new
        {
            selectedFile  = logFilePath,
            filesHtml     = BuildFileOptions(allFiles, logFilePath),
            logHtml       = BuildLogLines(logLines),
            statusLabel   = BuildStatusLabel(selectedLines, logLines.Length, allLinesLimit),
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

    private static string BuildStatusLabel(string selectedLines, int lineCount, int allLinesLimit = 0)
    {
        var countStr = lineCount.ToString(CultureInfo.InvariantCulture);
        if (selectedLines.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (allLinesLimit > 0 && lineCount >= allLinesLimit)
                return $"All \u25b8 last {countStr} lines shown (file too large \u2014 capped at {allLinesLimit:N0})";
            return $"All {countStr} lines";
        }
        return $"Last {selectedLines} lines ({countStr} shown)";
    }

    private static string BuildLogLines(string[] logLines)
    {
        var sb = new StringBuilder(capacity: logLines.Length * 165);
        foreach (var logLine in logLines)
        {
            var css = logLine.Contains("[ERR]", StringComparison.Ordinal) ? "line-err"
                : logLine.Contains("[WRN]", StringComparison.Ordinal)     ? "line-wrn"
                : logLine.Contains("[FTL]", StringComparison.Ordinal)     ? "line-ftl"
                : logLine.Contains("[DBG]", StringComparison.Ordinal)     ? "line-dbg"
                : "line-inf";
            sb.Append("<div class=\"log-line ");
            sb.Append(css);
            sb.Append("\">");
            AppendHtmlEncoded(sb, logLine.AsSpan());
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Writes <paramref name="text"/> into <paramref name="sb"/> replacing the five
    /// HTML-unsafe characters with their entity equivalents.
    /// No intermediate string is allocated; safe character runs are appended as
    /// <see cref="ReadOnlySpan{T}"/> slices directly onto the <see cref="StringBuilder"/>.
    /// </summary>
    private static void AppendHtmlEncoded(StringBuilder sb, ReadOnlySpan<char> text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var entity = text[i] switch
            {
                '&'  => "&amp;",
                '<'  => "&lt;",
                '>'  => "&gt;",
                '"'  => "&quot;",
                '\'' => "&#39;",
                _    => null
            };
            if (entity is null) continue;
            if (i > start) sb.Append(text[start..i]);
            sb.Append(entity);
            start = i + 1;
        }
        if (start < text.Length) sb.Append(text[start..]);
    }
}

