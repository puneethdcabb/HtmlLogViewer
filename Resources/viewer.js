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
