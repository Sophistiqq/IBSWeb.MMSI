const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
let selector;
vm.runInNewContext(read('IBSWeb/wwwroot/msap/js/schedule-confirmation.js'), {
    document: { querySelectorAll: value => { selector = value; return []; }, getElementById: () => null }
});
assert.equal(selector, '[data-schedule-confirm="true"]', 'Posting links must bypass the confirmation handler.');
for (const view of ['Billing/Create', 'Collection/Create', 'Collection/Edit']) {
    const source = read(`IBSWeb/Areas/MSAP/Views/${view}.cshtml`)
        + (view.startsWith('Collection/') ? read('IBSWeb/Areas/MSAP/Views/Collection/_CollectionForm.cshtml') : '');
    assert(source.includes('id="financialJobProgress"'));
    assert(source.includes('~/msap/js/job-progress.js'), `${view} must load the progress script.`);
}
(async () => {
    const window = {};
    const pending = [];
    vm.runInNewContext(read('IBSWeb/wwwroot/msap/js/job-progress.js'), {
        window, URLSearchParams, AbortController,
        fetch: (url, options) => new Promise(resolve => pending.push({url, options, resolve}))
    });
    const container = {
        dataset: {actionStage: '7', progressUrl: '/MSAP/CustomerOrderSlip/Progress'},
        replaceChildren() { this.innerHTML = ''; this.textContent = ''; }
    };
    const first = window.JobProgress.load(container, {customerOrderSlipId: 1});
    const second = window.JobProgress.load(container, {billingIds: [7, 8]});
    assert(pending[0].options.signal.aborted, 'Changing selection must cancel the old request.');
    const query = new URL(pending[1].url, 'http://localhost').searchParams;
    assert.deepEqual(query.getAll('billingIds'), ['7', '8']);
    assert.equal(query.get('actionStage'), '7');
    pending[1].resolve({ok: true, redirected: false, text: async () => 'Current jobs'});
    await second;
    pending[0].resolve({ok: true, redirected: false, text: async () => 'Stale job'});
    await first;
    assert.equal(container.innerHTML, 'Current jobs', 'Late responses must not replace current progress.');
    await window.JobProgress.load(container, {});
    assert.equal(container.innerHTML, '');
    assert.equal(pending.length, 2, 'Empty selection must not request progress.');
    const tickets = window.JobProgress.load(container, {dispatchTicketIds: [11, 12]});
    assert.deepEqual(new URL(pending[2].url, 'http://localhost').searchParams.getAll('dispatchTicketIds'), ['11', '12']);
    pending[2].resolve({ok: true, redirected: true});
    await tickets;
    assert.equal(container.textContent, 'Unable to load Job Progress. Refresh and check your access.');
    console.log('PASS: confirmation links open their review page; financial forms load progress; multiple jobs, selection changes, late responses and access errors are handled.');
})().catch(error => { console.error(error); process.exitCode = 1; });
