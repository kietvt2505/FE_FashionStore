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
    var form = dialog.querySelector('form');
    var reasonCode = form.querySelector('[name="ReasonCode"]');
    if (reasonCode) {
        var other = form.querySelector('[data-other-reason]');
        var reason = other.querySelector('textarea');
        var addressOffer = form.querySelector('[data-address-offer]');
        var paymentOffer = form.querySelector('[data-payment-offer]');
        var confirm = form.querySelector('[type="submit"]');
        function updateReason() {
            other.hidden = reasonCode.value !== 'OTHER';
            reason.required = !other.hidden;
            reason.disabled = other.hidden;
            addressOffer.hidden = reasonCode.value !== 'ADDRESS';
            paymentOffer.hidden = reasonCode.value !== 'PAYMENT';
            confirm.textContent = reasonCode.value === 'ADDRESS' || reasonCode.value === 'PAYMENT' ? 'TIẾP TỤC HỦY ĐƠN' : 'XÁC NHẬN HỦY ĐƠN';
        }
        reasonCode.addEventListener('change', updateReason);
        updateReason();
    }
    form.addEventListener('submit', function () {
        if (!form.checkValidity()) return;
        var submit = form.querySelector('[type="submit"]');
        submit.disabled = true;
        submit.textContent = 'ĐANG GỬI…';
    });
    if (dialog.dataset.openOnLoad === 'true') openDialog();
}());
