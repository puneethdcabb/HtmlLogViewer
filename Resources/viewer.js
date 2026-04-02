// ── SVG icons ─────────────────────────────────────────────────────────────────
var _LV_ICON_MOON = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>';
var _LV_ICON_SUN  = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="4"/><line x1="12" y1="2" x2="12" y2="6"/><line x1="12" y1="18" x2="12" y2="22"/><line x1="4.93" y1="4.93" x2="7.76" y2="7.76"/><line x1="16.24" y1="16.24" x2="19.07" y2="19.07"/><line x1="2" y1="12" x2="6" y2="12"/><line x1="18" y1="12" x2="22" y2="12"/><line x1="4.93" y1="19.07" x2="7.76" y2="16.24"/><line x1="16.24" y1="7.76" x2="19.07" y2="4.93"/></svg>';

// Prefix embedded in the <option value> for every remote file so the client
// can detect and parse it: __remote__::{host}::{remotePath}
var _LV_REMOTE_PFX = '__remote__::';

// ── Alpine.js component factory ────────────────────────────────────────────────
function logViewer() {
    return {
        ICON_MOON: _LV_ICON_MOON,
        ICON_SUN:  _LV_ICON_SUN,

        // UI state
        isLight:       false,
        loading:       false,
        loadingRemote: false,
        autoRefresh:   false,
        autoInterval:  '10',
        _autoTimer:    null,

        // Data state (seeded from server bootstrap, updated on every fetch)
        selectedFile:  '',
        selectedLines: '',
        dirs:          '',
        remoteHosts:   '',
        remoteErrors:  [],
        logHtml:       '',
        statusLabel:   '',
        generatedTime: '',

        // ── Lifecycle ────────────────────────────────────────────────────────
        init() {
            this.isLight     = localStorage.getItem('logViewerTheme')       === 'light';
            this.remoteHosts = localStorage.getItem('logViewerRemoteHosts') || '';

            // Capture the server-rendered log HTML before Alpine.js takes over the log div
            var pre = document.getElementById('log-output');
            if (pre) this.logHtml = pre.innerHTML;

            // Seed the rest of the UI state from the server bootstrap JSON
            var b = window.__LV__;
            if (b) {
                this.selectedFile  = b.selectedFile  || '';
                this.selectedLines = b.selectedLines || '';
                this.dirs          = b.dirs          || '';
                this.statusLabel   = b.statusLabel   || '';
                this.generatedTime = b.generatedTime || '';
            }
        },

        // ── Theme ────────────────────────────────────────────────────────────
        toggleTheme() {
            this.isLight = !this.isLight;
            localStorage.setItem('logViewerTheme', this.isLight ? 'light' : 'dark');
        },

        // ── Refresh button — fetch log data AND refresh remote file list ──────
        async onRefresh() {
            await Promise.all([
                this.fetchData(),
                this.remoteHosts.trim() ? this.fetchRemoteFiles() : Promise.resolve()
            ]);
        },

        // ── Reactive log fetch (no page reload) ──────────────────────────────
        async fetchData() {
            if (this.loading) return;
            this.loading = true;

            var container = document.getElementById('log-container');
            var atBottom  = !container ||
                container.scrollTop + container.clientHeight >= container.scrollHeight - 10;

            try {
                var base     = window.location.pathname.replace(/\/+$/, '');
                var url;
                var isRemote = this.selectedFile.startsWith(_LV_REMOTE_PFX);

                if (isRemote) {
                    // Parse __remote__::{host}::{remotePath}
                    var rest  = this.selectedFile.slice(_LV_REMOTE_PFX.length);
                    var sep   = rest.indexOf('::');
                    var rHost = rest.slice(0, sep);
                    var rPath = rest.slice(sep + 2);

                    url = new URL(base + '/remote/data', window.location.origin);
                    url.searchParams.set('host',  rHost);
                    url.searchParams.set('file',  rPath);
                    url.searchParams.set('lines', this.selectedLines);
                    if (this.dirs) url.searchParams.set('dirs', this.dirs);
                } else {
                    url = new URL(base + '/data', window.location.origin);
                    url.searchParams.set('file',  this.selectedFile);
                    url.searchParams.set('lines', this.selectedLines);
                    if (this.dirs) url.searchParams.set('dirs', this.dirs);
                }

                var resp = await fetch(url.toString(), { cache: 'no-store' });
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                var data = await resp.json();

                this.logHtml       = data.logHtml       || '';
                this.statusLabel   = data.statusLabel   || '';
                this.generatedTime = data.generatedTime || '';

                // For local fetches: refresh file dropdown while preserving the remote <optgroup>
                if (!isRemote && data.filesHtml && this.$refs.fileSelect) {
                    var sel       = this.$refs.fileSelect;
                    var remoteGrp = sel.querySelector('optgroup[data-remote]');
                    sel.innerHTML = data.filesHtml;
                    if (remoteGrp) sel.appendChild(remoteGrp);
                    if (data.selectedFile) this.selectedFile = data.selectedFile;
                }

                this.$nextTick(() => {
                    if (container && atBottom) container.scrollTop = container.scrollHeight;
                });
            } catch (e) {
                console.error('[LogViewer] fetch error', e);
            } finally {
                this.loading = false;
            }
        },

        // ── Remote file list fetch ────────────────────────────────────────────
        async fetchRemoteFiles() {
            if (this.loadingRemote) return;
            this.loadingRemote = true;
            this.remoteErrors  = [];

            // Persist the current value for the next page load
            localStorage.setItem('logViewerRemoteHosts', this.remoteHosts);

            // Remove the stale remote <optgroup> (if any)
            var sel = this.$refs.fileSelect;
            if (sel) {
                var old = sel.querySelector('optgroup[data-remote]');
                if (old) sel.removeChild(old);
            }

            if (!this.remoteHosts.trim()) {
                this.loadingRemote = false;
                return;
            }

            try {
                var base = window.location.pathname.replace(/\/+$/, '');
                var url  = new URL(base + '/remote/files', window.location.origin);
                url.searchParams.set('hosts', this.remoteHosts);

                var resp = await fetch(url.toString(), { cache: 'no-store' });
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                var data = await resp.json();

                if (data.errors && data.errors.length) this.remoteErrors = data.errors;

                if (data.files && data.files.length && sel) {
                    var grp          = document.createElement('optgroup');
                    grp.label        = 'Remote Hosts';
                    grp.dataset.remote = 'true';
                    data.files.forEach(function(f) {
                        var opt         = document.createElement('option');
                        opt.value       = f.path;
                        opt.textContent = f.name;
                        grp.appendChild(opt);
                    });
                    sel.appendChild(grp);
                }
            } catch (e) {
                console.error('[LogViewer] remote files error', e);
                this.remoteErrors = [e.message];
            } finally {
                this.loadingRemote = false;
            }
        },

        // ── Auto-refresh ─────────────────────────────────────────────────────
        onAutoRefreshToggle() {
            this.autoRefresh ? this._startTimer() : this._stopTimer();
        },

        onAutoIntervalChange() {
            if (this.autoRefresh) this._startTimer();
        },

        _startTimer() {
            this._stopTimer();
            this._autoTimer = setInterval(
                () => this.fetchData(),
                parseInt(this.autoInterval, 10) * 1000
            );
        },

        _stopTimer() {
            if (this._autoTimer !== null) {
                clearInterval(this._autoTimer);
                this._autoTimer = null;
            }
        },

        // ── Scroll helpers ───────────────────────────────────────────────────
        scrollToBottom() {
            var el = document.getElementById('log-container');
            if (el) el.scrollTop = el.scrollHeight;
        }
    };
}
