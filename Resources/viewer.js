// ?? Theme toggle ????????????????????????????????????????????????????????????
(function () {
    var DARK  = 'dark';
    var LIGHT = 'light';
    var KEY   = 'logViewerTheme';

    var ICON_MOON = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>';
    var ICON_SUN  = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="4"/><line x1="12" y1="2" x2="12" y2="6"/><line x1="12" y1="18" x2="12" y2="22"/><line x1="4.93" y1="4.93" x2="7.76" y2="7.76"/><line x1="16.24" y1="16.24" x2="19.07" y2="19.07"/><line x1="2" y1="12" x2="6" y2="12"/><line x1="18" y1="12" x2="22" y2="12"/><line x1="4.93" y1="19.07" x2="7.76" y2="16.24"/><line x1="16.24" y1="7.76" x2="19.07" y2="4.93"/></svg>';

    function applyTheme(theme) {
        var isLight = theme === LIGHT;
        document.body.classList.toggle('light-mode', isLight);
        document.getElementById('theme-icon').innerHTML   = isLight ? ICON_SUN : ICON_MOON;
        document.getElementById('theme-label').textContent = isLight ? 'Light' : 'Dark';
        localStorage.setItem(KEY, theme);
    }

    window.toggleTheme = function () {
        applyTheme(localStorage.getItem(KEY) === LIGHT ? DARK : LIGHT);
    };

    // Restore saved preference immediately (before first paint)
    applyTheme(localStorage.getItem(KEY) === LIGHT ? LIGHT : DARK);
}());

// ?? Refresh ??????????????????????????????????????????????????????????????????
function doRefresh() {
    var file  = document.getElementById('file-select').value;
    var lines = document.getElementById('lines-select').value;
    var dirs  = document.getElementById('dirs-input').value.trim();
    var url   = window.location.pathname
              + '?file='  + encodeURIComponent(file)
              + '&lines=' + encodeURIComponent(lines);
    if (dirs) url += '&dirs=' + encodeURIComponent(dirs);
    window.location.href = url;
}
