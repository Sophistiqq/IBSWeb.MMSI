const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function captureTable(view) {
    let config;
    const handlers = new Map();
    const chain = {
        ready: callback => callback(),
        DataTable: options => { config = options; return chain; },
        on: (event, selector, callback) => { handlers.set(`${event}:${selector}`, callback); return chain; },
        column: () => chain, search: () => chain, draw: () => chain
    };
    const jquery = () => chain;
    jquery.extend = (...values) => Object.assign({}, ...values.filter(value => typeof value === 'object'));
    jquery.fn = { dataTable: { render: { text: () => ({}) } } };
    const sandbox = {
        $: jquery, document: {}, window: { location: { search: '' } }, URLSearchParams, console
    };
    vm.createContext(sandbox);
    const helper = fs.readFileSync(path.join(__dirname, '../../IBSWeb/wwwroot/msap/js/modern-table.js'), 'utf8');
    vm.runInContext(helper.split('$(document).ready')[0] + '\nglobalThis.ModernTable = ModernTable;', sandbox);
    sandbox.escapeHtml = sandbox.ModernTable.render.text;
    const source = fs.readFileSync(path.join(__dirname, `../../IBSWeb/Areas/MSAP/Views/${view}/Index.cshtml`), 'utf8');
    const script = [...source.matchAll(/<script>([\s\S]*?)<\/script>/g)]
        .map(match => match[1]).find(value => value.includes('.DataTable('));
    assert(script, `Find ${view}'s actual table script`);
    vm.runInContext(script
        .replace(/@Html.Raw\(System.Text.Json.JsonSerializer.Serialize\(periodSearch\)\)/g, '""')
        .replace(/@Url.Action\([^\n]*?\)/g, '/inert-route')
        .replace(/const canDelete = [^\n]+/, 'const canDelete = true;'), sandbox);
    assert(config, `Capture ${view}'s actual table configuration`);
    return { config, handlers, sandbox };
}

const dispatch = captureTable('DispatchTicket');
const billing = captureTable('Billing');
const audit = captureTable('AuditTrail');
const payload = `<img src=x onerror=this.alt=731> & " ' &#60;`;
const encoded = '&lt;img src=x onerror=this.alt=731&gt; &amp; &quot; &#39; &amp;#60;';
const row = { customerId: 1, terminal: { port: { portName: payload }, terminalName: payload } };

for (const type of ['display', 'filter']) {
    assert.equal(dispatch.config.columns[7].render(null, type, row), `${encoded} - ${encoded}`);
    assert.equal(dispatch.config.columns[9].render(payload, type, row), encoded);
    assert.equal(billing.config.columns[4].render(null, type, row), `${encoded} - ${encoded}`);
    assert.equal(billing.config.columns[6].render(payload, type, row), encoded);
    assert.equal(audit.config.columns[3].render(payload, type, row), encoded);
}
assert.equal(dispatch.config.columns[7].render(null, 'display', {}), '');
assert.equal(dispatch.config.columns[9].render(null, 'display', { customerId: 1 }), '');
assert.match(dispatch.config.columns[9].render(null, 'display', {}), /<span[^>]*>\(For Customer Assigning\)<\/span>/);
assert.equal(dispatch.config.columns[9].render('Customer A', 'display', row), 'Customer A');
assert.equal(billing.config.columns[6].render(null, 'display', row), '');
const multiple = `${payload}, T-2, T-3, T-4`;
assert.equal(billing.config.columns[6].render(multiple, 'display', row),
    `<span title="${encoded}, T-2, T-3, T-4">${encoded}, T-2, T-3 +1 more</span>`);
const action = audit.config.columns[6].render(null, 'display', { documentType: 'COS', recordId: 7, referenceNumber: payload });
assert(action.includes(`data-trace-reference="${encoded}"`));
assert(!action.includes('onclick='), 'Stored references must not be interpolated into JavaScript');
assert.equal(audit.config.columns[6].render(null, 'display', { documentType: 'Billing', recordId: 7 }), '-');
let traced;
audit.sandbox.loadTrace = (...values) => { traced = values; };
audit.handlers.get('click:.audit-trace').call({ dataset: { traceId: '7', traceReference: payload } });
assert.deepEqual(traced, ['7', payload], 'The trace action must receive the original reference as text');
assert.match(dispatch.config.columns[2].render('DT-7', 'display', { dispatchTicketId: 7 }), /<a href="\/inert-route\?id=7"[^>]*>DT-7<\/a>/);
console.log('PASS: actual dispatch, billing and audit renderers encode stored text and preserve placeholders, links and trace actions.');
