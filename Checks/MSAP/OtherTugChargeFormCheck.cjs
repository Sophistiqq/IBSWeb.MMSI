const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/other-tug-charges.js'), 'utf8');

function element(properties = {}) {
    return {events: {}, addEventListener(name, handler) { this.events[name] = handler; }, ...properties};
}

function setup({canEdit = true, editing = false, saved = true} = {}) {
    const include = element({checked: true, disabled: !editing});
    const amount = element({value: '123.45', disabled: !editing});
    const fieldset = {disabled: !editing};
    const row = {dataset: {chargeTicket: '9'}, querySelector: selector => selector === '[data-include]' ? include : amount};
    const ids = {
        otherTugChargeTotal: {}, otherTugChargeError: {},
        editOtherTugCharge: canEdit ? element({hidden: editing}) : null,
        cancelOtherTugChargeEdit: canEdit ? element({hidden: !editing || !saved}) : null,
        saveOtherTugCharge: canEdit ? element({hidden: !editing}) : null,
        confirmOtherTugCharge: saved ? element({hidden: editing}) : null
    };
    const form = element({
        dataset: {canEdit: String(canEdit), editable: String(editing)},
        elements: {Id: {value: saved ? '7' : ''}, ProviderReference: {focus() {}}},
        querySelector: () => fieldset,
        querySelectorAll: selector => selector === '[data-charge-ticket]' ? [row] : Object.values(ids).filter(Boolean),
        reset() { include.checked = true; amount.value = '123.45'; }
    });
    ids.otherTugChargeForm = form;
    const window = element();
    vm.runInNewContext(source, {
        document: {getElementById: id => ids[id], addEventListener: (_, ready) => ready()},
        window, ModernAlert: {confirm: async () => ({isConfirmed: false})}
    });
    return {form, ids, include, amount, fieldset, window};
}

(async () => {
    const page = setup();
    assert(page.fieldset.disabled && page.amount.disabled, 'Saved drafts must open locked.');
    assert.equal(page.ids.otherTugChargeTotal.textContent, '123.45');
    page.ids.editOtherTugCharge.events.click();
    assert(!page.fieldset.disabled && !page.include.disabled && !page.amount.disabled);
    assert(page.ids.confirmOtherTugCharge.hidden && !page.ids.saveOtherTugCharge.hidden);
    page.amount.value = '200';
    page.form.events.input();
    assert.equal(page.ids.otherTugChargeTotal.textContent, '200.00');
    await page.ids.confirmOtherTugCharge.events.click();
    assert.match(page.ids.otherTugChargeError.textContent, /Finish editing/);
    page.include.checked = false;
    page.form.events.change();
    assert(page.amount.disabled, 'Unselected tickets must stay disabled during edits.');
    page.ids.cancelOtherTugChargeEdit.events.click();
    assert(page.fieldset.disabled && page.amount.disabled && page.include.checked);
    assert.equal(page.amount.value, '123.45');
    assert.equal(page.ids.otherTugChargeTotal.textContent, '123.45');
    assert(!page.ids.confirmOtherTugCharge.hidden && page.ids.saveOtherTugCharge.hidden);
    page.window.events.beforeunload({preventDefault() { assert.fail('Cancel must clear unsaved changes.'); }});
    const newBill = setup({editing: true, saved: false});
    assert(!newBill.amount.disabled && newBill.ids.cancelOtherTugChargeEdit.hidden);
    const reviewer = setup({canEdit: false});
    assert(reviewer.fieldset.disabled && reviewer.amount.disabled);
    console.log('PASS: saved drafts locked, explicit editing, selected-ticket amounts, cancel restoration, confirmation guard and read-only access.');
})();
