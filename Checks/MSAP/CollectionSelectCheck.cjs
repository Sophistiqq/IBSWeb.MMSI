const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = name => fs.readFileSync(path.join(__dirname, '../../', name), 'utf8');
const script = read('IBSWeb/wwwroot/msap/js/collection-form.js');
const view = read('IBSWeb/Areas/MSAP/Views/Collection/_CollectionForm.cshtml');
assert.match(view, /id="progressCos" class="js-modern-select"[^>]*disabled/);
assert.match(view, /id="billedParty" class="js-modern-select"/);
const picker = { value: '', disabled: true, add(option) { if (!this.value) this.value = option.value; }, replaceChildren() { this.value = ''; } };
let bills = [];
let progress;
let refreshed = 0;
const body = script.split('    function updateProgress() {')[1].split('    function updateTotals')[0];
const update = vm.runInNewContext(`(function () {${body.slice(0, body.lastIndexOf('}'))}})`, {
    document: { getElementById: id => id === 'progressCos' ? picker : {} },
    form: { dataset: {} }, selected: () => bills,
    Option: function (text, value) { this.value = value; },
    $: value => value, refreshModernSelect: () => refreshed++,
    JobProgress: { load: (container, value) => progress = value.customerOrderSlipId }
});
update(); assert.equal(picker.disabled, true); assert.equal(progress, '');
bills = [{ customerOrderSlipId: 7, cosNumber: 'COS-7' }];
update(); assert.equal(picker.disabled, false); assert.equal(progress, '7');
bills = []; update(); assert.equal(picker.disabled, true); assert.equal(progress, '');
assert.equal(refreshed, 3);
const component = read('IBSWeb/wwwroot/msap/js/modern-select.js');
let disabled = true;
const attributes = {};
let closed = 0;
const trigger = { toggleClass(name, value) { attributes[name] = value; return this; }, attr(name, value) { attributes[name] = value; return this; }, removeClass() {} };
const syncBody = component.split('        const syncDisabled = () => {')[1].split('        syncDisabled();')[0];
const sync = vm.runInNewContext(`(function () {${syncBody.slice(0, syncBody.lastIndexOf('};'))}})`, {
    $select: { prop: () => disabled }, $trigger: trigger, $dropdown: { removeClass() { closed++; } }
});
sync(); assert.equal(attributes['aria-disabled'], 'true'); assert.equal(attributes.tabindex, '-1'); assert.equal(closed, 1);
disabled = false; sync(); assert.equal(attributes['aria-disabled'], 'false'); assert.equal(attributes.tabindex, '0');
console.log('Collection select checks passed: empty, selected, deselected and modern disabled state.');
