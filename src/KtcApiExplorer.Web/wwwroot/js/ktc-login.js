(function ($) {
  function panel(s) {
    var $p = $('#sessionPanel');
    if (!s || !s.isLoggedIn) { $p.html('<div class="text-body-secondary"><i class="bi bi-shield-x"></i> Not signed in to KTC.</div>' + (s && s.state === 'expired' ? '<div class="text-danger small mt-1">The previous session expired.</div>' : '')); return; }
    var left = Math.max(0, (App.ktc.expires - Date.now()) / 1000);
    $p.html('<dl class="row mb-2 small"><dt class="col-4">User</dt><dd class="col-8">' + App.esc(s.userName) + '</dd><dt class="col-4">App / mode</dt><dd class="col-8">' + App.esc(s.appName) + ' / ' + App.esc(s.authMode) +
      '</dd><dt class="col-4">Server</dt><dd class="col-8 text-break">' + App.esc(s.baseUrl) + '</dd><dt class="col-4">Expires in</dt><dd class="col-8"><b id="cd">' + App.mm(left) + '</b> <span class="text-body-secondary">(' + App.esc(App.fmtDate(s.expiresUtc)) + ')</span></dd></dl>' +
      '<div class="progress mb-3" style="height:6px"><div class="progress-bar" id="cdBar" style="width:100%"></div></div>' +
      '<button class="btn btn-sm btn-outline-primary me-2" id="btnRefresh"><i class="bi bi-arrow-repeat"></i> Refresh token</button>' +
      '<button class="btn btn-sm btn-outline-danger" id="btnLogout"><i class="bi bi-box-arrow-right"></i> Sign out of KTC</button>' +
      '<a class="btn btn-sm btn-primary float-end" href="/Explorer">Open Explorer</a>');
  }
  var total = 0;
  setInterval(function () {
    var s = App.ktc.status; if (!s || !s.isLoggedIn) return;
    var left = Math.max(0, (App.ktc.expires - Date.now()) / 1000); if (left > total) total = left;
    $('#cd').text(App.mm(left)); $('#cdBar').css('width', (total ? 100 * left / total : 0) + '%').toggleClass('bg-warning', left <= 120).toggleClass('bg-danger', left <= 30);
  }, 1000);
  $(document).on('ktcstatus', function (e, s) { panel(s); });

  $('#ktcForm').on('submit', function (e) {
    e.preventDefault(); $('#ktcError').text(''); var $b = $('#signIn').prop('disabled', true);
    App.api('POST', '/api/ktc/login', { collectionId: +$('#collectionId').val(), username: $('#username').val(), password: $('#password').val(),
      appName: $('#appName').val(), authMode: $('#authMode').val(), pluginName: $('#pluginName').val() || null }, { silent: true })
      .done(function (s) { $('#password').val(''); total = 0; App.setKtcStatus(s); App.toast('Signed in to KTC', 'success'); })
      .fail(function (err) { $('#ktcError').text(err.message); })
      .always(function () { $b.prop('disabled', false); });
  });
  $('#sessionPanel').on('click', '#btnRefresh', function () { App.api('POST', '/api/ktc/refresh').done(function (s) { total = 0; App.setKtcStatus(s); App.toast('Token refreshed', 'success'); }); });
  $('#sessionPanel').on('click', '#btnLogout', function () { App.api('POST', '/api/ktc/logout').done(function (s) { App.setKtcStatus(s); }); });
  App.api('GET', '/api/ktc/status', undefined, { silent: true }).done(function (s) { App.setKtcStatus(s); });
})(jQuery);
