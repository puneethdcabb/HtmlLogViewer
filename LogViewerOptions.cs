// Copyright © ABB Ltd. All rights reserved.

namespace HtmlLogViewer;

/// <summary>
/// Configuration options for the ABB GDS Log Viewer.
/// Bind from the <c>"LogViewer"</c> appsettings section or pass an
/// <see cref="Action{LogViewerOptions}"/> to <see cref="LogViewerExtensions.AddLogViewer"/>.
/// </summary>
public sealed class LogViewerOptions
{
    /// <summary>
    /// URL route at which the log viewer is available.
    /// <para>Default: <c>"logs"</c>  →  <c>https://host/logs</c></para>
    /// </summary>
    public string RoutePrefix { get; set; } = "logs";

    /// <summary>
    /// Numeric line-count choices shown in the dropdown.
    /// The option <c>"All"</c> is always appended automatically.
    /// <para>Default: <c>[100, 200, 300]</c></para>
    /// </summary>
    public int[] LineCountOptions { get; set; } = [100, 200, 300];

    /// <summary>
    /// Number of lines shown on the first (default) load.
    /// Should be one of the values in <see cref="LineCountOptions"/>.
    /// <para>Default: <c>100</c></para>
    /// </summary>
    public int DefaultLineCount { get; set; } = 100;

    /// <summary>
    /// Title shown in the browser tab and the page heading.
    /// <para>Default: <c>"Log Viewer"</c></para>
    /// </summary>
    public string PageTitle { get; set; } = "Log Viewer";

    /// <summary>
    /// Additional log-file paths to include in the viewer alongside any paths
    /// auto-discovered from the <c>Serilog</c> configuration section.
    /// Useful when the application does not configure Serilog file sinks, or when
    /// you want to surface logs from other services on the same host.
    /// Each entry is the path to a Serilog-style rolling log file and may contain
    /// environment-variable placeholders (e.g. <c>%PROGRAMDATA%\MyApp\Logs\App.log</c>).
    /// </summary>
    public string[] ExtraPaths { get; set; } = [];
}
