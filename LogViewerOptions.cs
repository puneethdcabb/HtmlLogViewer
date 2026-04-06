// Copyright © Puneeth DC Ltd. All rights reserved.

namespace HtmlLogViewer;

/// <summary>
/// Configuration options for the Puneeth DC GDS Log Viewer.
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

    /// <summary>
    /// Hard cap on the number of lines returned when the user selects <c>"All"</c>.
    /// Without this guard a very large log file (hundreds of MB) can exhaust server
    /// memory and freeze the browser tab trying to render the result.
    /// <para>
    /// The last <c>AllLinesLimit</c> lines of the file are returned; the status bar
    /// shows a warning when the file was larger than the cap.
    /// </para>
    /// <para>Set to <c>0</c> to disable the cap — only safe for files under ~50 MB.</para>
    /// <para>Default: <c>10 000</c></para>
    /// </summary>
    public int AllLinesLimit { get; set; } = 10_000;

    /// <summary>
    /// When <c>true</c>, TLS/SSL certificate errors (expired, self-signed, hostname mismatch)
    /// are ignored when the log viewer proxies requests to remote host instances.
    /// <para><b>Warning:</b> only enable this on trusted internal networks.</para>
    /// <para>
    /// Configurable from <c>appsettings.json</c>:
    /// <code>
    /// "LogViewer": { "SkipRemoteSslValidation": true }
    /// </code>
    /// </para>
    /// <para>Default: <c>false</c></para>
    /// </summary>
    public bool SkipRemoteSslValidation { get; set; } = false;
}
