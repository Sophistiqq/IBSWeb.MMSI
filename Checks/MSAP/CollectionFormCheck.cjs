const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = page => fs.readFileSync(path.join(root, `IBSWeb/Areas/MSAP/Views/Collection/${page}.cshtml`), 'utf8');
const create = read('Create');
const customerHandler = create.slice(create.indexOf('            function handleCustomerSelected('), create.indexOf('            function handleCheckboxChange(')).replace("'@Model.CustomerId'", "'1'");
for (const restoringSubmission of [true, false]) {
    let cleared = 0;
    let loaded;
    const fields = new Map([['#CustomerOrderSlipId', '9'], ['#CheckNumber', 'CHK-123'], ['#cashAmount', '25']]);
    const context = {
        restoringSubmission, customer: {value: 1, name: 'Customer'},
        $: selector => ({
            val: value => value === undefined ? fields.get(selector) : fields.set(selector, value),
            show() { return this; }, css() { return this; }, text() { return this; }, data() { return this; }, prop() { return this; }
        }),
        clearForm() { cleared++; fields.set('#CheckNumber', ''); fields.set('#cashAmount', ''); },
        handleCheckboxChange() {}, loadBillings(value) { loaded = value; },
        JobProgress: {load() {}}, document: {getElementById() { return {}; }}
    };
    vm.runInNewContext(`${customerHandler}
handleCustomerSelected(customer);`, context);
    assert.equal(cleared, restoringSubmission ? 0 : 1, 'A failed submission must keep payment fields; changing customer must reset them.');
    assert.equal(loaded, 1);
    if (restoringSubmission) {
        assert.equal(fields.get('#CheckNumber'), 'CHK-123');
        assert.equal(fields.get('#cashAmount'), '25');
    }
}
for (const page of ['Create', 'Edit']) {
    const source = read(page);
    const initialize = source.slice(source.indexOf('            function initializeDataTable('), source.indexOf('            function setupEventListeners('));
    let config;
    vm.runInNewContext(`${initialize}
initializeDataTable();`, {
        submittedBillingIds: [2], $: () => ({DataTable(value) { config = value; }})
    });
    const row = {amount: 100, ewt: 0, wvat: 0, net: 100, isSelected: true};
    assert(config.columns[0].render(2, 'display', row).includes('checked'), `${page} must restore submitted billings.`);
    assert(!config.columns[0].render(1, 'display', row).includes('checked'), `${page} must retain deselected billings after failure.`);
    assert(source.includes("table.$('.bill-checkbox:checked')"), 'Selections must include filtered and paginated billings.');
}
for (const page of ['Create', 'Edit']) {
    const source = read(page);
    const handlerName = page === 'Create' ? 'areYouSureToSave' : 'areYouSureToUpdate';
    const start = source.indexOf(`            function ${handlerName}(`);
    const end = source.indexOf("                if (!ModernFormValidator.validate('myForm')) return;", start);
    const handler = source.slice(start, end) + "                return;\n            }";
    for (const amount of ['', '25.50', 'invalid']) {
        const fields = [{value: amount}, {value: ''}, {value: ''}];
        const context = {
            document: {getElementById() { return {}; }},
            $: selector => typeof selector === 'string'
                ? {each(callback) { fields.forEach(field => callback.call(field)); }}
                : {val(value) { if (value !== undefined) selector.value = value; return selector.value; }}
        };
        vm.runInNewContext(`${handler}\n${handlerName}({preventDefault() {}});`, context);
        assert.equal(fields[0].value, amount === '' ? '0.00' : amount, `${page}: empty optional amounts must submit zero; entered amounts must be preserved.`);
        assert.equal(fields[1].value, '0.00');
        assert.equal(fields[2].value, '0.00');
    }
}
console.log('PASS: failed collection submissions preserve payment fields and billing selections; customer changes reset them; empty optional amounts submit zero.');
