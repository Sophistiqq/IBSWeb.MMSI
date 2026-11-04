const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/billing-preparation.js'), 'utf8');
const calculation = source.slice(source.indexOf('    function roundMoney('), source.indexOf('    function renderServices('));
let skipped = false;
let discount = 12.5;
const context = vm.createContext({
    commercialEnabled: true,
    $: () => ({ is: () => skipped, val: () => discount })
});
vm.runInContext(calculation, context);
context.ticket = { dispatchRate: 100, bafRate: 20, actualHours: 1.25, dispatchChargeType: 'Per hour', bafChargeType: 'Per move' };
function charges() { return vm.runInContext('charges(ticket)', context); }
assert.equal(charges().basic, 109.38);
assert.equal(charges().baf, 17.5);
skipped = true;
assert.equal(charges().basic, 125);
assert.equal(charges().baf, 20);
skipped = false;
discount = 100;
assert.equal(charges().basic, 0);
context.ticket.actualHours = 0.5;
discount = 0;
assert.equal(charges().basic, 100, 'Hourly charges retain the one-hour minimum.');
context.ticket.dispatchRate = 1.005;
assert.equal(charges().basic, 1, 'Midpoint rounding must match the server.');
context.commercialEnabled = false;
context.ticket.dispatchNetAmount = 42;
context.ticket.bafNetAmount = 7;
assert.equal(charges().basic, 42, 'Legacy jobs retain recorded charges.');
assert.equal(charges().baf, 7);
console.log('PASS: Billing discount changes Basic/BAF previews, supports skipping and 100%, preserves minimum hours, midpoint rounding and legacy amounts.');

const sbmaVisibility = source.slice(source.indexOf('            const canSetSbma ='), source.indexOf('            updateDiscount();', source.indexOf('            const canSetSbma =')));
for (const [portName, commercialEnabled, expected] of [
    ['Subic', true, true], [' Subic Bay ', true, true], ['SUBIC', true, true],
    ['Manila', true, false], ['SubicOther', true, false], [null, true, false], ['Subic', false, false]
]) {
    const properties = {};
    vm.runInNewContext(sbmaVisibility, {
        commercialEnabled, header: {portName},
        $: selector => ({prop: (name, value) => properties[selector + ':' + name] = value})
    });
    assert.equal(properties['#sbmaCard:hidden'], !expected);
    assert.equal(properties['#SbmaEnabled, #sbmaFalse:disabled'], !expected, 'Hidden SBMA settings must not be submitted.');
}
console.log('PASS: SBMA toggle appears only for Subic bookings; hidden settings are excluded from submission.');

const discountEvents = source.slice(source.indexOf("    $('#discountCard').on('click'"), source.indexOf("    $recipient.on('change'"));
const controls = {
    '#SkipCosDiscount': {checked: false}, '#CosDiscountPercent': {value: '12.5', readOnly: false}
};
const handlers = {};
let focused = false;
vm.runInNewContext(discountEvents, {
    commercialEnabled: true,
    $: selector => ({
        on: (event, callback) => { handlers[selector + ':' + event] = callback; },
        is: () => controls[selector].checked,
        prop: (name, value) => { if (value === undefined) return controls[selector][name]; controls[selector][name] = value; },
        val: value => { if (value === undefined) return controls[selector].value; controls[selector].value = value; },
        trigger: event => { if (event === 'focus') focused = true; }
    }),
    renderServices() {}, updateDiscount() {}, updateSummary() {}
});
const clickDiscount = () => (handlers['#discountCard:click'] || handlers['#discountToggle:click'])({
    target: {closest: selector => selector === 'button'}
});
clickDiscount();
assert.equal(controls['#SkipCosDiscount'].checked, true);
assert.equal(controls['#CosDiscountPercent'].value, '0', 'Skipping must clear the displayed discount, as Scheduling did.');
clickDiscount();
assert(focused, 'Enabling must focus the discount input.');
controls['#CosDiscountPercent'].value = '';
handlers['#CosDiscountPercent:blur']();
assert.equal(controls['#SkipCosDiscount'].checked, true, 'A blank value must switch back to no discount.');
assert.equal(controls['#CosDiscountPercent'].value, '0');
handlers['#discountCard:click']({target: {closest: () => null}});
assert.equal(controls['#SkipCosDiscount'].checked, false, 'Clicking the skipped card must enable editing.');
controls['#CosDiscountPercent'].value = '15';
handlers['#discountCard:click']({target: {closest: selector => selector === 'input'}});
assert.equal(controls['#SkipCosDiscount'].checked, false, 'Editing the input must not toggle the card.');
controls['#CosDiscountPercent'].value = '0';
handlers['#CosDiscountPercent:blur']();
assert.equal(controls['#SkipCosDiscount'].checked, true, 'Zero must switch back to no discount.');
console.log('PASS: Discount skipping, enabling and blank-value behavior match Scheduling.');

context.customer = {vatType: 'Vatable', withholdingTax: true, withholdingVat: true};
assert.equal(vm.runInContext('statementTotals(1120).due', context), 1050);
context.customer.withholdingVat = false;
assert.equal(vm.runInContext('statementTotals(1120).due', context), 1100);
context.customer.withholdingTax = false;
assert.equal(vm.runInContext('statementTotals(1120).due', context), 1120);
context.customer = {vatType: 'Exempt', withholdingTax: true};
assert.equal(vm.runInContext('statementTotals(1000).due', context), 980);
console.log('PASS: displayed amounts use the existing statement VAT/withholding calculation without an SBMA deduction.');

context.commercialEnabled = true;
skipped = false;
discount = 0;
assert.equal(vm.runInContext('appliedDiscount(ticket)', context), 0);
discount = 12.5;
assert.equal(vm.runInContext('appliedDiscount(ticket)', context), 12.5);
skipped = true;
assert.equal(vm.runInContext('appliedDiscount(ticket)', context), 0);
context.commercialEnabled = false;
context.ticket.bafDiscount = 5;
assert.equal(vm.runInContext('appliedDiscount(ticket)', context), 5);
console.log('PASS: discount-column visibility uses an applied positive discount, excluding skipped and zero discounts.');

context.commercialEnabled = true;
skipped = false;
context.ticket = { dispatchRate: 100, bafRate: 20, actualHours: 1.25, dispatchChargeType: 'Per hour', bafChargeType: 'Per move' };
for (const percentage of [0, 12.5, 100]) {
    discount = percentage;
    const amounts = charges();
    assert.equal(amounts.basicGross, 125, 'Original Basic must remain visible when a discount changes.');
    assert.equal(amounts.bafGross, 20, 'Original BAF must remain visible when a discount changes.');
    assert.equal(amounts.basic, Math.round(125 * (1 - percentage / 100) * 100) / 100);
    assert.equal(amounts.baf, 20 * (1 - percentage / 100));
}
context.commercialEnabled = false;
context.ticket.dispatchAmount = 84;
context.ticket.bafAmount = 14;
context.ticket.dispatchNetAmount = 42;
context.ticket.bafNetAmount = 7;
assert.equal(charges().basicGross, 84);
assert.equal(charges().bafGross, 14);
assert.equal(charges().basic, 42);
assert.equal(charges().baf, 7);
console.log('PASS: original and net Basic/BAF amounts stay distinct for zero, partial, full and recorded discounts.');

context.commercialEnabled = false;
context.ticket.dispatchDiscount = 10;
context.ticket.bafDiscount = 0;
assert.equal(vm.runInContext('appliedDiscount(ticket)', context), 10, 'An older Basic-only discount must remain visible.');
console.log('PASS: older tickets retain their recorded Basic and BAF discount distinctions.');
