const CollectionAllocation = {
    round(value) {
        const scaled = value * 100;
        const floor = Math.floor(scaled);
        return (Math.abs(scaled - floor - 0.5) < 1e-8
            ? (floor % 2 === 0 ? floor : floor + 1) : Math.round(scaled)) / 100;
    },
    calculate(bill, payment) {
        if (!Number.isFinite(payment) || payment <= 0 || payment !== bill.net) {
            throw new RangeError('Each selected billing must be paid in full.');
        }
        return { ewt: bill.ewt, wvat: bill.wvat, gross: bill.amount, remaining: 0 };
    }
};
if (typeof module !== 'undefined') module.exports = CollectionAllocation;
if (typeof document !== 'undefined') $(function () {
    const form = document.getElementById('collectionForm');
    if (!form) return;
    const money = value => Number(value).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const submitted = JSON.parse(document.getElementById('collectionPayments').textContent) || [];
    let restoreSubmitted = true;
    let bills = [];
    let request = 0;
    let loading = false;
    let saving = false;
    let paymentMode = form.dataset.initialMode || 'check';
    let autoSplitCheck = true;
    const selected = () => bills.filter(b => b.selected);
    const error = message => $('#collectionError').text(message).prop('hidden', !message);

    function syncPaymentModeUi() {
        $('.collection-mode-btn').each(function () {
            const active = this.dataset.paymentMode === paymentMode;
            $(this).toggleClass('is-active', active).attr('aria-pressed', String(active));
        });
        $('#cashAmountField').prop('hidden', paymentMode === 'check');
        $('#checkAmountField').prop('hidden', paymentMode === 'cash');
        $('#CashAmount').prop('readOnly', paymentMode === 'cash');
        $('#CheckAmount').prop('readOnly', paymentMode === 'check');
        if (paymentMode !== 'cash' && !$('#CheckDate').val() && $('#Date').val()) {
            $('#CheckDate').val($('#Date').val());
        }
    }

    function updateProgress() {
        const picker = document.getElementById('progressCos');
        const previous = picker.value || form.dataset.initialCos;
        const jobs = new Map();
        selected().forEach(b => { if (b.customerOrderSlipId) jobs.set(String(b.customerOrderSlipId), b.cosNumber || `COS ${b.customerOrderSlipId}`); });
        picker.replaceChildren();
        jobs.forEach((label, id) => picker.add(new Option(label, id)));
        if (jobs.has(previous)) picker.value = previous;
        picker.disabled = jobs.size === 0;
        if (!jobs.size) picker.add(new Option('Select billings to view progress', ''));
        refreshModernSelect($(picker));
        JobProgress.load(document.getElementById('financialJobProgress'), { customerOrderSlipId: picker.value });
    }

    function updateTotals(progressChanged = false) {
        let gross = 0, net = 0, ewt = 0, wvat = 0, allocated = 0;
        selected().forEach(b => {
            const part = CollectionAllocation.calculate(b, b.payment);
            gross += b.amount; net += b.payment; ewt += part.ewt; wvat += part.wvat; allocated += part.gross;
        });
        net = CollectionAllocation.round(net);
        ewt = CollectionAllocation.round(ewt);
        wvat = CollectionAllocation.round(wvat);

        if (paymentMode === 'check') {
            $('#CashAmount').val('0.00');
            $('#CheckAmount').val(net.toFixed(2));
        } else if (paymentMode === 'cash') {
            $('#CashAmount').val(net.toFixed(2));
            $('#CheckAmount').val('0.00');
        } else if (autoSplitCheck) {
            $('#CheckAmount').val(Math.max(0, CollectionAllocation.round(net - Number($('#CashAmount').val()))).toFixed(2));
        }

        const cashVal = Number($('#CashAmount').val()) || 0;
        const checkVal = Number($('#CheckAmount').val()) || 0;
        const received = CollectionAllocation.round(cashVal + checkVal);
        $('#Amount').val(received.toFixed(2));
        $('#EWT').val(ewt.toFixed(2)); $('#WVAT').val(wvat.toFixed(2));
        $('#summaryCount').text(selected().length);
        $('#selectedCount').text(`${selected().length} bills / ${new Set(selected().map(b => b.customerOrderSlipId).filter(Boolean)).size} COS`);
        $('#summaryGross').text(money(gross)); $('#summaryReceived').text(money(received));
        $('#summaryEwt').text(money(ewt)); $('#summaryWvat').text(money(wvat));
        $('#summaryAllocated').text(money(allocated)); $('#summaryRemaining').text(money(gross - allocated));
        const matches = CollectionAllocation.round(received - net) === 0;
        $('#settlementStatus').text(matches ? 'Cash plus check matches the allocated net payments.' : `Cash plus check must equal ${money(net)} in allocated net payments.`).toggleClass('text-error', !matches);
        const needsCheck = paymentMode === 'check' || (paymentMode === 'split' && checkVal > 0);
        $('#checkDetailsSection').prop('hidden', !needsCheck);
        $('#CheckDate, #CheckNumber').prop('required', needsCheck && checkVal > 0);
        $('#saveCollection').prop('disabled', saving || loading || !selected().length || !matches);
        if (progressChanged) updateProgress();
    }

    function visible(b) {
        const query = $('#billingSearch').val().trim().toLowerCase();
        return (!$('#billedParty').val() || b.billedParty === $('#billedParty').val())
            && `${b.msapBillingNumber} ${b.cosNumber || ''}`.toLowerCase().includes(query);
    }

    function render() {
        const body = $('#collectionBillings').empty();
        bills.filter(visible).forEach(b => {
            const row = $('<tr>').toggleClass('is-selected', b.selected).toggleClass('is-unavailable', !b.eligible);
            const checkbox = $('<input type="checkbox">').prop({ checked: b.selected, disabled: !b.eligible }).attr('aria-label', `Select ${b.msapBillingNumber}`);
            row.append($('<td>').append(checkbox));
            row.append($('<td>').append($('<strong>').text(`${b.msapBillingNumber} / ${b.component}`), $('<p class="ui-caption ui-muted">').text(`${b.cosNumber || 'No COS reference'} / ${b.reason || 'Approved'}`)));
            row.append($('<td>').text(money(b.amount)));
            row.append($('<td>').text(money(b.ewt)));
            row.append($('<td>').text(money(b.wvat)));
            row.append($('<td>').text(money(b.net)));
            row.append($('<td>').text(b.eligible ? money(b.net) : 'Not eligible'));
            row.append($('<td>').text(money(b.selected ? 0 : b.amount))); body.append(row);
            checkbox.on('change', function () { b.selected = this.checked; render(); updateTotals(true); });
        });
        if (!body.children().length) {
            const emptyText = loading
                ? 'Loading billings...'
                : !$('#CustomerId').val()
                    ? 'Select a service customer to load approved billings.'
                    : 'No billings match the current filters.';
            body.append($('<tr>').append($('<td colspan="8">').text(emptyText)));
        }
        const eligible = bills.filter(b => b.eligible && visible(b));
        $('#selectAllBillings').prop({ checked: eligible.length > 0 && eligible.every(b => b.selected), indeterminate: eligible.some(b => b.selected) && !eligible.every(b => b.selected), disabled: !eligible.length });
    }

    async function loadBillings() {
        const version = ++request;
        bills = []; loading = true; error('');
        $('#billedParty').empty().append(new Option('All billed parties', ''));
        render(); updateTotals(true);
        const customerId = $('#CustomerId').val();
        if (!customerId) { loading = false; render(); updateTotals(); return; }
        try {
            const params = new URLSearchParams({ customerId });
            if ($('#MsapCollectionId').val()) params.set('collectionId', $('#MsapCollectionId').val());
            const response = await fetch(`${form.dataset.billingsUrl}?${params}`);
            if (!response.ok || response.redirected) throw new Error('Unable to load billings. Refresh and check your access.');
            const result = await response.json();
            if (version !== request) return;
            if (!result.success) throw new Error(result.message || 'Unable to load billings.');
            bills = result.data.map(b => {
                const payment = restoreSubmitted ? submitted.find(p => p.billingId === b.msapBillingId) : null;
                return { ...b, selected: b.eligible && (restoreSubmitted && submitted.length ? !!payment : b.isSelected), payment: b.net };
            });
            restoreSubmitted = false;
            [...new Set(bills.map(b => b.billedParty))].sort().forEach(party => $('#billedParty').append(new Option(party, party)));
        } catch (exception) {
            if (version === request) error(exception.message);
        } finally {
            if (version === request) { loading = false; render(); updateTotals(true); }
        }
    }

    $('#CustomerId').on('change', loadBillings);
    $('#billedParty').on('change', render);
    $('#billingSearch').on('input', render);
    $('#progressCos').on('change', function () { JobProgress.load(document.getElementById('financialJobProgress'), { customerOrderSlipId: this.value }); });
    $('#selectAllBillings').on('change', function () { bills.filter(b => b.eligible && visible(b)).forEach(b => b.selected = this.checked); render(); updateTotals(true); });
    $('.collection-mode-btn').on('click', function () {
        paymentMode = this.dataset.paymentMode;
        autoSplitCheck = true;
        if (paymentMode === 'split' && Number($('#CashAmount').val()) === 0 && Number($('#CheckAmount').val()) === 0) {
            $('#CashAmount').val('0.00');
        }
        syncPaymentModeUi();
        updateTotals();
    });
    $('#Date').on('change', () => {
        if (paymentMode !== 'cash' && !$('#CheckDate').val()) {
            $('#CheckDate').val($('#Date').val());
        }
    });
    $('#CashAmount').on('input', () => { autoSplitCheck = true; updateTotals(); });
    $('#CheckAmount').on('input', () => { autoSplitCheck = false; updateTotals(); });
    function documentationChanged() {
        const undocumented = $('#IsUndocumented').prop('checked');
        const $num = $('#MsapCollectionNumber');
        $num.prop({ required: !undocumented, readOnly: undocumented })
            .attr('placeholder', undocumented ? 'Auto-generated on save' : 'Enter receipt number');
        if (undocumented && !$('#MsapCollectionId').val()) {
            $num.val('');
        }
    }
    $('#IsUndocumented').on('change', documentationChanged);
    $(form).on('submit', async function (event) {
        event.preventDefault();
        if (saving || loading) return;
        error(''); updateTotals();
        if (Number($('#CheckAmount').val()) <= 0) {
            $('#CheckDate').val('');
            $('#CheckNumber').val('');
        }
        if (!form.reportValidity()) return;
        const payments = selected();
        if (!payments.length || payments.some(b => !b.eligible || !Number.isFinite(b.payment) || b.payment <= 0 || b.payment !== b.net)) {
            error('Select approved billings to settle their full net balances.'); return;
        }
        if (CollectionAllocation.round(Number($('#Amount').val()) - payments.reduce((sum, b) => sum + b.payment, 0)) !== 0) {
            error('Cash plus check must match the allocated net payments.'); return;
        }
        saving = true; updateTotals();
        let navigating = false;
        try {
            const result = await ModernAlert.confirm({ title: 'Save collection?', text: 'Review the receipt, selected bills and payment allocations before saving.', icon: 'question', confirmText: 'Save collection', cancelText: 'Keep reviewing' });
            if (!result.isConfirmed) return;
            $(form).find('[data-allocation]').remove();
            payments.forEach((b, index) => {
                $('<input type="hidden" data-allocation>').attr('name', `BillingPayments[${index}].BillingId`).val(b.msapBillingId).appendTo(form);
                $('<input type="hidden" data-allocation>').attr('name', `BillingPayments[${index}].AmountToPay`).val(b.payment.toFixed(2)).appendTo(form);
            });
            HTMLFormElement.prototype.submit.call(form);
            navigating = true;
        } finally {
            if (!navigating) { saving = false; updateTotals(); }
        }
    });
    syncPaymentModeUi();
    documentationChanged();
    loadBillings();
});
