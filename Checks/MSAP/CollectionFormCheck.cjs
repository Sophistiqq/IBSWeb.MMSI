const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const source = read('IBSWeb/wwwroot/msap/js/collection-form.js');
for (const page of ['Create', 'Edit']) {
    const view = read(`IBSWeb/Areas/MSAP/Views/Collection/${page}.cshtml`);
    assert(view.includes('<partial name="_CollectionForm"'), `${page} must use the shared collection form.`);
    assert(view.includes('~/msap/js/collection-form.js'), `${page} must load the shared form behavior.`);
}
const start = source.indexOf('    async function loadBillings() {');
const end = source.indexOf("    $('#CustomerId').on('change', loadBillings);", start);
assert(start >= 0 && end > start, 'The billing loader must be present.');
const fields = new Map([['#CustomerId', '1'], ['#MsapCollectionId', '9'], ['#CheckNumber', 'CHK-123'], ['#CashAmount', '25']]);
const context = {
    request: 0, bills: [], loading: false, restoreSubmitted: true,
    submitted: [{billingId: 2, amountToPay: 100}],
    form: {dataset: {billingsUrl: '/MSAP/Collection/GetBillings'}},
    URLSearchParams, Option: function (text, value) { this.value = value; },
    $: selector => ({
        val(value) { return value === undefined ? fields.get(selector) : fields.set(selector, value); },
        empty() { return this; }, append() { return this; }
    }),
    error(message) { assert.equal(message, ''); }, render() {}, updateTotals() {},
    async fetch(url) {
        assert(url.includes('customerId=1') && url.includes('collectionId=9'));
        return {ok: true, json: async () => ({success: true, data: [
            {msapBillingId: 1, eligible: true, isSelected: true, net: 50, billedParty: 'Customer'},
            {msapBillingId: 2, eligible: true, isSelected: false, net: 100, billedParty: 'Customer'},
            {msapBillingId: 3, eligible: false, isSelected: true, net: 75, billedParty: 'Customer'}
        ]})};
    }
};
vm.createContext(context);
vm.runInContext(source.slice(start, end), context);
(async () => {
    await vm.runInContext('loadBillings()', context);
    assert.deepEqual(Array.from(context.bills, bill => bill.selected), [false, true, false], 'Failed submissions must restore selections and exclude ineligible bills.');
    assert.equal(context.bills[1].payment, 100, 'Selected bills must settle their full net balance.');
    assert.equal(fields.get('#CheckNumber'), 'CHK-123');
    assert.equal(fields.get('#CashAmount'), '25');
    assert.equal(context.loading, false);
    await vm.runInContext('loadBillings()', context);
    assert.deepEqual(Array.from(context.bills, bill => bill.selected), [true, false, false], 'Later loads must use server selections instead of submitted selections.');
    console.log('PASS: shared collection forms restore submitted selections and payment fields, reject ineligible selections and stop restoring on later loads.');
})().catch(error => { console.error(error); process.exitCode = 1; });
