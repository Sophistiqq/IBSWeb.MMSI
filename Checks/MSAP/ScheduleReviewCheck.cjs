const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/schedule-confirmation.js'), 'utf8');
async function scenario(confirmations, results) {
    let submit;
    const button = {disabled: false};
    const requests = [];
    const errors = [];
    const window = {location: {href: ''}};
    const form = {
        dataset: {reapproval: 'false'}, action: '/MSAP/VesselSchedule/Confirm',
        querySelector: () => button, addEventListener: (event, handler) => submit = handler
    };
    vm.runInNewContext(source, {
        document: {querySelectorAll: () => [], getElementById: id => id === 'confirmSchedule' ? form : null},
        window, URLSearchParams,
        FormData: class { constructor() { return [['id', '5'], ['reviewedAt', '2026-10-16T10:00:00'], ['__RequestVerificationToken', 'token'], ['allowConflicts', 'true']]; } },
        $: () => ({text: value => ({html: () => String(value).replaceAll('<', '&lt;')})}),
        ModernAlert: {confirm: async () => ({isConfirmed: confirmations.shift()}), showLoading() {}, error: value => errors.push(value)},
        fetch: async (url, options) => {
            requests.push({url, body: new URLSearchParams(options.body), headers: options.headers});
            return {ok: true, json: async () => results.shift()};
        }
    });
    await Promise.all([submit({preventDefault() {}}), submit({preventDefault() {}})]);
    assert.equal(button.disabled, false, 'The approval action must recover after cancellation or failure.');
    for (const request of requests) {
        assert.equal(request.body.get('__RequestVerificationToken'), 'token');
        assert.equal(request.body.get('reviewedAt'), '2026-10-16T10:00:00');
    }
    return {requests, errors, window};
}
(async () => {
    const success = await scenario([true], [{success: true, redirectUrl: '/MSAP/CustomerOrderSlip/Details/7'}]);
    assert.equal(success.requests.length, 1);
    assert.equal(success.requests[0].body.get('allowConflicts'), 'false');
    assert.equal(success.window.location.href, '/MSAP/CustomerOrderSlip/Details/7');
    const cancelled = await scenario([false], []);
    assert.equal(cancelled.requests.length, 0);
    const overlaps = await scenario([true, true, true], [{requiresConflictAcknowledgement: true, message: '<Overlap>'}, {success: true, redirectUrl: '/approved'}]);
    assert.equal(overlaps.requests.length, 2);
    assert.equal(overlaps.requests[0].body.get('allowConflicts'), 'false');
    assert.equal(overlaps.requests[1].body.get('allowConflicts'), 'true');
    const declined = await scenario([true, true, false], [{requiresConflictAcknowledgement: true, message: 'Overlap'}]);
    assert.equal(declined.requests.length, 1, 'Declining the final acknowledgement must not retry approval.');
    const stale = await scenario([true], [{success: false, message: '<Booking changed>'}]);
    assert.equal(stale.window.location.href, '');
    assert.equal(stale.errors[0], '&lt;Booking changed>');
    const manual = await scenario([true, true], [{
        rateAcknowledgement: 'reviewed-state',
        message: 'Unpublished rates'
    }, {success: true, redirectUrl: '/approved'}]);
    assert.equal(manual.requests[0].body.has('rateAcknowledgement'), false);
    assert.equal(manual.requests[1].body.get('rateAcknowledgement'), 'reviewed-state');
    const unpublished = await scenario([true, false], [{
        rateAcknowledgement: 'reviewed-state',
        message: 'Unpublished rates'
    }]);
    assert.equal(unpublished.requests.length, 1, 'Declining manual-rate acknowledgement must not approve.');
    const both = await scenario([true, true, true, true], [{
        rateAcknowledgement: 'reviewed-state',
        message: 'Unpublished'
    },
        {requiresConflictAcknowledgement: true, message: 'Overlap'}, {success: true, redirectUrl: '/approved'}]);
    assert.equal(both.requests.length, 3);
    assert.equal(both.requests[2].body.get('rateAcknowledgement'), 'reviewed-state');
    assert.equal(both.requests[2].body.get('allowConflicts'), 'true');
    for (const result of [{exists: true, tariffRate: 0, bafRate: 1}, {exists: false}, null]) {
        const tasks = [];
        const label = {textContent: ''};
        const manualClasses = new Set();
        const tableClasses = new Set();
        const manual = {dataset: {manual: 'true', reviewRate: 'baf'}, textContent: '29,000.00',
            parentElement: {classList: {toggle: (name, on) => on ? manualClasses.add(name) : manualClasses.delete(name)}, querySelector: () => label}};
        const table = {dataset: {manual: 'false', reviewRate: 'tariff'},
            parentElement: {classList: {toggle: (name, on) => on ? tableClasses.add(name) : tableClasses.delete(name)}, querySelector: () => label}};
        const row = {dataset: {reviewService: '1'}, querySelectorAll: () => [table, manual]};
        const review = {
            dataset: {customerId: '1', terminalId: '2', plannedStart: '2026-10-16T08:00', rateUrl: '/rates'},
            querySelectorAll: () => ({forEach: callback => tasks.push(callback(row))})
        };
        vm.runInNewContext(source, {
            document: {querySelectorAll: () => [], getElementById: id => id === 'scheduleReview' ? review : null}, URLSearchParams,
            fetch: async () => ({ok: result !== null, json: async () => result})
        });
        await Promise.all(tasks);
        assert.equal(manual.textContent, '29,000.00', 'Table previews must not overwrite saved manual rates.');
        assert.equal(table.textContent, result === null ? 'Unavailable' : result.exists ? '0.00' : 'No table match');
        assert.equal(manualClasses.has('schedule-review-attention'), true, 'Saved manual rates must be flagged for attention.');
        assert.equal(tableClasses.has('schedule-review-attention'), false, 'Table-derived rates must not be flagged for attention.');
    }
    console.log('PASS: review approval/cancellation, anti-forgery and review timestamp, explicit overlap acknowledgement, stale review errors and action recovery.');
})().catch(error => { console.error(error); process.exitCode = 1; });
