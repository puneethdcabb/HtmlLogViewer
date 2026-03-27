# HtmlLogViewer

A lightweight ASP.NET Core log viewer for **Serilog rolling log files**.  
Adds a single browser-accessible route that lists every rolling log file in
the configured directory, lets users choose how many lines to display
(100 / 200 / 300 / All), and renders a dark-themed, dependency-free HTML page.

---

## Installation

```bash
dotnet add package HtmlLogViewer
```

---

## Quick Start

```csharp
// Program.cs / Startup.cs
builder.Services
    .AddControllers()
    .AddLogViewer(options =>
    {
        // Route:  https://host/logs  (default)
        options.RoutePrefix  = "logs";

        // Read the log path from appsettings.json Serilog section (default)
        options.SerilogFilePathConfigKey = "Serilog:WriteTo:1:Args:path";

        // Or supply an explicit path (overrides the config key lookup)
        // options.LogFilePath = @"%PROGRAMDATA%\MyApp\Logs\App.log";

        options.PageTitle        = "My Application — Logs";
        options.DefaultLineCount = 100;
        options.LineCountOptions = [100, 200, 300];  // "All" is appended automatically
    });
```

`MapControllers()` / `UseEndpoints(e => e.MapControllers())` must also be called,
as with any ASP.NET Core controller.

---

## Options

| Property | Type | Default | Description |
|---|---|---|---|
| `SerilogFilePathConfigKey` | `string` | `"Serilog:WriteTo:1:Args:path"` | `IConfiguration` key holding the Serilog file path |
| `LogFilePath` | `string?` | `null` | Explicit path (overrides config key). Supports `%ENV%` vars. |
| `RoutePrefix` | `string` | `"logs"` | URL route for the viewer |
| `LineCountOptions` | `int[]` | `[100, 200, 300]` | Choices in the lines dropdown; `"All"` is always added |
| `DefaultLineCount` | `int` | `100` | Lines shown on first load |
| `PageTitle` | `string` | `"Log Viewer"` | Browser tab title and page heading |

---

## How It Works

* Resolves the log directory from config (expanding environment variables).
* Discovers all `<BaseName>*.log` files (Serilog `rollingInterval: "Day"` naming).
* Default selection is the **most recently written** file.
* Opens the file with `FileShare.ReadWrite` so Serilog's write handle is never blocked.
* Returns a self-contained HTML page — no CDN, no external assets.

---

## License

© Copyright 2025 ABB Ltd. All rights reserved.
