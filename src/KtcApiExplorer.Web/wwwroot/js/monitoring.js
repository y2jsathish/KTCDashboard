(function ($) {
  var chart, trend = [];
  function dot(r) {
    if (r.lastSuccess == null) return '<span class="badge text-bg-secondary">no data</span>';
    return r.lastSuccess ? '<span class="badge text-bg-success"><i class="bi bi-check-lg"></i> Healthy</span>' : '<span class="badge text-bg-danger"><i class="bi bi-x-lg"></i> Failing</span>';
  }
  function render(m) {
    $('#rows').html(m.rows.length ? m.rows.map(function (r) {
      return '<tr><td>' + dot(r) + '</td><td>' + App.methodBadge(r.method) + ' <a class="mono text-decoration-none" href="/Explorer?endpoint=' + r.endpointId + '">' + App.esc(r.route) + '</a></td>' +
        '<td>' + (r.lastStatus || '-') + '</td><td class="text-end">' + (r.lastDurationMs == null ? '-' : App.fmtMs(r.lastDurationMs)) + '</td><td class="text-end">' + (r.samples ? App.fmtMs(r.avgMs) : '-') + '</td>' +
        '<td class="text-end">' + r.samples + '</td><td class="text-end ' + (r.failures ? 'text-danger fw-semibold' : '') + '">' + r.failures + '</td><td class="small">' + App.esc(App.fmtDate(r.lastCheckedUtc)) + '</td></tr>'; }).join('')
      : '<tr><td colspan="8" class="text-center py-4 text-body-secondary">No endpoints are monitored. Open an API in the Explorer and press <b>Monitor</b> (Admin).</td></tr>');
    trend = m.errorTrend || []; draw();
  }
  function draw() {
    var c = App.chartColors(); if (chart) chart.destroy(); Chart.defaults.color = c.text; Chart.defaults.borderColor = c.grid;
    chart = new Chart($('#errChart'), { data: { labels: trend.map(function (x) { return x.day; }), datasets: [
      { type: 'bar', label: 'Failures', data: trend.map(function (x) { return x.failures; }), backgroundColor: c.bad, yAxisID: 'y1', borderRadius: 3 },
      { type: 'line', label: 'Avg response (ms)', data: trend.map(function (x) { return x.avgMs; }), borderColor: c.primary, backgroundColor: c.primary, tension: .25, yAxisID: 'y' }] },
      options: { maintainAspectRatio: false, scales: { y: { beginAtZero: true, title: { display: true, text: 'ms' } }, y1: { beginAtZero: true, position: 'right', grid: { drawOnChartArea: false }, ticks: { precision: 0 }, title: { display: true, text: 'failures' } } } } });
  }
  function load() { App.api('GET', '/api/monitoring/health').done(render); }
  $('#btnRun').on('click', function () {
    var $b = $(this).prop('disabled', true).html('<span class="spinner-border spinner-border-sm"></span> Checking…');
    App.api('POST', '/api/monitoring/run', undefined, { silent: true }).done(function () { App.toast('Health check finished', 'success'); load(); })
      .fail(function (e) { App.toast(e.message + (e.code === 'ktc_login_required' ? ' (Use the KTC badge in the top bar.)' : ''), 'danger'); })
      .always(function () { $b.prop('disabled', false).html('<i class="bi bi-heart-pulse"></i> Run health check'); });
  });
  $(document).on('themechange', draw);
  load();
})(jQuery);
