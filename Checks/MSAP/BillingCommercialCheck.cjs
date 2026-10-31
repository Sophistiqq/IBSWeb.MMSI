const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/billing-preparation.js'), 'utf8');
const calculation = source.slice(source.indexOf('    function charges('), source.indexOf('    function renderServices('));
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
