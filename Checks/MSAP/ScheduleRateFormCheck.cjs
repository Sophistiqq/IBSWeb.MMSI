const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/vessel-schedule-form.js'), 'utf8');
const validation = source.slice(source.indexOf('    function validateForm('), source.indexOf('    const saveErrors'));
const provenance = source.slice(source.indexOf('    function updateRateProvenance('), source.indexOf('    async function fetchTariffAndBaf('));
for (const readOnly of [false, true]) {
    const tariff = {value: '', readOnly, dataset: {lookup: readOnly ? 'found' : 'missing'}, classList: {toggle() {}}};
    const baf = {value: '', readOnly, dataset: {lookup: readOnly ? 'found' : 'missing'}, classList: {toggle() {}}};
    const row = {querySelector(selector) { return selector.endsWith('-provenance') ? {} : selector.includes('tariff') ? tariff : baf; }};
    const context = vm.createContext({
        form: {querySelectorAll: selector => selector === '[data-service-row]' ? [row] : []},
        ModernFormValidator: {validate: () => !tariff.required || Number(tariff.value) > 0}
    });
    vm.runInContext(provenance + validation + '\nthis.validate = validateForm;', context);
    assert.equal(context.validate(), readOnly, 'Manual tariff cannot be blank; table-backed tariff needs no manual value.');
    assert.equal(baf.value, readOnly ? '' : '0', 'Only an empty manual BAF should default to zero.');
    tariff.value = '100';
    assert(context.validate());
    assert.equal(tariff.value, '100');
    if (!readOnly) {
        tariff.value = '0';
        assert.equal(context.validate(), false, 'Zero manual tariff must be rejected.');
        tariff.value = '0.0001';
        assert(context.validate(), 'Positive tariff must be allowed.');
        baf.value = '25';
        assert(context.validate());
        assert.equal(baf.value, '25', 'Explicit BAF must not be overwritten.');
    }
}
console.log('PASS: missing manual tariff blocked, blank manual BAF becomes zero, table placeholders and entered amounts preserved.');

const view = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/Areas/MSAP/Views/VesselSchedule/_ScheduleForm.cshtml'), 'utf8');
const tariffInputs = view.split('\n').filter(line => line.includes('class="modern-input js-tariff-rate"'));
assert.equal(tariffInputs.length, 2);
assert(tariffInputs.every(line => line.includes('min="0.0001"')), 'Saved rows and new rows must reject zero tariffs.');
