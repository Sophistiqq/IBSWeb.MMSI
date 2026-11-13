const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/vessel-schedule-form.js'), 'utf8');
const view = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/Areas/MSAP/Views/VesselSchedule/_ScheduleForm.cshtml'), 'utf8');
for (const role of ['js-service-select']) {
    assert.equal(view.split(`class="js-modern-select ${role}"`).length - 1, 2, `${role} must use ModernSelect in saved rows and the template.`);
}
assert(!view.includes('js-tariff-basis'), 'Tariff basis selector must be removed from Operations scheduling form.');
assert(!view.includes('js-baf-method'), 'BAF method selector must be removed from Operations scheduling form.');
assert(!view.includes('js-tariff-rate'), 'Manual tariff rate input must be removed from Operations scheduling form.');
assert(!view.includes('js-baf-rate'), 'Manual BAF rate input must be removed from Operations scheduling form.');
assert(!view.includes('id="RateMode"'), 'Rate mode toggle must be removed from Operations scheduling form.');
const addRow = source.slice(source.indexOf('    function addServiceRow('), source.indexOf('    if (addTugboatBtn'));
{
    const newRow = {querySelectorAll: () => []};
    let initialized;
    vm.runInNewContext(addRow + '\naddServiceRow(1, target);', {
        serviceRowTemplate: {innerHTML: '<div></div>'},
        document: {createElement: () => ({firstElementChild: newRow})},
        target: {appendChild: row => assert.equal(row, newRow)},
        $: row => ({find: selector => { assert.equal(row, newRow); return selector; }}),
        ModernSelect: {init: selects => initialized = selects}, updateSummary() {}, manualRates: () => false
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
        form: {querySelectorAll: selector => selector === '[data-service-row]'
            ? []
            : [{checkValidity: () => valid, getAttribute: () => 'Tugboat'}]},
        ModernAlert: {error: text => message = text},
        ModernFormValidator: {validate: () => { nativeChecks++; return true; }}
    });
    assert.equal(result, valid);
    assert.equal(nativeChecks, valid ? 1 : 0, 'Hidden invalid selects must be handled before native reportValidity tries to focus them.');
    if (!valid) assert.equal(message, 'Please choose a value for Tugboat.');
}
assert(!source.includes('updateRateProvenance'), 'updateRateProvenance must be removed from scheduling form JS.');
assert(!source.includes('fetchTariffAndBaf'), 'fetchTariffAndBaf must be removed from scheduling form JS.');
assert(!source.includes('manualRates'), 'manualRates must be removed from scheduling form JS.');
assert(!source.includes('applyRateLock'), 'applyRateLock must be removed from scheduling form JS.');
assert(!source.includes('refreshAllRates'), 'refreshAllRates must be removed from scheduling form JS.');
assert(source.includes("$(servicesContainer).on('change'"), 'ModernSelect emits jQuery change events; tug assignments must receive them.');
console.log('PASS: Operations scheduling form strictly decouples rates, removes recurrence toggles, and initializes ModernSelect cleanly.');
assert(!view.includes('id="iocToggle"') && !view.includes('id="commercialHeading"'));
assert(view.includes('id="summaryIoc"') && view.includes('id="summarySbma"'));
console.log('PASS: Scheduling shows commercial defaults as summary information without editors.');
