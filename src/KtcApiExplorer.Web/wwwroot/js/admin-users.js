$('#userModal').on('show.bs.modal', function (e) {
  var d = $(e.relatedTarget).data(), isNew = !!d.new;
  $('#userModalTitle').text(isNew ? 'New user' : 'Edit ' + d.user);
  $('#uId').val(isNew ? '' : d.id); $('#uUser').val(isNew ? '' : d.user).prop('readonly', !isNew); $('#uDisplay').val(isNew ? '' : d.display);
  $('#uEmail').val(isNew ? '' : d.email || ''); $('#uRole').val(isNew ? 'Viewer' : d.role); $('#uPw').val('').prop('required', isNew);
  $('#uPwHint').text(isNew ? '' : '(leave empty to keep)'); $('#uActive').prop('checked', isNew || d.active == 1);
});
