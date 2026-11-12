(function ($) {
  var skip = 0, take = 25, total = 0, sel = [];
  function load() {
    var ok = $('#fOk').val(); var url = '/api/library/history?skip=' + skip + '&take=' + take + (ok ? '&success=' + ok : '');
    App.api('GET', url).done(function (r) {
      total = r.total; sel = [];
      $('#rows').html(r.items.length ? r.items.map(function (x) {
        return '<tr><td><input type="checkbox" class="form-check-input pick" data-id="' + x.id + '" aria-label="Select for compare"></td><td class="small text-nowrap">' + App.esc(App.fmtDate(x.executedUtc)) + '</td><td>' + App.methodBadge(x.method) + '</td>' +
          '<td class="mono text-break" style="max-width:460px">' + App.esc(x.url) + '</td><td><span class="badge text-bg-' + (x.statusCode ? App.statusClass(x.statusCode) : 'danger') + '">' + (x.statusCode || 'ERR') + '</span></td>' +
          '<td class="text-end small">' + App.fmtMs(x.durationMs) + '</td><td class="small">' + App.esc(x.source) + '</td><td><button class="btn btn-sm btn-outline-secondary view" data-id="' + x.id + '"><i class="bi bi-eye"></i></button>' +
          (x.endpointId ? ' <a class="btn btn-sm btn-outline-secondary" href="/Explorer?endpoint=' + x.endpointId + '" title="Open in Explorer"><i class="bi bi-box-arrow-up-right"></i></a>' : '') + '</td></tr>'; }).join('')
        : '<tr><td colspan="8" class="text-center py-4 text-body-secondary">No executions yet.</td></tr>');
      $('#pageInfo').text(total ? (skip + 1) + '–' + Math.min(skip + take, total) + ' of ' + total : '');
      $('#prev').prop('disabled', skip === 0); $('#next').prop('disabled', skip + take >= total); $('#btnCompare').prop('disabled', true);
    });
  }
  $('#fOk').on('change', function () { skip = 0; load(); });
  $('#prev').on('click', function () { skip = Math.max(0, skip - take); load(); });
  $('#next').on('click', function () { skip += take; load(); });
  $('#rows').on('change', '.pick', function () {
    sel = $('#rows .pick:checked').map(function () { return +$(this).data('id'); }).get();
    if (sel.length > 2) { $(this).prop('checked', false); sel = sel.slice(0, 2); App.toast('Select exactly two executions.', 'warning'); }
    $('#btnCompare').prop('disabled', sel.length !== 2);
  });
  $('#btnCompare').on('click', function () {
    var ids = sel.slice().sort(function (a, b) { return a - b; });   // older on the left
    App.api('GET', '/api/library/history/compare?left=' + ids[0] + '&right=' + ids[1]).done(function (d) { App.showDiff(d, 'Execution #' + ids[0] + ' vs #' + ids[1]); });
  });
  $('#rows').on('click', '.view', function () {
    App.api('GET', '/api/library/history/' + $(this).data('id')).done(function (d) {
      function pre(t) { return '<pre class="code" style="max-height:300px">' + App.esc(t || '(none)') + '</pre>'; }
      $('#detailModal .modal-body').html('<div class="mb-2">' + App.methodBadge(d.method) + ' <span class="mono">' + App.esc(d.url) + '</span></div><div class="mb-2"><span class="badge text-bg-' + (d.statusCode ? App.statusClass(d.statusCode) : 'danger') + '">' + (d.statusCode || 'ERR') + '</span> ' + App.fmtMs(d.durationMs) + ' · ' + App.esc(App.fmtDate(d.executedUtc)) + (d.errorMessage ? ' · <span class="text-danger">' + App.esc(d.errorMessage) + '</span>' : '') + '</div>' +
        '<div class="row g-3"><div class="col-md-6"><div class="fw-semibold small">Request headers</div>' + pre(d.requestHeadersJson) + '<div class="fw-semibold small mt-2">Request body</div>' + pre(d.requestBody) + '</div>' +
        '<div class="col-md-6"><div class="fw-semibold small">Response headers</div>' + pre(d.responseHeadersJson) + '<div class="fw-semibold small mt-2">Response body (masked)</div>' + pre(d.responseBody) + '</div></div>');
      new bootstrap.Modal('#detailModal').show();
    });
  });
  load();
})(jQuery);
