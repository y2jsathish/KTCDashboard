(function ($) {
  function epItem(e, extra) { return '<div class="list-group-item d-flex align-items-center gap-2">' + App.methodBadge(e.method) + '<a class="mono text-decoration-none flex-grow-1 text-break" href="/Explorer?endpoint=' + e.id + '">' + App.esc(e.route) + '</a>' + (extra || '') + '</div>'; }
  function empty(t) { return '<div class="text-body-secondary p-3">' + t + '</div>'; }
  function route(x) { return '/Explorer?endpoint=' + x.endpointId; }

  App.api('GET', '/api/library/favorites').done(function (l) { $('#favList').html(l.length ? l.map(function (e) { return epItem(e); }).join('') : empty('No favorites yet - press the star on an API in the Explorer.')); });
  App.api('GET', '/api/library/recent?take=30').done(function (l) { $('#recentList').html(l.length ? l.map(function (e) { return epItem(e); }).join('') : empty('Nothing executed yet.')); });

  function loadTpl() {
    App.api('GET', '/api/library/templates').done(function (l) {
      $('#tplList').html(l.length ? l.map(function (t) {
        return '<div class="list-group-item d-flex align-items-center gap-2"><div class="flex-grow-1"><b>' + App.esc(t.name) + '</b>' + (t.isShared ? ' <span class="badge text-bg-info">shared</span>' : '') + '<div class="small mono text-body-secondary">' + App.esc(t.endpointName) + '</div></div>' +
          '<a class="btn btn-sm btn-outline-primary" href="/Explorer?endpoint=' + t.endpointId + '&template=' + t.id + '">Open</a>' + (t.isMine ? '<button class="btn btn-sm btn-outline-danger del-tpl" data-id="' + t.id + '"><i class="bi bi-trash"></i></button>' : '') + '</div>'; }).join('') : empty('No templates yet - use "Save template" in the Explorer.'));
    });
  }
  var tcs = [];
  function loadTc() {
    App.api('GET', '/api/library/testcases').done(function (l) {
      tcs = l;
      $('#tcList').html(l.length ? l.map(function (t) {
        return '<div class="list-group-item d-flex align-items-center gap-2"><div class="flex-grow-1"><b>' + App.esc(t.name) + '</b> ' + (t.lastResult ? '<span class="badge text-bg-' + (t.lastResult === 'Passed' ? 'success' : 'danger') + '" title="' + App.esc(t.lastMessage || '') + '">' + t.lastResult + '</span>' : '') +
          '<div class="small mono text-body-secondary">' + App.esc(t.endpointName) + ' · expects ' + (t.expectedStatus || '2xx') + (t.hasBaseline ? ' · baseline stored' : '') + '</div></div>' +
          '<button class="btn btn-sm btn-outline-primary run" data-id="' + t.id + '">Run</button><button class="btn btn-sm btn-outline-secondary run" data-id="' + t.id + '" data-b="1" title="Run and replace baseline">Run + baseline</button>' +
          '<a class="btn btn-sm btn-outline-secondary" href="/Explorer?endpoint=' + t.endpointId + '&test=' + t.id + '"><i class="bi bi-box-arrow-up-right"></i></a><button class="btn btn-sm btn-outline-danger del-tc" data-id="' + t.id + '"><i class="bi bi-trash"></i></button></div>'; }).join('') : empty('No test cases yet.'));
    });
  }
  function run(id, b) { return App.api('POST', '/api/library/testcases/' + id + '/run?baseline=' + (b ? 'true' : 'false')); }
  $('#tcList').on('click', '.run', function () { var $b = $(this).prop('disabled', true); run($b.data('id'), $b.data('b')).done(function (r) { App.toast(r.message, r.passed ? 'success' : 'danger'); if (r.baselineDiff && !r.baselineDiff.areEqual) App.showDiff(r.baselineDiff, 'Difference from stored baseline'); loadTc(); }).always(function () { $b.prop('disabled', false); }); });
  $('#runAll').on('click', function () {
    var $b = $(this).prop('disabled', true), p = 0, f = 0, chain = $.Deferred().resolve();
    tcs.forEach(function (t) { chain = chain.then(function () { return run(t.id, false).then(function (r) { r.passed ? p++ : f++; }, function () { f++; }); }); });
    chain.always(function () { App.toast(p + ' passed, ' + f + ' failed', f ? 'danger' : 'success'); loadTc(); $b.prop('disabled', false); });
  });
  $('#tcList').on('click', '.del-tc', function () { App.api('DELETE', '/api/library/testcases/' + $(this).data('id')).done(loadTc); });
  $('#tplList').on('click', '.del-tpl', function () { App.api('DELETE', '/api/library/templates/' + $(this).data('id')).done(loadTpl); });
  loadTpl(); loadTc();
})(jQuery);
