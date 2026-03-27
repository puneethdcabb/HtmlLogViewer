# HtmlLogViewer

A lightweight ASP.NET Core log viewer for **Serilog rolling log files**.  
Adds a single browser-accessible route that lists every rolling log file it
can discover, lets users choose how many lines to display, and renders a
self-contained HTML page with **dark / light mode toggle** — zero external
dependencies.

---

## Installation

```bash
dotnet add package HtmlLogViewer
```

---

## Quick Start

### 1 — Register the viewer

```csharp
// Program.cs
builder.Services
    .AddControllers()
    .AddLogViewer();          // reads all settings from appsettings.json "LogViewer" section
```

`MapControllers()` must also be called, as with any ASP.NET Core controller:

```csharp
app.MapControllers();
```

### 2 — Add configuration to `appsettings.json`

```json
{
  "LogViewer": {
    "RoutePrefix"      : "logs",
    "PageTitle"        : "My Application — Log Viewer",
    "DefaultLineCount" : 200,
    "LineCountOptions" : [ 100, 200, 300, 500 ],
    "ExtraPaths"       : [ "logs", "C:\\OtherApp\\Logs" ]
  }
}
```

> **Why the `Serilog` section matters**  
> The viewer auto-discovers log file paths by walking every `"path"` property
> found anywhere inside the `Serilog` configuration section.  
> If Serilog is configured **only** in code (not in `appsettings.json`), use
> `ExtraPaths` to point the viewer at the log directory instead.

### 3 — Wire Serilog from configuration *(recommended)*

```csharp
// Program.cs
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration));
```

This keeps the `"Serilog"` section as the single source of truth for both
the file sink and the viewer's path auto-discovery.

---

## `LogViewer` appsettings Reference

| Key | Type | Default | Description |
|---|---|---|---|
| `RoutePrefix` | `string` | `"logs"` | URL path for the viewer — `https://host/{RoutePrefix}` |
| `PageTitle` | `string` | `"Log Viewer"` | Browser-tab title and page `<h1>` |
| `DefaultLineCount` | `int` | `100` | Number of lines shown on first load |
| `LineCountOptions` | `int[]` | `[100, 200, 300]` | Choices in the **Lines** dropdown; `"All"` is always appended automatically |
| `ExtraPaths` | `string[]` | `[]` | Extra file or directory paths scanned for `*.log` / `*.txt` files. Supports `%ENV%` variables. Useful when Serilog is configured in code rather than `appsettings.json` |

---

## Programmatic Override

Settings from `appsettings.json` can be overridden in code via the optional
`configure` delegate. The delegate runs **after** the config section is bound,
so only the properties you touch are overridden:

```csharp
builder.Services
    .AddControllers()
    .AddLogViewer(options =>
    {
        options.PageTitle        = "My Application — Logs";
        options.DefaultLineCount = 200;
        options.LineCountOptions = [100, 200, 300, 500];
        options.ExtraPaths       = [@"C:\OtherService\Logs"];
    });
```

---

## Log File Discovery

The viewer collects candidate log paths from three sources on every request,
then merges and de-duplicates them:

| Priority | Source | How to configure |
|---|---|---|
| 1 | **Serilog auto-discovery** | Any `"path"` key anywhere in the `"Serilog"` appsettings section |
| 2 | **`ExtraPaths`** | `LogViewer.ExtraPaths` in `appsettings.json` or code |
| 3 | **Runtime directories** | Entered by the user in the **Log Directories** text box in the UI |

Each resolved path may be a **file path** (its parent directory is used) or a
**directory path** (scanned directly). All `*.log` and `*.txt` files are
collected, de-duplicated, and sorted newest-first.

---

## Log Level Colours

Log lines are coloured by severity based on the token inside `[...]`:

| Token | Meaning | Colour (dark mode) |
|---|---|---|
| `[INF]` | Information | Blue |
| `[DBG]` | Debug | Green |
| `[WRN]` | Warning | Yellow |
| `[ERR]` | Error | Red-orange |
| `[FTL]` | Fatal | Bold red |

---

## Dark / Light Mode

The viewer ships with a **dark/light mode toggle** button in the header.  
The chosen theme is persisted in `localStorage` and restored automatically
on the next page load — no server round-trip needed.

---

## License

© Copyright 2025 ABB Ltd. All rights reserved.
