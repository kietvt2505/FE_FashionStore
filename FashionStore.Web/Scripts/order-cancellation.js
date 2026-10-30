(function () {
    'use strict';
    var dialog = document.getElementById('order-cancellation-dialog');
    if (!dialog) return;
    var opener;
    function openDialog(event) {
        opener = event ? event.currentTarget : document.activeElement;
        if (!dialog.open) dialog.showModal();
    }
    document.querySelectorAll('[data-order-cancel-open]').forEach(function (button) { button.addEventListener('click', openDialog); });
    dialog.querySelectorAll('[data-order-dialog-close]').forEach(function (button) { button.addEventListener('click', function () { dialog.close(); }); });
    dialog.addEventListener('close', function () { if (opener) opener.focus(); });
    var reason = dialog.querySelector('[data-reason-required="true"]');
    if (reason) { reason.required = true; reason.minLength = 5; }
    var form = dialog.querySelector('form');
    form.addEventListener('submit', function () {
        if (!form.checkValidity()) return;
        var submit = form.querySelector('[type="submit"]');
        submit.disabled = true;
        submit.textContent = 'ĐANG GỬI…';
    });
    if (dialog.dataset.openOnLoad === 'true') openDialog();
}());
