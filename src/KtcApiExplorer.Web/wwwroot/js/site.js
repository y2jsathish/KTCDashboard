/* Shared helpers: CSRF-aware fetch, toasts, theme toggle, KTC token badge. */
window.App = (function ($) {
  var csrf = $('meta[name="csrf"]').attr('content');

  function esc(s) { return $('<div>').text(s == null ? '' : String(s)).html(); }

  function toast(msg, kind) {
    var $t = $('<div class="toast align-items-center text-bg-' + (kind || 'secondary') + ' border-0" role="alert"><div class="d-flex"><div class="toast-body"></div>' +
      '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button></div></div>');
    $t.find('.toast-body').text(msg);
    $('#toasts').append($t);
    var t = new bootstrap.Toast($t[0], { delay: kind === 'danger' ? 7000 : 3500 });
    $t.on('hidden.bs.toast', function () { $t.remove(); });
    t.show();
  }

  /** api('POST','/api/x', obj) -> jqXHR. Errors are normalised to {code,message,status} and shown unless opts.silent. */
  function api(method, url, data, opts) {
    opts = opts || {};
    var d = $.Deferred();
    $.ajax({
      url: url, method: method, dataType: 'json', contentType: 'application/json; charset=utf-8',
      data: data === undefined ? undefined : JSON.stringify(data),
      headers: { 'RequestVerificationToken': csrf }
    }).done(function (r) { d.resolve(r); })
      .fail(function (xhr) {
        if (xhr.status === 401) { window.location = '/Account/Login?returnUrl=' + encodeURIComponent(location.pathname + location.search); return; }
        var e = (xhr.responseJSON && xhr.responseJSON.error) || { code: 'error', message: xhr.status === 429 ? 'Too many requests - slow down.' : 'Request failed (' + xhr.status + ').' };
        e.status = xhr.status;
        if (!opts.silent) toast(e.message, 'danger');
        d.reject(e);
      });
    return d.promise();
  }

  function fmtMs(n) { return n == null ? '-' : n < 1000 ? Math.round(n) + ' ms' : (n / 1000).toFixed(2) + ' s'; }
  function fmtBytes(n) { return n == null ? '-' : n < 1024 ? n + ' B' : n < 1048576 ? (n / 1024).toFixed(1) + ' KB' : (n / 1048576).toFixed(2) + ' MB'; }
  function fmtDate(s) { return s ? new Date(s.endsWith('Z') ? s : s + 'Z').toLocaleString() : ''; }
  function mm(secs) { secs = Math.max(0, Math.floor(secs)); return Math.floor(secs / 60) + ':' + ('0' + (secs % 60)).slice(-2); }
  function methodBadge(m) { return '<span class="m-badge m-' + esc(m) + '">' + esc(m) + '</span>'; }
  function statusClass(code) { return !code ? 'danger' : code < 300 ? 'success' : code < 400 ? 'info' : code < 500 ? 'warning' : 'danger'; }

  function copy(text, label) {
    function ok() { toast((label || 'Copied') + ' to clipboard', 'success'); }
    if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text).then(ok, fallback); } else { fallback(); }
    function fallback() {
      var $ta = $('<textarea style="position:fixed;opacity:0">').val(text).appendTo('body'); $ta[0].select();
      try { document.execCommand('copy'); ok(); } catch (e) { toast('Copy failed - select and copy manually', 'danger'); } $ta.remove();
    }
  }

  function download(name, text, mime) {
    var blob = text instanceof Blob ? text : new Blob([text], { type: mime || 'text/plain' });
    var a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = name; document.body.appendChild(a); a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 500);
  }

  function chartColors() {
    var s = getComputedStyle(document.documentElement);
    var dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
    return { text: dark ? '#c9d1d9' : '#495057', grid: dark ? 'rgba(255,255,255,.1)' : 'rgba(0,0,0,.08)', primary: dark ? '#6ea8fe' : '#0d6efd', ok: dark ? '#4cc38a' : '#198754', bad: dark ? '#ff7b72' : '#dc3545', warn: '#f0ad4e' };
  }

  // declarative confirm for forms (inline handlers are blocked by the CSP)
  $(document).on('submit', 'form[data-confirm]', function (e) { if (!window.confirm($(this).data('confirm'))) e.preventDefault(); });

  /** Renders a DiffResultDto into the shared #diffModal */
  function showDiff(diff, title) {
    var $m = $('#diffModal'); $m.find('.modal-title').text(title || 'Response comparison');
    var $b = $m.find('.modal-body').empty();
    if (diff.areEqual) $b.append('<div class="alert alert-success mb-0"><i class="bi bi-check-circle"></i> The two responses are identical.</div>');
    else {
      $b.append('<div class="mb-2 small text-body-secondary">' + diff.differences.length + (diff.truncated ? '+' : '') + ' difference(s)</div>');
      var rows = diff.differences.map(function (d) {
        return '<tr><td class="mono text-break">' + esc(d.path) + '</td><td class="diff-' + esc(d.type) + '">' + esc(d.type) + '</td><td class="mono text-break">' + esc(d.left == null ? '' : d.left) + '</td><td class="mono text-break">' + esc(d.right == null ? '' : d.right) + '</td></tr>';
      }).join('');
      $b.append('<div class="table-responsive"><table class="table table-sm"><thead><tr><th>Path</th><th>Change</th><th>Left (older)</th><th>Right (newer)</th></tr></thead><tbody>' + rows + '</tbody></table></div>');
    }
    bootstrap.Modal.getOrCreateInstance($m[0]).show();
  }

  // ---- theme ----
  function applyTheme(t) {
    document.documentElement.setAttribute('data-bs-theme', t);
    try { localStorage.setItem('theme', t); } catch (e) { }
    $('#themeToggle i').attr('class', t === 'dark' ? 'bi bi-sun' : 'bi bi-moon-stars');
    $(document).trigger('themechange');
  }
  $('#themeToggle').on('click', function () { applyTheme(document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark'); });
  $('#themeToggle i').attr('class', document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'bi bi-sun' : 'bi bi-moon-stars');

  // ---- KTC token badge / expiry countdown / auto refresh ----
  var ktc = { status: null, expires: 0, refreshing: false };
  function renderBadge() {
    var s = ktc.status, $b = $('#ktcBadge'), $t = $('#ktcBadgeText');
    if (!$b.length) return;
    $b.removeClass('ktc-none ktc-ok ktc-warning ktc-expired');
    if (!s || !s.isLoggedIn) { $b.addClass(s && s.state === 'expired' ? 'ktc-expired' : 'ktc-none'); $t.text(s && s.state === 'expired' ? 'KTC: session expired' : 'KTC: not signed in'); return; }
    var left = (ktc.expires - Date.now()) / 1000;
    var state = left <= 0 ? 'expired' : left <= 120 ? 'warning' : 'ok';
    $b.addClass('ktc-' + state);
    $t.text('KTC: ' + s.userName + ' · ' + (left > 0 ? mm(left) : 'expired') + (s.canAutoRefresh ? ' ⟳' : ''));
    $b.attr('title', 'Token expires ' + new Date(ktc.expires).toLocaleTimeString() + (s.canAutoRefresh ? ' - renewed automatically' : ' - sign in again when it expires'));
    if (left < 60 && s.canAutoRefresh && !ktc.refreshing) refresh();
  }
  function setStatus(s) { ktc.status = s; ktc.expires = s && s.expiresUtc ? Date.parse(s.expiresUtc.endsWith('Z') ? s.expiresUtc : s.expiresUtc + 'Z') : 0; renderBadge(); $(document).trigger('ktcstatus', [s]); }
  function loadStatus() { return api('GET', '/api/ktc/status', undefined, { silent: true }).done(setStatus); }
  function refresh() {
    ktc.refreshing = true;
    return api('POST', '/api/ktc/refresh', undefined, { silent: true }).done(setStatus).fail(function () { loadStatus(); }).always(function () { ktc.refreshing = false; });
  }
  if ($('#ktcBadge').length) { loadStatus(); setInterval(renderBadge, 1000); setInterval(loadStatus, 60000); }

  return { api: api, esc: esc, toast: toast, fmtMs: fmtMs, fmtBytes: fmtBytes, fmtDate: fmtDate, methodBadge: methodBadge, statusClass: statusClass,
           copy: copy, download: download, showDiff: showDiff, reloadKtcStatus: loadStatus, chartColors: chartColors, ktc: ktc, setKtcStatus: setStatus, refreshKtc: refresh, mm: mm };
})(jQuery);
