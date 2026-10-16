const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/vessel-schedule-form.js'), 'utf8');
const view = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/Areas/MSAP/Views/VesselSchedule/_ScheduleForm.cshtml'), 'utf8');
for (const role of ['js-service-select', 'js-tariff-basis', 'js-baf-method']) {
    assert.equal(view.split(`class="js-modern-select ${role}"`).length - 1, 2, `${role} must use ModernSelect in saved rows and the template.`);
}
const addRow = source.slice(source.indexOf('    function addServiceRow('), source.indexOf('    if (addTugboatBtn'));
{
    const newRow = {};
    let initialized;
    vm.runInNewContext(addRow + '\naddServiceRow(1, target);', {
        serviceRowTemplate: {innerHTML: '<div></div>'},
        document: {createElement: () => ({firstElementChild: newRow})},
        target: {appendChild: row => assert.equal(row, newRow)},
        $: row => ({find: selector => { assert.equal(row, newRow); return selector; }}),
        ModernSelect: {init: selects => initialized = selects}, updateSummary() {}
    });
    assert.equal(initialized, '.js-modern-select', 'Adding a service must initialize its modern selects.');
}
{
    const cleanup = source.slice(source.indexOf('    function removeAssignment('), source.indexOf('    function updateTugBadge('));
    let dropdownsRemoved = 0;
    let assignmentRemoved = false;
    const assignment = {remove: () => assignmentRemoved = true};
    const selects = [{}, {}, {}];
    vm.runInNewContext(cleanup + '\nremoveAssignment(assignment);', {
        assignment, updateSummary() {},
        $: element => element === assignment
            ? {find: () => ({each: callback => selects.forEach(select => callback.call(select))})}
            : {data: () => ({remove: () => dropdownsRemoved++})}
    });
    assert.equal(dropdownsRemoved, 3, 'Removing a service/tug must remove every dropdown appended to the body.');
    assert(assignmentRemoved);
}
const validation = source.slice(source.indexOf('    function validateForm('), source.indexOf('    const saveErrors'));
for (const valid of [true, false]) {
    let nativeChecks = 0;
    let message;
    const result = vm.runInNewContext(validation + '\nvalidateForm();', {
        form: {querySelectorAll: () => [{checkValidity: () => valid, getAttribute: () => 'Tugboat'}]},
        ModernAlert: {error: text => message = text},
        ModernFormValidator: {validate: () => { nativeChecks++; return true; }}
    });
    assert.equal(result, valid);
    assert.equal(nativeChecks, valid ? 1 : 0, 'Hidden invalid selects must be handled before native reportValidity tries to focus them.');
    if (!valid) assert.equal(message, 'Please choose a value for Tugboat.');
}
const rates = source.slice(source.indexOf('    function updateRateProvenance('), source.indexOf('    function refreshAllRates('));
const pending = [];
const context = vm.createContext({
    AbortController, URLSearchParams,
    form: {dataset: {rateUrl: '/rates'}},
    getScheduleContext: () => ({customerId: 1, terminalId: 2, date: '2026-10-16T08:00'}),
    updateSummary() {},
    fetch: () => new Promise(resolve => pending.push(resolve))
});
vm.runInContext(rates + '\nthis.lookup = fetchTariffAndBaf; this.provenance = updateRateProvenance;', context);
function row() {
    const elements = Object.fromEntries(['tariff-rate', 'baf-rate', 'tariff-provenance', 'baf-provenance'].map(key => ['.js-' + key, {value: '', dataset: {}}]));
    return {isConnected: true, querySelector: selector => elements[selector]};
}
function respond(data) { pending.shift()({ok: true, json: async () => data}); }
(async () => {
    const entry = row();
    const first = context.lookup(entry, 1);
    assert.equal(entry.querySelector('.js-tariff-rate').dataset.lookup, 'loading');
    respond({exists: true, tariffRate: 10000, bafRate: 29000});
    await first;
    assert.equal(entry.querySelector('.js-tariff-rate').value, '', 'A table estimate must never submit as a manual override.');
    assert.equal(entry.querySelector('.js-baf-rate').placeholder, '29,000.00');
    assert.equal(entry.querySelector('.js-baf-rate').dataset.lookup, 'found');
    assert.equal(entry.querySelector('.js-baf-rate').readOnly, true, 'Detected table rates must be read-only.');

    const noMatch = context.lookup(entry, 3);
    respond({exists: false});
    await noMatch;
    assert.equal(entry.querySelector('.js-baf-rate').readOnly, false, 'Manual entry is available only without a table match.');
    entry.querySelector('.js-baf-rate').value = '0';
    context.provenance(entry);
    assert.equal(entry.querySelector('.js-baf-provenance').textContent, 'Manual rate', 'Zero is a valid manual rate.');
    const old = context.lookup(entry, 1);
    const latest = context.lookup(entry, 2);
    respond({exists: true, tariffRate: 1, bafRate: 2});
    respond({exists: true, tariffRate: 35000, bafRate: 1200});
    await Promise.all([old, latest]);
    assert.equal(entry.querySelector('.js-tariff-rate').placeholder, '35,000.00', 'An old lookup must not overwrite a newer selection.');
    assert.equal(entry.querySelector('.js-baf-rate').value, '', 'A detected table rate supersedes a previous manual entry.');
    assert.equal(entry.querySelector('.js-baf-rate').readOnly, true);
    assert.equal(entry.querySelector('.js-baf-provenance').textContent, 'Table estimate · planned date');

    const missing = context.lookup(entry, 3);
    respond({exists: false});
    await missing;
    assert.equal(entry.querySelector('.js-tariff-provenance').textContent, 'No table match');
    await context.lookup(entry, 0);
    assert.equal(entry.querySelector('.js-tariff-rate').placeholder, 'From table', 'Clearing a service must remove its old estimate.');
    assert.equal(entry.querySelector('.js-tariff-rate').dataset.lookup, 'unchecked');
    const unavailable = context.lookup(entry, 1);
    pending.shift()({ok: false});
    await unavailable;
    assert.equal(entry.querySelector('.js-tariff-provenance').textContent, 'Lookup unavailable');
    assert.equal(entry.querySelector('.js-tariff-rate').readOnly, true, 'A failed lookup is not proof of a missing table rate.');
    assert(source.includes("$(servicesContainer).on('change'"), 'ModernSelect emits jQuery change events; tug assignments must receive them.');
    console.log('PASS: detected rates are read-only and stay out of manual fields; missing matches allow zero/manual entry; stale responses are ignored and failed lookups stay locked.');
})().catch(error => { console.error(error); process.exitCode = 1; });
