const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = name => fs.readFileSync(path.join(__dirname, '../..', name), 'utf8');
const receiptView = read('IBSWeb/Areas/MSAP/Views/Collection/Preview.cshtml');
assert(receiptView.includes('@billing.AmountPaid.ToString("N2")'), 'Receipt must show the allocated payment, not only the full bill amount.');
assert(receiptView.includes('@Model.WVAT.ToString("N2")'), 'Receipt totals must include WVAT.');
assert(receiptView.includes('!Model.IsMonthClosed && !Model.ClearedDate.HasValue'), 'Receipt actions must respect closed and cleared records.');
assert(receiptView.includes('JobProgressActionTargetId'), 'Sidebar actions must distinguish this receipt from other receipts.');
assert(read('IBSWeb/Areas/MSAP/Views/Shared/_JobProgress.cshtml').includes('!(actionHere && Model.BankingCollection != null)'), 'Use the designated receipt controls instead of duplicating banking actions.');
const bankingView = read('IBSWeb/Areas/MSAP/Views/Collection/_Banking.cshtml');
assert(bankingView.includes('banks.Count == 1 ? banks[0].Value : ""'), 'Only a single receiving account should be defaulted.');
assert(bankingView.includes('Deposit to MMSI account'), 'The receiving account must be distinguished from the issuing bank.');
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
