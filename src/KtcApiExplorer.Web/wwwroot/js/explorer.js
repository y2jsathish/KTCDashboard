/* API Explorer: tree + request editor + response panel. All dynamic text goes through App.esc / .text() (XSS-safe). */
(function ($) {
  'use strict';
  var role = $('#explorer').data('role');
  var S = { cid: null, tree: [], flat: [], ep: null, scope: 'all', method: '', folder: '', q: '', open: {}, recent: [], prevExec: {}, last: null, masked: true };
  var esc = App.esc;
  function store(k, v) { try { if (v === undefined) return localStorage.getItem(k); localStorage.setItem(k, v); } catch (e) { } }

  // ------------------------------------------------------------ collections + tree
  function loadCollections() {
    return App.api('GET', '/api/explorer/collections').done(function (cols) {
      var $s = $('#collectionSel').empty();
      cols.forEach(function (c) { $s.append($('<option>').val(c.id).text(c.name + ' (' + c.endpointCount + ')')); });
      if (!cols.length) { $('#tree').html('<div class="empty-hint">No collections imported yet.' + (role === 'Admin' ? '<br><a href="/Collections">Import one</a>' : '') + '</div>'); return; }
      var want = +store('explorer.cid'); if (want && cols.some(function (c) { return c.id === want; })) $s.val(want);
      S.cid = +$s.val(); loadTree().then(initialDeepLink);
    });
  }

  function loadTree() {
    return App.api('GET', '/api/explorer/collections/' + S.cid + '/tree').done(function (t) {
      S.tree = t; S.flat = [];
      t.forEach(function (f) { f.endpoints.forEach(function (e) { e.folder = f.name; e.search = (e.method + ' ' + e.route + ' ' + (e.summary || '') + ' ' + f.name).toLowerCase(); S.flat.push(e); }); });
      var $f = $('#folderFilter').empty().append('<option value="">All folders</option>');
      t.forEach(function (f) { $f.append($('<option>').val(f.name).text(f.name + ' (' + f.endpoints.length + ')')); });
      $('#treeCount').text(S.flat.length);
      renderTree();
    });
  }

  function matches(e) {
    if (S.method && e.method !== S.method) return false;
    if (S.folder && e.folder !== S.folder) return false;
    if (S.scope === 'fav' && !e.isFavorite) return false;
    if (S.q) { var terms = S.q.toLowerCase().split(/\s+/); for (var i = 0; i < terms.length; i++) if (e.search.indexOf(terms[i]) < 0) return false; }
    return true;
  }

  function epRow(e) {
    return '<div class="tree-ep' + (S.ep && S.ep.id === e.id ? ' active' : '') + '" data-id="' + e.id + '" title="' + esc(e.summary || '') + '">' + App.methodBadge(e.method) +
      '<span class="route">' + esc(e.route) + '</span>' + (e.isFavorite ? '<i class="bi bi-star-fill fav"></i>' : '') + '</div>';
  }

  function renderTree() {
    var $t = $('#tree'), html = '', filtering = !!(S.q || S.method || S.folder || S.scope === 'fav');
    if (S.scope === 'recent') {
      var list = S.recent.map(function (r) { return S.flat.filter(function (e) { return e.id === r.id; })[0]; }).filter(function (e) { return e && matches(e); });
      html = list.length ? list.map(epRow).join('') : '<div class="empty-hint">Nothing executed recently.</div>';
    } else {
      var shown = 0;
      S.tree.forEach(function (f) {
        var items = f.endpoints.filter(matches); if (!items.length) return; shown += items.length;
        var open = filtering || S.open[f.name];
        html += '<div class="tree-folder" data-folder="' + esc(f.name) + '"><i class="bi ' + (open ? 'bi-chevron-down' : 'bi-chevron-right') + '"></i><i class="bi bi-folder' + (open ? '2-open' : '') + '"></i> ' + esc(f.name) + '<span class="count">' + items.length + '</span></div>';
        if (open) html += items.map(epRow).join('');
      });
      if (!shown) html = '<div class="empty-hint">No APIs match the current filters.</div>';
      $('#treeCount').text(shown + (filtering ? ' / ' + S.flat.length : ''));
    }
    $t.html(html);
  }

  $('#tree').on('click', '.tree-folder', function () { var n = $(this).data('folder'); S.open[n] = !S.open[n]; renderTree(); });
  $('#tree').on('click', '.tree-ep', function () { selectEndpoint($(this).data('id')); });
  var qTimer; $('#q').on('input', function () { clearTimeout(qTimer); var v = this.value; qTimer = setTimeout(function () { S.q = v.trim(); renderTree(); }, 120); });
  $('#clearQ').on('click', function () { S.q = S.method = S.folder = ''; S.scope = 'all'; $('#q').val(''); $('#folderFilter').val(''); $('#methodFilter .btn,#scope .btn').removeClass('active'); $('#methodFilter [data-m=""],#scope [data-s="all"]').addClass('active'); renderTree(); });
  $('#methodFilter').on('click', '.btn', function () { S.method = $(this).data('m'); $('#methodFilter .btn').removeClass('active'); $(this).addClass('active'); renderTree(); });
  $('#folderFilter').on('change', function () { S.folder = this.value; renderTree(); });
  $('#scope').on('click', '.btn', function () {
    S.scope = $(this).data('s'); $('#scope .btn').removeClass('active'); $(this).addClass('active');
    if (S.scope === 'recent') App.api('GET', '/api/library/recent?take=30').done(function (r) { S.recent = r; renderTree(); }); else renderTree();
  });
  $('#collectionSel').on('change', function () { S.cid = +this.value; store('explorer.cid', S.cid); S.ep = null; $('#editor').html('<div class="empty-hint">Select an API.</div>'); loadTree(); });
  $(document).on('keydown', function (e) {
    if (e.key === '/' && !$(e.target).is('input,textarea,select')) { e.preventDefault(); $('#q').focus(); }
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey) && S.ep) { e.preventDefault(); execute(); }
  });
  $('.collapse-btn').on('click', function () { $('#' + $(this).data('target')).toggleClass('collapsed'); });

  // ------------------------------------------------------------ editor
  function selectEndpoint(id, applied) {
    return App.api('GET', '/api/explorer/endpoints/' + id).done(function (d) {
      S.ep = d; $('#tree .tree-ep').removeClass('active').filter('[data-id="' + id + '"]').addClass('active');
      renderEditor(d); if (applied) applyRequest(applied);
      $('#editorPane').removeClass('collapsed');
    });
  }

  function kvRows(list, kind, withRequired) {
    return list.map(function (p) {
      return '<tr data-kind="' + kind + '"><td style="width:2rem"><input type="checkbox" class="form-check-input kv-on" ' + (p.enabled === false ? '' : 'checked') + '></td>' +
        '<td><input class="form-control form-control-sm kv-key" value="' + esc(p.key) + '"' + (kind === 'query' && withRequired ? ' data-required="' + (p.required ? 1 : 0) + '"' : '') + '></td>' +
        '<td><input class="form-control form-control-sm kv-val" value="' + esc(p.value) + '" placeholder="' + (p.required ? 'required' : '') + '"></td>' +
        '<td style="width:2rem"><button class="btn btn-sm btn-link text-danger p-0 kv-del" title="Remove"><i class="bi bi-x-lg"></i></button></td></tr>';
    }).join('');
  }

  function renderEditor(d) {
    var hasPath = d.pathParams.length > 0;
    var h = '';
    h += '<div class="d-flex align-items-start gap-2 mb-2">' + App.methodBadge(d.method) + '<div class="flex-grow-1"><div class="mono fw-semibold text-break">' + esc(d.displayRoute) + '</div><div class="small text-body-secondary">' + esc(d.folder) + '</div></div>' +
      '<button class="btn btn-sm btn-outline-warning" id="btnFav" title="Favorite"><i class="bi ' + (d.isFavorite ? 'bi-star-fill' : 'bi-star') + '"></i></button>' +
      (role === 'Admin' ? '<button class="btn btn-sm btn-outline-secondary" id="btnMon" title="Include in health monitoring"><i class="bi bi-activity"></i> ' + (d.isMonitored ? 'Monitored' : 'Monitor') + '</button>' : '') + '</div>';
    if (d.description) h += '<p class="small mb-2">' + esc(d.description) + '</p>';
    h += '<div class="url-bar mb-2" id="urlBar"></div>';
    h += '<div class="d-flex flex-wrap gap-2 mb-2">' +
      '<button class="btn btn-primary btn-sm" id="btnExec"><i class="bi bi-play-fill"></i> Execute <kbd class="ms-1 small">Ctrl+Enter</kbd></button>' +
      '<button class="btn btn-outline-secondary btn-sm" id="btnCurl"><i class="bi bi-terminal"></i> Copy cURL</button>' +
      '<button class="btn btn-outline-secondary btn-sm" id="btnJson"><i class="bi bi-braces"></i> Copy JSON</button>' +
      '<button class="btn btn-outline-secondary btn-sm" id="btnSaveTpl"><i class="bi bi-bookmark-plus"></i> Save template</button>' +
      '<button class="btn btn-outline-secondary btn-sm" id="btnSaveTest"><i class="bi bi-clipboard-check"></i> Save test</button></div>';
    h += '<div id="ktcWarn" class="alert alert-warning py-2 small d-none"></div>';

    h += '<ul class="nav nav-tabs mb-2" role="tablist">' +
      tab('params', 'Params', true) + tab('headers', 'Headers') + tab('body', 'Body') + tab('examples', 'Examples' + (d.examples.length ? ' (' + d.examples.length + ')' : '')) +
      tab('dev', 'Code') + tab('ai', 'AI') + tab('lib', 'Library') + '</ul><div class="tab-content">';

    // params
    h += '<div class="tab-pane fade show active" id="tab-params">';
    if (hasPath) h += '<div class="small fw-semibold mb-1">Path parameters</div><table class="table table-sm kv-table mb-3"><tbody>' + d.pathParams.map(function (p) {
      return '<tr><td style="width:30%" class="mono small">{' + esc(p.key) + '}</td><td><input class="form-control form-control-sm path-val" data-k="' + esc(p.key) + '" placeholder="required"></td></tr>'; }).join('') + '</tbody></table>';
    h += '<div class="d-flex justify-content-between"><div class="small fw-semibold mb-1">Query parameters</div><button class="btn btn-sm btn-link py-0 kv-add" data-kind="query">+ add</button></div>' +
      '<table class="table table-sm kv-table" id="queryTbl"><tbody>' + kvRows(d.query, 'query', true) + '</tbody></table>' +
      (!hasPath && !d.query.length ? '<div class="small text-body-secondary">This endpoint has no parameters.</div>' : '') + '</div>';

    // headers
    h += '<div class="tab-pane fade" id="tab-headers"><div class="small text-body-secondary mb-2"><code>Authorization: Bearer …</code> is added automatically from your KTC session.</div>' +
      '<div class="d-flex justify-content-end"><button class="btn btn-sm btn-link py-0 kv-add" data-kind="header">+ add</button></div>' +
      '<table class="table table-sm kv-table" id="headerTbl"><tbody>' + kvRows(d.headers, 'header') + '</tbody></table></div>';

    // body
    h += '<div class="tab-pane fade" id="tab-body"><div class="d-flex gap-2 mb-2"><button class="btn btn-sm btn-outline-secondary" id="btnFmt">Format JSON</button><button class="btn btn-sm btn-outline-secondary" id="btnSample">Reset to sample</button>' +
      '<span class="small ms-auto align-self-center" id="bodyState"></span></div><textarea class="form-control code" id="bodyTxt" rows="14" spellcheck="false" placeholder="' + (d.requiresBody ? 'JSON request body' : 'No body needed for ' + esc(d.method)) + '">' + esc(d.body || '') + '</textarea>' +
      (d.bodyMode && d.bodyMode !== 'raw' ? '<div class="small text-body-secondary mt-1">Body mode <b>' + esc(d.bodyMode) + '</b>: edit as a JSON list of {key,value}.</div>' : '') + '</div>';

    // examples
    h += '<div class="tab-pane fade" id="tab-examples">' + (d.examples.length ? d.examples.map(function (x, i) {
      return '<div class="mb-3"><div class="fw-semibold small">' + esc(x.name) + (x.status ? ' <span class="badge text-bg-' + App.statusClass(x.status) + '">' + x.status + '</span>' : '') + '</div><pre class="code" style="max-height:200px">' + esc(x.body || '') + '</pre></div>'; }).join('')
      : '<div class="text-body-secondary small">No saved examples in the imported collection. Execute the request and save the result as a test case baseline.</div>') + '</div>';

    // dev
    h += '<div class="tab-pane fade" id="tab-dev"><div class="d-flex gap-2 mb-2 flex-wrap"><select class="form-select form-select-sm" id="lang" style="max-width:220px">' +
      '<option value="curl">cURL</option><option value="csharp-httpclient">C# HttpClient</option><option value="csharp-restsharp">C# RestSharp</option><option value="javascript-fetch">JavaScript fetch</option><option value="csharp-dto">C# DTO models (from body)</option></select>' +
      '<button class="btn btn-sm btn-outline-primary" id="btnGen">Generate</button><button class="btn btn-sm btn-outline-secondary" id="btnCopyCode">Copy</button></div>' +
      '<pre class="code" id="codeOut" style="min-height:120px">Choose a language and press Generate. The bearer token is replaced by a placeholder.</pre></div>';

    // ai
    h += '<div class="tab-pane fade" id="tab-ai"><div class="d-flex flex-wrap gap-1 mb-2">' +
      ['explain|Explain API', 'request-body|Generate request body', 'test-data|Sample test data', 'required-fields|Required fields', 'documentation|Generate docs'].map(function (x) { var p = x.split('|'); return '<button class="btn btn-sm btn-outline-primary ai-btn" data-k="' + p[0] + '">' + p[1] + '</button>'; }).join('') +
      '</div><div class="small text-body-secondary mb-1" id="aiProv"></div><pre class="code" id="aiOut" style="min-height:120px">Pick an action.</pre><div class="mt-2 d-none" id="aiApply"><button class="btn btn-sm btn-outline-secondary" id="btnUseBody">Use as request body</button></div></div>';

    // library
    h += '<div class="tab-pane fade" id="tab-lib"><div id="libList" class="small text-body-secondary">Loading…</div></div>';
    h += '</div>';
    $('#editor').html(h); updateUrl();
    $('#bodyTxt').on('input', validateBody); validateBody();
  }
  function tab(id, label, active) { return '<li class="nav-item"><button class="nav-link' + (active ? ' active' : '') + '" data-bs-toggle="tab" data-bs-target="#tab-' + id + '" type="button" role="tab">' + label + '</button></li>'; }

  function validateBody() {
    var v = $('#bodyTxt').val().trim(), $s = $('#bodyState');
    if (!v) { $s.text(''); return true; }
    try { JSON.parse(v); $s.html('<span class="text-success"><i class="bi bi-check-circle"></i> valid JSON</span>'); return true; } catch (e) { $s.html('<span class="text-danger"><i class="bi bi-x-circle"></i> ' + esc(e.message) + '</span>'); return false; }
  }

  function collect() {
    var r = { endpointId: S.ep.id, pathParams: {}, query: [], headers: [], body: null };
    $('#editor .path-val').each(function () { r.pathParams[$(this).data('k')] = this.value; });
    $('#queryTbl tr').each(function () { var $r = $(this); r.query.push({ key: $r.find('.kv-key').val(), value: $r.find('.kv-val').val(), enabled: $r.find('.kv-on').is(':checked'), required: $r.find('.kv-key').data('required') == 1 }); });
    $('#headerTbl tr').each(function () { var $r = $(this); r.headers.push({ key: $r.find('.kv-key').val(), value: $r.find('.kv-val').val(), enabled: $r.find('.kv-on').is(':checked') }); });
    var b = $('#bodyTxt').val(); r.body = b && b.trim() ? b : null;
    return r;
  }

  function applyRequest(r) {
    if (!r) return;
    Object.keys(r.pathParams || {}).forEach(function (k) { $('#editor .path-val[data-k="' + k + '"]').val(r.pathParams[k]); });
    if (r.query && r.query.length) $('#queryTbl tbody').html(kvRows(r.query, 'query', true));
    if (r.headers && r.headers.length) $('#headerTbl tbody').html(kvRows(r.headers, 'header'));
    if (r.body != null) $('#bodyTxt').val(r.body);
    validateBody(); updateUrl();
  }

  function updateUrl() {
    if (!S.ep) return; var r = collect(), route = S.ep.route;
    var path = route.replace(/\{([^}/]+)\}/g, function (m, k) { return r.pathParams[k] ? encodeURIComponent(r.pathParams[k]) : m; });
    var qs = r.query.filter(function (q) { return q.enabled && q.key && q.value; }).map(function (q) { return encodeURIComponent(q.key) + '=' + encodeURIComponent(q.value); }).join('&');
    $('#urlBar').text(S.ep.method + ' ' + S.ep.baseUrl.replace(/\/$/, '') + '/' + path + (qs ? '?' + qs : ''));
  }

  $('#editor').on('input', 'input', updateUrl);
  $('#editor').on('click', '.kv-add', function () { var k = $(this).data('kind'); $(k === 'query' ? '#queryTbl tbody' : '#headerTbl tbody').append(kvRows([{ key: '', value: '', enabled: true }], k, k === 'query')); });
  $('#editor').on('click', '.kv-del', function () { $(this).closest('tr').remove(); updateUrl(); });
  $('#editor').on('click', '#btnFmt', function () { try { $('#bodyTxt').val(JSON.stringify(JSON.parse($('#bodyTxt').val()), null, 2)); validateBody(); } catch (e) { App.toast('Not valid JSON: ' + e.message, 'danger'); } });
  $('#editor').on('click', '#btnSample', function () { $('#bodyTxt').val(S.ep.body || ''); validateBody(); });
  $('#editor').on('click', '#btnFav', function () {
    App.api('POST', '/api/explorer/endpoints/' + S.ep.id + '/favorite').done(function (r) {
      S.ep.isFavorite = r.isFavorite; $('#btnFav i').attr('class', 'bi ' + (r.isFavorite ? 'bi-star-fill' : 'bi-star'));
      S.flat.forEach(function (e) { if (e.id === S.ep.id) e.isFavorite = r.isFavorite; }); renderTree(); App.toast(r.isFavorite ? 'Added to favorites' : 'Removed from favorites', 'success'); });
  });
  $('#editor').on('click', '#btnMon', function () {
    App.api('POST', '/api/monitoring/toggle/' + S.ep.id).done(function (r) { S.ep.isMonitored = r.isMonitored; $('#btnMon').html('<i class="bi bi-activity"></i> ' + (r.isMonitored ? 'Monitored' : 'Monitor')); App.toast(r.isMonitored ? 'Added to health monitoring' : 'Removed from monitoring', 'success'); });
  });
  $('#editor').on('click', '#btnJson', function () { var b = $('#bodyTxt').val(); if (!b.trim()) return App.toast('There is no request body to copy.', 'warning'); App.copy(b, 'JSON'); });
  $('#editor').on('click', '#btnCurl', function () { gen('curl').done(function (r) { App.copy(r.code, 'cURL command'); }); });

  // ------------------------------------------------------------ code generation + AI
  function gen(lang) { var r = collect(); return App.api('POST', '/api/tools/codegen', { request: r, language: lang }); }
  $('#editor').on('click', '#btnGen', function () { var l = $('#lang').val(); gen(l).done(function (r) { $('#codeOut').text(r.code); }).fail(function (e) { $('#codeOut').text(e.message); }); });
  $('#editor').on('click', '#btnCopyCode', function () { App.copy($('#codeOut').text(), 'Code'); });
  var aiLast = null;
  $('#editor').on('click', '.ai-btn', function () {
    var k = $(this).data('k'), $b = $(this).prop('disabled', true); $('#aiOut').text('Working…'); $('#aiApply').addClass('d-none');
    App.api('POST', '/api/tools/ai/' + k, { endpointId: S.ep.id, count: 3 }).done(function (r) {
      aiLast = r; $('#aiOut').text(r.content); $('#aiProv').text('Provider: ' + r.provider);
      if (k === 'request-body' || k === 'test-data') $('#aiApply').removeClass('d-none');
    }).fail(function (e) { $('#aiOut').text(e.message); }).always(function () { $b.prop('disabled', false); });
  });
  $('#editor').on('click', '#btnUseBody', function () {
    try { var j = JSON.parse(aiLast.content); if (Array.isArray(j)) j = j[0]; $('#bodyTxt').val(JSON.stringify(j, null, 2)); validateBody(); $('[data-bs-target="#tab-body"]').tab('show'); } catch (e) { App.toast('The generated text is not JSON.', 'danger'); }
  });

  // ------------------------------------------------------------ library (templates / tests)
  $('#editor').on('shown.bs.tab', '[data-bs-target="#tab-lib"]', loadLib);
  function loadLib() {
    var id = S.ep.id;
    $.when(App.api('GET', '/api/library/templates?endpointId=' + id, undefined, { silent: true }), App.api('GET', '/api/library/testcases?endpointId=' + id, undefined, { silent: true })).done(function (tpl, tc) {
      var h = '<div class="fw-semibold mb-1">Templates</div>';
      h += tpl.length ? tpl.map(function (x) { return '<div class="d-flex align-items-center gap-2 border-bottom py-1"><span class="flex-grow-1">' + esc(x.name) + (x.isShared ? ' <span class="badge text-bg-info">shared</span>' : '') + (x.isMine ? '' : ' <span class="text-body-secondary">by others</span>') + '</span><button class="btn btn-sm btn-outline-primary tpl-load" data-id="' + x.id + '">Load</button>' + (x.isMine ? '<button class="btn btn-sm btn-outline-danger tpl-del" data-id="' + x.id + '"><i class="bi bi-trash"></i></button>' : '') + '</div>'; }).join('') : '<div class="text-body-secondary mb-2">None saved.</div>';
      S.tpl = tpl; S.tc = tc;
      h += '<div class="fw-semibold mt-3 mb-1">Test cases</div>';
      h += tc.length ? tc.map(function (x) { return '<div class="d-flex align-items-center gap-2 border-bottom py-1"><span class="flex-grow-1">' + esc(x.name) + ' <span class="text-body-secondary">expects ' + (x.expectedStatus || '2xx') + '</span> ' + (x.lastResult ? '<span class="badge text-bg-' + (x.lastResult === 'Passed' ? 'success' : 'danger') + '" title="' + esc(x.lastMessage || '') + '">' + x.lastResult + '</span>' : '') + '</span>' +
        '<button class="btn btn-sm btn-outline-primary tc-run" data-id="' + x.id + '">Run</button><button class="btn btn-sm btn-outline-secondary tc-run" data-id="' + x.id + '" data-baseline="1" title="Run and store the response as baseline">Run + baseline</button><button class="btn btn-sm btn-outline-danger tc-del" data-id="' + x.id + '"><i class="bi bi-trash"></i></button></div>'; }).join('') : '<div class="text-body-secondary">None saved.</div>';
      $('#libList').html(h);
    });
  }
  $('#editor').on('click', '.tpl-load', function () { var t = S.tpl.filter(function (x) { return x.id === $(this).data('id'); }.bind(this))[0]; applyRequest(t.request); App.toast('Template loaded', 'success'); });
  $('#editor').on('click', '.tpl-del', function () { App.api('DELETE', '/api/library/templates/' + $(this).data('id')).done(loadLib); });
  $('#editor').on('click', '.tc-del', function () { App.api('DELETE', '/api/library/testcases/' + $(this).data('id')).done(loadLib); });
  $('#editor').on('click', '.tc-run', function () {
    var $b = $(this).prop('disabled', true);
    App.api('POST', '/api/library/testcases/' + $b.data('id') + '/run?baseline=' + ($b.data('baseline') ? 'true' : 'false')).done(function (r) {
      App.toast(r.message, r.passed ? 'success' : 'danger'); if (r.result) renderResponse(r.result);
      if (r.baselineDiff && !r.baselineDiff.areEqual) App.showDiff(r.baselineDiff, 'Difference from stored baseline'); loadLib();
    }).always(function () { $b.prop('disabled', false); });
  });
  $('#editor').on('click', '#btnSaveTpl', function () {
    var n = window.prompt('Template name'); if (!n) return;
    App.api('POST', '/api/library/templates', { endpointId: S.ep.id, name: n, isShared: window.confirm('Share this template with other users?\n(OK = shared, Cancel = only me)'), request: collect() }).done(function () { App.toast('Template saved', 'success'); });
  });
  $('#editor').on('click', '#btnSaveTest', function () {
    var n = window.prompt('Test case name'); if (!n) return; var st = window.prompt('Expected HTTP status (blank = any 2xx)', '200');
    var must = window.prompt('Response must contain text (optional)', '');
    App.api('POST', '/api/library/testcases', { endpointId: S.ep.id, name: n, expectedStatus: st ? +st : null, expectedBodyContains: must || null, request: collect() }).done(function () { App.toast('Test case saved', 'success'); });
  });

  // ------------------------------------------------------------ execute + response
  function execute() {
    if (!S.ep) return; if (!validateBody()) { App.toast('Fix the JSON body first.', 'danger'); return; }
    var $b = $('#btnExec').prop('disabled', true).html('<span class="spinner-border spinner-border-sm"></span> Running…'); $('#ktcWarn').addClass('d-none');
    App.api('POST', '/api/explorer/execute', collect(), { silent: true }).done(function (r) {
      var old = S.prevExec[S.ep.id]; if (r.executionId) S.prevExec[S.ep.id] = { prev: old ? old.cur : null, cur: r.executionId };
      renderResponse(r);
    }).fail(function (e) {
      if (e.code === 'ktc_login_required') $('#ktcWarn').removeClass('d-none').html('<i class="bi bi-shield-lock"></i> ' + esc(e.message) + ' <a href="/Ktc/Login">Sign in to KTC</a>');
      else App.toast(e.message, 'danger');
      $('#response').html('<div class="alert alert-' + (e.code === 'ktc_login_required' ? 'warning' : 'danger') + '">' + esc(e.message) + '</div>');
    }).always(function () { App.reloadKtcStatus();   // the server may have renewed the token silently
      $b.prop('disabled', false).html('<i class="bi bi-play-fill"></i> Execute <kbd class="ms-1 small">Ctrl+Enter</kbd>'); });
  }
  $('#editor').on('click', '#btnExec', execute);

  function highlight(json) {
    if (json.length > 400000) return esc(json);
    return esc(json).replace(/(&quot;(?:\\.|[^&\\])*?&quot;)(\s*:)?|\b(true|false)\b|\bnull\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g, function (m, str, colon, bool) {
      if (str) return colon ? '<span class="json-key">' + str + '</span>' + colon : '<span class="json-str">' + str + '</span>';
      if (bool) return '<span class="json-bool">' + m + '</span>'; if (m === 'null') return '<span class="json-null">null</span>'; return '<span class="json-num">' + m + '</span>';
    });
  }

  function bodyView(r) {
    var raw = S.masked ? (r.maskedBody != null ? r.maskedBody : r.body) : r.body;
    if (r.isBinary) return { text: '', html: '<div class="alert alert-secondary mb-0">Binary response (' + esc(r.contentType || 'unknown type') + ', ' + App.fmtBytes(r.sizeBytes) + '). Use <b>Download</b>.</div>' };
    if (raw == null || raw === '') return { text: '', html: '<div class="text-body-secondary">(empty body)</div>' };
    try { var pretty = JSON.stringify(JSON.parse(raw), null, 2); return { text: pretty, html: '<pre class="code">' + highlight(pretty) + '</pre>' }; }
    catch (e) { return { text: raw, html: '<pre class="code">' + esc(raw) + '</pre>' }; }
  }

  function renderResponse(r) {
    S.last = r; var v = bodyView(r);
    var cls = r.error ? 'danger' : App.statusClass(r.statusCode);
    var h = '<div class="d-flex flex-wrap align-items-center gap-2 mb-2">' +
      '<span class="badge text-bg-' + cls + ' status-pill">' + (r.error ? 'ERROR' : r.statusCode + ' ' + esc(r.reasonPhrase || '')) + '</span>' +
      '<span class="badge text-bg-secondary" title="Execution time"><i class="bi bi-stopwatch"></i> ' + App.fmtMs(r.durationMs) + '</span>' +
      '<span class="badge text-bg-secondary" title="Response size"><i class="bi bi-hdd"></i> ' + App.fmtBytes(r.sizeBytes) + '</span>' +
      (r.truncated ? '<span class="badge text-bg-warning">truncated</span>' : '') +
      '<div class="ms-auto d-flex gap-1"><button class="btn btn-sm btn-outline-secondary" id="btnMask" title="Mask tokens / passwords in the displayed body"><i class="bi bi-' + (S.masked ? 'eye-slash' : 'eye') + '"></i> ' + (S.masked ? 'Masked' : 'Revealed') + '</button>' +
      '<button class="btn btn-sm btn-outline-secondary" id="btnCopyRes"><i class="bi bi-clipboard"></i></button><button class="btn btn-sm btn-outline-secondary" id="btnDl"><i class="bi bi-download"></i> Download</button>' +
      '<button class="btn btn-sm btn-outline-secondary" id="btnCmp"><i class="bi bi-arrow-left-right"></i> Compare</button></div></div>';
    if (r.error) h += '<div class="alert alert-danger py-2 small">' + esc(r.error) + '</div>';
    h += '<div class="url-bar mb-2 small">' + esc(r.method) + ' ' + esc(r.url) + '</div>';
    h += '<ul class="nav nav-tabs mb-2"><li class="nav-item"><button class="nav-link active" data-bs-toggle="tab" data-bs-target="#rtab-body" type="button">Body</button></li>' +
      '<li class="nav-item"><button class="nav-link" data-bs-toggle="tab" data-bs-target="#rtab-hdr" type="button">Headers (' + r.headers.length + ')</button></li></ul>' +
      '<div class="tab-content"><div class="tab-pane fade show active" id="rtab-body">' + v.html + '</div><div class="tab-pane fade" id="rtab-hdr"><table class="table table-sm mono"><tbody>' +
      r.headers.map(function (x) { return '<tr><td class="fw-semibold text-nowrap">' + esc(x.key) + '</td><td class="text-break">' + esc(x.value) + '</td></tr>'; }).join('') + '</tbody></table></div></div>';
    $('#response').html(h); $('#responsePane').removeClass('collapsed');
  }
  $('#response').on('click', '#btnMask', function () { S.masked = !S.masked; renderResponse(S.last); });
  $('#response').on('click', '#btnCopyRes', function () { var v = bodyView(S.last); if (!v.text) return App.toast('Nothing to copy.', 'warning'); App.copy(v.text, 'Response'); });
  $('#response').on('click', '#btnDl', function () {
    var r = S.last, name = 'response-' + (S.ep ? S.ep.route.replace(/[^\w]+/g, '_') : 'api') + '-' + r.statusCode;
    if (r.isBinary && r.bodyBase64) { var bin = atob(r.bodyBase64), arr = new Uint8Array(bin.length); for (var i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i); App.download(name + '.bin', new Blob([arr], { type: r.contentType || 'application/octet-stream' })); }
    else { var v = bodyView(r); App.download(name + (v.text.charAt(0) === '{' || v.text.charAt(0) === '[' ? '.json' : '.txt'), v.text || '', 'application/json'); }
  });
  $('#response').on('click', '#btnCmp', function () {
    var p = S.ep && S.prevExec[S.ep.id]; if (!p || !p.prev) return App.toast('Run the request at least twice to compare with the previous run.', 'warning');
    App.api('GET', '/api/library/history/compare?left=' + p.prev + '&right=' + p.cur).done(function (d) { App.showDiff(d, 'Previous run vs. this run (stored, masked copies)'); });
  });

  // ------------------------------------------------------------ deep links: ?endpoint=ID[&template=ID|test=ID]
  function initialDeepLink() {
    var p = new URLSearchParams(location.search), id = +p.get('endpoint'); if (!id) return;
    App.api('GET', '/api/explorer/endpoints/' + id, undefined, { silent: true }).done(function (d) {
      var go = function () {
        var applied = null, tid = +p.get('template'), cid = +p.get('test');
        if (tid) App.api('GET', '/api/library/templates?endpointId=' + id, undefined, { silent: true }).done(function (ts) { var t = ts.filter(function (x) { return x.id === tid; })[0]; selectEndpoint(id, t && t.request); });
        else if (cid) App.api('GET', '/api/library/testcases?endpointId=' + id, undefined, { silent: true }).done(function (ts) { var t = ts.filter(function (x) { return x.id === cid; })[0]; selectEndpoint(id, t && t.request); });
        else selectEndpoint(id, applied);
      };
      if (d.collectionId !== S.cid) { $('#collectionSel').val(d.collectionId); S.cid = d.collectionId; loadTree().then(go); } else go();
    });
  }

  loadCollections();
})(jQuery);
