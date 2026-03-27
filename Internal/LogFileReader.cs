// Copyright © ABB Ltd. All rights reserved.

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HtmlLogViewer.Internal;

/// <summary>
/// File-system helpers for discovering and reading Serilog rolling log files.
/// All members are <c>internal</c> so they can be unit-tested from within the assembly
/// while remaining hidden from consumers of the NuGet package.
/// </summary>
internal static class LogFileReader
{
    // -------------------------------------------------------------------------
    // Serilog path discovery
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads the entire <c>Serilog</c> section from <paramref name="configuration"/> as a
    /// JSON object, then recursively traverses every property and every element of every
    /// array until it finds all string properties named <c>"path"</c>.
    /// Returns the collected raw path values (environment variables not yet expanded).
    /// </summary>
    internal static List<string> FindSerilogLogPaths(IConfiguration configuration)
    {
        var serilogSection = configuration.GetSection("Serilog");
        if (!serilogSection.Exists())
            return [];

        // Convert the IConfigurationSection tree into a JsonElement so we can walk it
        // exactly as the user requested: "read as a JSON object and go through all properties
        // and properties of all array objects".
        var json = BuildJsonFromSection(serilogSection);

        var paths = new List<string>();
        CollectPathValues(json, paths);
        return paths;
    }

    /// <summary>
    /// Recursively serialises an <see cref="IConfigurationSection"/> into a
    /// <see cref="JsonElement"/>, preserving nested objects and arrays.
    /// </summary>
    private static JsonElement BuildJsonFromSection(IConfigurationSection section)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
            WriteConfigNode(writer, section);

        ms.Position = 0;
        // Clone so the JsonDocument can be disposed while the element lives on.
        return JsonDocument.Parse(ms).RootElement.Clone();
    }

    private static void WriteConfigNode(Utf8JsonWriter writer, IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();

        if (children.Count == 0)
        {
            // Leaf node — write its scalar value.
            writer.WriteStringValue(section.Value ?? string.Empty);
            return;
        }

        // Detect arrays: IConfiguration represents them with integer string keys (0, 1, 2 …).
        bool isArray = children.All(c => int.TryParse(c.Key, out _));

        if (isArray)
        {
            writer.WriteStartArray();
            foreach (var child in children)
                WriteConfigNode(writer, child);
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteStartObject();
            foreach (var child in children)
            {
                writer.WritePropertyName(child.Key);
                WriteConfigNode(writer, child);
            }
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Recursively walks a <see cref="JsonElement"/> (objects and arrays) and collects every
    /// string property whose name is <c>"path"</c> (case-insensitive).
    /// </summary>
    private static void CollectPathValues(JsonElement element, List<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "path", StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            paths.Add(val);
                    }
                    // Always recurse — a "path" object could itself contain nested "path" keys.
                    CollectPathValues(prop.Value, paths);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectPathValues(item, paths);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // File discovery
    // -------------------------------------------------------------------------

    /// <summary>File extensions scanned in every resolved log directory.</summary>
    private static readonly string[] LogFilePatterns = ["*.log", "*.txt"];

    /// <summary>
    /// Resolves every entry in <paramref name="logFilePaths"/> (expanding environment
    /// variables) to a directory, then collects ALL <c>*.log</c> and <c>*.txt</c> files
    /// found there — no base-name filtering is applied.
    /// Each path entry may be:
    /// <list type="bullet">
    ///   <item>A file path   — its parent directory is used (<c>C:\Logs\App.log</c>).</item>
    ///   <item>A directory path — used directly            (<c>C:\Logs\</c>).</item>
    /// </list>
    /// Results are de-duplicated across overlapping sources and returned sorted
    /// newest-first by last-write time.
    /// </summary>
    internal static FileInfo[] GetAllLogFiles(IEnumerable<string> logFilePaths)
    {
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FileInfo>();

        foreach (var rawPath in logFilePaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                continue;

            var expanded = Environment.ExpandEnvironmentVariables(rawPath);

            // Resolve to a directory:
            //   • already a directory  → use it directly
            //   • a file path          → use its parent directory
            var dir = Directory.Exists(expanded)
                ? expanded
                : Path.GetDirectoryName(expanded);

            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                continue;

            foreach (var pattern in LogFilePatterns)
            {
                foreach (var filePath in Directory.GetFiles(dir, pattern))
                {
                    if (seen.Add(filePath))
                        result.Add(new FileInfo(filePath));
                }
            }
        }

        return [.. result.OrderByDescending(f => f.LastWriteTimeUtc)];
    }

    // -------------------------------------------------------------------------
    // File selection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the user-requested file to one of the known <paramref name="allFiles"/> entries
    /// by matching its <see cref="FileInfo.FullName"/> exactly against
    /// <paramref name="requestedFullPath"/>.
    /// Falls back to the most recently written file (index 0) when the request is absent or
    /// does not match any known file. Matching against the pre-built list means no path-traversal
    /// is possible regardless of the value supplied by the client.
    /// </summary>
    internal static string ResolveSelectedFile(FileInfo[] allFiles, string? requestedFullPath)
    {
        if (allFiles.Length == 0)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(requestedFullPath))
        {
            var candidate = allFiles.FirstOrDefault(
                f => string.Equals(f.FullName, requestedFullPath, StringComparison.OrdinalIgnoreCase));

            if (candidate is not null)
                return candidate.FullName;
        }

        return allFiles[0].FullName; // default: latest file
    }

    // -------------------------------------------------------------------------
    // File reading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads lines from <paramref name="filePath"/>.
    /// Opens with <see cref="FileShare.ReadWrite"/> so Serilog's open write handle is never
    /// blocked. When <paramref name="lineCount"/> is <c>null</c>, all lines are returned;
    /// otherwise the last <paramref name="lineCount"/> lines are returned.
    /// </summary>
    internal static string[] ReadLines(string filePath, int? lineCount)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return ["No log file available."];

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            // ── "All lines" path: stream the whole file ──────────────────────────
                if (lineCount is null)
                {
                    var all = new List<string>();
                    string? l;
                    while ((l = reader.ReadLine()) != null)
                        all.Add(l);
                    return [.. all];
                }

                // ── Tail path: circular buffer ────────────────────────────────────
                // Only the last N lines are ever allocated; the full file content
                // never enters memory regardless of file size.
                int     cap     = lineCount.Value;
                var     ring    = new string[cap];
                int     head    = 0;
                int     written = 0;
                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    ring[head] = line;
                    head       = (head + 1) % cap;
                    written++;
                }

                int filled = Math.Min(written, cap);
                int start  = written >= cap ? head : 0;
                var result = new string[filled];
                for (int i = 0; i < filled; i++)
                    result[i] = ring[(start + i) % cap];
                return result;
        }
        catch (IOException ex)
        {
            return [$"Error reading log file: {ex.Message}"];
        }
        catch (UnauthorizedAccessException ex)
        {
            return [$"Access denied reading log file: {ex.Message}"];
        }
    }
}
