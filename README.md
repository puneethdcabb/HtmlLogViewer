# HtmlLogViewer

A lightweight, reactive ASP.NET Core log viewer for **Serilog rolling log files**.  
Registers a browser-accessible route that auto-discovers every rolling log file,
supports configurable tail-line selection (100 / 200 / 300 / All), dark & light
themes, auto-refresh, and multi-node remote log aggregation — all with **zero
external runtime dependencies** (Alpine.js is embedded; no CDN required).

---

## Requirements

| | |
|---|---|
| **Runtime** | .NET 10 or later |
| **Framework** | ASP.NET Core (any host that calls `MapControllers()`) |
| **Logging** | Serilog with a `rollingInterval` file sink — or any directory of `*.log` / `*.txt` files |

---

## Installation

```bash
dotnet add package HtmlLogViewer
```

---

## Quick Start

```csharp
// Program.cs
builder.Services
    .AddControllers()
    .AddLogViewer();    // all settings from the "LogViewer" section of appsettings.json

app.MapControllers();
```

Navigate to `https://host/logs` (or the configured `RoutePrefix`).

### Programmatic configuration

```csharp
builder.Services
    .AddControllers()
    .AddLogViewer(o =>
    {
        o.RoutePrefix      = "logs";
        o.PageTitle        = "My App — Logs";
        o.DefaultLineCount = 100;
        o.LineCountOptions = [100, 200, 500];  // "All" is always appended automatically
        o.AllLinesLimit    = 10_000;           // safety cap for "All" on large files
        o.ExtraPaths       = [@"D:\OtherService\Logs"];
    });
```

### `appsettings.json`

```json
{
  "LogViewer": {
    "RoutePrefix"             : "logs",
    "PageTitle"               : "My App — Logs",
    "DefaultLineCount"        : 100,
    "LineCountOptions"        : [100, 200, 500],
    "AllLinesLimit"           : 10000,
    "ExtraPaths"              : ["D:\\OtherService\\Logs"],
    "SkipRemoteSslValidation" : false
  }
}
```

---

## Configuration Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `RoutePrefix` | `string` | `"logs"` | URL path for the viewer — e.g. `https://host/logs` |
| `PageTitle` | `string` | `"Log Viewer"` | Browser tab title and page heading |
| `LineCountOptions` | `int[]` | `[100, 200, 300]` | Tail-line choices in the dropdown; `"All"` is always appended |
| `DefaultLineCount` | `int` | `100` | Lines shown on first load |
| `AllLinesLimit` | `int` | `10000` | Maximum lines returned when `"All"` is selected. Prevents out-of-memory on large files. Set `0` to disable _(not recommended above ~50 MB)_ |
| `ExtraPaths` | `string[]` | `[]` | Extra log directories or file paths scanned alongside Serilog auto-discovered paths. Supports `%ENV%` variables |
| `SkipRemoteSslValidation` | `bool` | `false` | When `true`, TLS certificate errors (self-signed, expired, hostname mismatch) are **ignored** when proxying requests to remote LogViewer nodes. ⚠ Only enable on trusted internal networks |

---

## Log File Discovery

The viewer collects log files from three sources, de-duplicates, and sorts newest-first:

1. **Serilog auto-discovery** — walks the entire `Serilog` section of `appsettings.json`
   recursively and collects every string value whose key is `"path"`, at any depth.
2. **`ExtraPaths`** — additional directories or file paths from `LogViewerOptions`.
3. **Log Directories UI field** — extra paths typed at runtime
   (semicolon-separated, e.g. `C:\Logs\;D:\Other\`).

All paths are resolved to their parent directory, scanned for `*.log` and `*.txt`
files, and merged with de-duplication.

---

## Large File Handling

Selecting `"All"` on a multi-hundred-MB file would exhaust server memory with a
naïve full-file read. The viewer uses a **circular ring buffer** for all tail reads:
only the last `N` lines are ever allocated, regardless of file size.

When `AllLinesLimit` is active and the file is larger than the cap, the status bar
shows a transparent warning:

> `All ▸ last 10,000 lines shown (file too large — capped at 10,000)`

Set `AllLinesLimit: 0` to disable the cap — only safe for files under ~50 MB.

---

## Remote Hosts

The viewer can aggregate log files from **multiple nodes** that also run the
`HtmlLogViewer` NuGet package. Enter the remote nodes in the **Remote Hosts**
text field (semicolon-separated). On **Refresh**, the local server proxies a
file-list request to each node and merges the results into the `Log file` dropdown
under a **"Remote Hosts"** group. Selecting a remote file proxies the data request
server-side — the browser only ever talks to the local node.

### Host entry formats

```
node1:8932                     →  https://node1:8932  /  logs  (default prefix)
node1:8932/customlogs          →  https://node1:8932  /  customlogs
https://node1:8932/api/logs    →  https://node1:8932  /  api/logs
http://node1:8932              →  http://node1:8932   /  logs  (explicit HTTP)
```

| Rule | Detail |
|---|---|
| **Default scheme** | HTTPS is used when no scheme is specified |
| **Default prefix** | Falls back to the local instance's `RoutePrefix` (`logs`) when no `/prefix` suffix is given |
| **Display name** | Hostname without port, e.g. `[node1] App20250115.log` |
| **Persistence** | Remote host entries are saved in the browser's `localStorage` and restored on the next page load |
| **Multiple nodes** | Separate entries with `;`, e.g. `node1:8932;node2:9920/customlogs;http://dev3:5000` |

### Bypassing TLS errors on remote nodes

Internal nodes often run with self-signed certificates. Set `SkipRemoteSslValidation: true`
in `appsettings.json` on the **local (aggregating) node** to ignore TLS errors when
proxying to remote nodes:

```json
"LogViewer": {
  "SkipRemoteSslValidation": true
}
```

> ⚠ **Warning:** `SkipRemoteSslValidation` disables certificate-chain validation
> entirely for all remote LogViewer proxy requests on this node.  
> Only enable it on isolated, trusted internal networks where the risk is accepted.

### How the proxy works

All remote data is **server-side proxied** — the browser only ever talks to the
local node. Benefits:

- No browser CORS issues regardless of the remote origin.
- Remote nodes require **no CORS configuration**.
- The local node must be able to reach each remote node over HTTP/HTTPS on the
  network level.

### Remote API endpoints

Every node hosting `HtmlLogViewer` exposes these routes:

| Method | Route | Description |
|---|---|---|
| `GET` | `/{prefix}` | Full reactive HTML page (initial load) |
| `GET` | `/{prefix}/data` | JSON — log lines + updated file list |
| `GET` | `/{prefix}/files` | JSON — lightweight file list (used by remote aggregation) |
| `GET` | `/{prefix}/remote/files?hosts=…` | JSON — proxied & merged file list from remote nodes |
| `GET` | `/{prefix}/remote/data?host=…&file=…&lines=…` | JSON — proxied log data from a single remote node |

---

## UI Features

| Feature | Detail |
|---|---|
| **Reactive updates** | Changing file, lines, or clicking Refresh fetches only the log data — the page never fully reloads |
| **Auto-refresh** | Checkbox with interval selector (5 s / 10 s / 30 s / 60 s) for continuous live-tail |
| **Dark / Light theme** | Toggle persisted in `localStorage` |
| **Scroll-to-bottom** | Floating `↓` button; auto-pins to bottom on refresh when already at the bottom |
| **Large-file rendering** | Each log line uses CSS `content-visibility: auto` — the browser skips layout/paint for off-screen rows (CSS-native virtual scrolling, no JS needed) |
| **Offline / air-gapped** | Alpine.js is **embedded as an assembly resource** — no CDN request is made at runtime |
| **Remote errors** | Per-node connection errors are shown in a coloured banner below the controls |

---

## How It Works

1. `AddLogViewer()` registers `LogViewerController` from the library assembly, applies
   the route convention using `LogViewerOptions.RoutePrefix`, and registers a named
   `HttpClient` (`"logviewer-remote"`) for the server-side remote proxy.
2. The `HttpClient`'s primary handler is resolved from DI so `SkipRemoteSslValidation`
   is honoured at startup without any restart of the handler factory.
3. On each request, log-file paths are gathered from Serilog config, `ExtraPaths`, and
   the runtime UI field, then files are opened with `FileShare.ReadWrite` so Serilog's
   write handle is never blocked.
4. For tail reads, a **ring buffer** of exactly `lineCount` slots is allocated — the
   whole file never enters heap memory regardless of size.
5. The HTML template, CSS, JS, and the Alpine.js bundle are all **embedded assembly
   resources** loaded lazily on first use via `Lazy<string>`.
6. Each log line is rendered as a `<div class="log-line">` using a zero-allocation
   span-based HTML encoder — no intermediate `string` is allocated per line.

---

## License

© Copyright 2025 ABB Ltd. All rights reserved.
