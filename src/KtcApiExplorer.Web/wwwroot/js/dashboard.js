(function ($) {
  var charts = {}, data = null;
  function build() {
    if (!data) return; var c = App.chartColors();
    Object.values(charts).forEach(function (x) { x.destroy(); }); charts = {};
    Chart.defaults.color = c.text; Chart.defaults.borderColor = c.grid;

    var u = data.usageByApi || [];
    $('#usageEmpty').toggleClass('d-none', u.length > 0); $('#usageChart').toggle(u.length > 0);
    if (u.length) charts.u = new Chart($('#usageChart'), { type: 'bar',
      data: { labels: u.map(function (x) { return x.name.length > 38 ? x.name.slice(0, 36) + '…' : x.name; }), datasets: [{ label: 'Calls', data: u.map(function (x) { return x.count; }), backgroundColor: c.primary, borderRadius: 3 }] },
      options: { indexAxis: 'y', maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { x: { beginAtZero: true, ticks: { precision: 0 } } } } });

    var tot = data.success + data.failure;
    charts.r = new Chart($('#ratioChart'), { type: 'doughnut',
      data: { labels: ['Success', 'Failure'], datasets: [{ data: tot ? [data.success, data.failure] : [0, 0], backgroundColor: [c.ok, c.bad], borderWidth: 0 }] },
      options: { maintainAspectRatio: false, cutout: '65%', plugins: { legend: { position: 'bottom' }, subtitle: { display: true, text: tot ? Math.round(100 * data.success / tot) + '% success of ' + tot + ' calls' : 'No executions yet' } } } });

    var t = data.trend || [];
    charts.t = new Chart($('#trendChart'), {
      data: { labels: t.map(function (x) { return x.day; }), datasets: [
        { type: 'line', label: 'Avg response (ms)', data: t.map(function (x) { return x.avgMs; }), borderColor: c.primary, backgroundColor: c.primary, tension: .25, yAxisID: 'y', pointRadius: 3 },
        { type: 'bar', label: 'Failures', data: t.map(function (x) { return x.failures; }), backgroundColor: c.bad, yAxisID: 'y1', borderRadius: 3 }] },
      options: { maintainAspectRatio: false, interaction: { mode: 'index', intersect: false }, scales: { y: { beginAtZero: true, title: { display: true, text: 'ms' } }, y1: { beginAtZero: true, position: 'right', grid: { drawOnChartArea: false }, ticks: { precision: 0 }, title: { display: true, text: 'failures' } } } } });
  }
  App.api('GET', '/api/monitoring/dashboard').done(function (d) {
    data = d;
    $('#stats [data-k]').each(function () { $(this).text(d[$(this).data('k')]); });
    $('#noData').toggleClass('d-none', d.totalApis > 0);
    build();
  });
  $(document).on('themechange', build);
})(jQuery);
