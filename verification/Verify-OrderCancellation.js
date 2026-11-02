// Run with node; tests the dialog's events without a browser, account or order mutation.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function element(values = {}) {
    return Object.assign({hidden: false, disabled: false, required: false, listeners: {},
        addEventListener(name, handler) { this.listeners[name] = handler; }}, values);
}
const reason = element(), other = element({querySelector: () => reason});
const code = element({value: ''}), address = element(), payment = element(), submit = element();
const form = element({checkValidity: () => true, querySelector(selector) {
    return {'[name="ReasonCode"]': code, '[data-other-reason]': other, '[data-address-offer]': address,
        '[data-payment-offer]': payment, '[type="submit"]': submit}[selector];
}});
const opener = element({focus() { this.focused = true; }});
const dialog = element({open: false, dataset: {}, querySelector: () => form, querySelectorAll: () => [],
    showModal() { this.open = true; }, close() { this.open = false; }});
const document = {activeElement: opener, getElementById: () => dialog, querySelectorAll: () => [opener]};
vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname, '../FashionStore.Web/Scripts/order-cancellation.js'), 'utf8'), {document});
opener.listeners.click({currentTarget: opener});assert.equal(dialog.open, true);
for (const selected of ['NO_NEED', 'ADDRESS', 'PAYMENT', 'OTHER']) {
    code.value = selected;code.listeners.change();
    assert.equal(address.hidden, selected !== 'ADDRESS');
    assert.equal(payment.hidden, selected !== 'PAYMENT');
    assert.equal(other.hidden, selected !== 'OTHER');
    assert.equal(reason.required, selected === 'OTHER');
    assert.equal(reason.disabled, selected !== 'OTHER');
    assert.equal(submit.textContent, ['ADDRESS','PAYMENT'].includes(selected) ? 'TIẾP TỤC HỦY ĐƠN' : 'XÁC NHẬN HỦY ĐƠN');
    assert.equal(submit.disabled, false); // Selecting an alternative never submits/cancels an order.
}
form.listeners.submit();assert.equal(submit.disabled, true);
console.log('PASS: four reasons, update offers, other text validation, no automatic cancellation, submit guard');
