const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = name => fs.readFileSync(path.join(__dirname, '../..', name), 'utf8');
const alertContext = {window: {}, Swal: {fire(options) { alertContext.options = options; }}, $: () => ({find: () => ({attr() {}})})};
vm.runInNewContext(read('IBSWeb/wwwroot/msap/js/modern-alert.js'), alertContext);
let opened = false;
const preConfirm = () => 'validated';
alertContext.window.modernAlert.confirm({didOpen() { opened = true; }, preConfirm});
alertContext.options.didOpen({});
assert(opened, 'ModernAlert must initialize its input controls.');
assert.equal(alertContext.options.preConfirm, preConfirm, 'ModernAlert must run input validation before closing.');
(async () => {
    for (const scenario of ['save', 'cancel', 'invalid', 'failure']) {
        let listener;
        let reloads = 0;
        let error;
        const requests = [];
        const form = {action: '/MSAP/Collection/Banking', reportValidity: () => scenario !== 'invalid', addEventListener() {}};
        const popup = {querySelector: () => form};
        const context = {
            document: {addEventListener(event, callback) { listener = callback; }},
            window: {location: {reload() { reloads++; }}}, URLSearchParams,
            FormData: class { *[Symbol.iterator]() { yield ['id', '55']; yield ['bankingAction', 'Deposit']; yield ['bankId', '7']; yield ['date', '2026-10-12']; yield ['__RequestVerificationToken', 'test-token']; } },
            ModernSelect: {init(selector) { assert.equal(selector, '#collectionBankingForm .js-modern-select'); }},
            Swal: {getPopup: () => popup},
            ModernAlert: {
                async confirm(options) {
                    options.didOpen(popup);
                    if (scenario === 'cancel') return {isConfirmed: false};
                    const value = options.preConfirm();
                    return {isConfirmed: value !== false, value};
                },
                showLoading() {}, error(value) { error = value; }
            },
            $: () => ({text(value) { this.value = value; return this; }, html() { return this.value; }}),
            async fetch(url, options) {
                requests.push({url, options});
                return options.method === 'POST'
                    ? {ok: true, json: async () => ({success: scenario !== 'failure', message: 'Period is closed.'})}
                    : {ok: true, text: async () => '<form id="collectionBankingForm"></form>'};
            }
        };
        vm.runInNewContext(read('IBSWeb/wwwroot/msap/js/collection-banking.js'), context);
        const button = {href: '/MSAP/Collection/Banking/55?bankingAction=Deposit', dataset: {collectionBanking: 'Record Deposit'}};
        await listener({target: {closest: () => button}, preventDefault() {}});
        assert.equal(requests.length, scenario === 'save' || scenario === 'failure' ? 2 : 1, 'Cancelled or invalid forms must not submit.');
        assert.equal(reloads, scenario === 'save' ? 1 : 0, 'Only successful updates should refresh Job Progress.');
        if (scenario === 'save') {
            assert.equal(requests[1].options.body.get('bankingAction'), 'Deposit');
            assert.equal(requests[1].options.body.get('bankId'), '7');
            assert.equal(requests[1].options.body.get('__RequestVerificationToken'), 'test-token');
        }
        if (scenario === 'failure') assert.equal(error, 'Period is closed.');
    }
    console.log('PASS: ModernAlert banking inputs, validation, cancellation, bank selection, antiforgery submission, errors and progress refresh.');
})().catch(error => { console.error(error); process.exitCode = 1; });
