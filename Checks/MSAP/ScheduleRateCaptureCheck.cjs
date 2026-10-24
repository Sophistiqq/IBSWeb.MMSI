const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../IBSWeb/wwwroot/msap/js/schedule-rate-capture.js'), 'utf8');

async function scenario({
                            kind = '1',
                            cancelDate = false,
                            cancelApproval = false,
                            previewFailure = false,
                            saveFailure = false
                        } = {}) {
    let click;
    let reloads = 0;
    const requests = [];
    const dialogs = [];
    const errors = [];
    const button = {
        disabled: false, dataset: {kind, scheduleId: '5', entryId: '7', effectiveDate: '2026-10-20'},
        addEventListener: (_, handler) => click = handler
    };
    const input = {
        value: '2026-10-13', reportValidity: () => true, focus() {
        }
    };
    const review = {dataset: {capturePreviewUrl: '/preview', captureUrl: '/capture'}, querySelectorAll: () => [button]};
    vm.runInNewContext(source, {
        document: {
            getElementById: id => id === 'scheduleReview' ? review : input,
            querySelector: () => ({value: 'anti-forgery'})
        },
        window: {location: {reload: () => reloads++}}, URLSearchParams,
        $: () => ({text: value => ({html: () => String(value).replaceAll('<', '&lt;').replaceAll('"', '&quot;')})}),
        ModernAlert: {
            error: value => errors.push(value), confirm: async options => {
                dialogs.push(options);
                if (dialogs.length === 1) {
                    options.didOpen();
                    return {isConfirmed: !cancelDate, value: options.preConfirm()};
                }
                return {isConfirmed: !cancelApproval};
            }
        },
        fetch: async (url, options = {}) => {
            requests.push({url, options});
            if (url.startsWith('/preview')) return {
                ok: !previewFailure, json: async () => previewFailure ? {message: '<No access>'} : {
                    fingerprint: 'trusted-review', chargingContext: 'PerHour', rate: {
                        customerName: '<Customer>',
                        portName: 'Port',
                        terminalName: 'Terminal',
                        serviceName: 'Service',
                        storedAmountText: null,
                        amountText: kind === '2' ? '0' : '100.0000'
                    }
                }
            };
            return {
                ok: !saveFailure,
                json: async () => saveFailure ? {message: 'Stored rate changed'} : {url: '/rate-evidence'}
            };
        }
    });
    await Promise.all([click(), click()]);
    assert.equal(button.disabled, false, 'The save action must recover after cancellation or failure.');
    return {requests, dialogs, errors, reloads};
}

(async () => {
    const basic = await scenario();
    assert.equal(basic.dialogs[0].title, 'Save Basic for reuse');
    assert.equal(basic.requests.length, 2, 'Double clicks must not duplicate capture.');
    assert.match(basic.requests[0].url, /effectiveDate=2026-10-13/);
    assert.match(basic.dialogs[1].text, /&lt;Customer>/, 'Source names must be escaped.');
    const body = basic.requests[1].options.body;
    assert.equal(body.get('fingerprint'), 'trusted-review');
    assert.equal(body.get('__RequestVerificationToken'), 'anti-forgery');
    assert.equal(body.get('effectiveDate'), '2026-10-13');
    assert.equal(basic.reloads, 1);
    const baf = await scenario({kind: '2'});
    assert.equal(baf.dialogs[0].title, 'Save BAF for reuse');
    assert.match(baf.dialogs[0].text, /Tuesday/);
    assert.match(baf.dialogs[1].text, /Approved amount: 0/);
    assert.equal((await scenario({cancelDate: true})).requests.length, 0);
    assert.equal((await scenario({cancelApproval: true})).requests.length, 1);
    const denied = await scenario({previewFailure: true});
    assert.equal(denied.requests.length, 1);
    assert.equal(denied.errors[0], '&lt;No access>');
    const stale = await scenario({saveFailure: true});
    assert.equal(stale.reloads, 0);
    assert.equal(stale.errors[0], 'Stored rate changed');
    console.log('PASS: Basic/BAF capture, explicit effective date and zero review, escaped context, anti-forgery, stale saves, cancellation and duplicate-click recovery.');
})().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
