/* Billing preparation uses saved service charges; no rate or ticket-selection editors. */
$(function () {
    const form = document.getElementById('billingPreparationForm');
    if (!form) return;
    const $customer = $('#CustomerId');
    const $cos = $('#CustomerOrderSlipSelect');
    const $principal = $('#PrincipalSelect');
    const $recipient = $('#BilledTo');
    let customer = null;
    let principals = [];
    let tickets = [];
    let customerRequest = 0;
    let cosRequest = 0;
    let saving = false;
    let initialCos = form.dataset.initialCos;
    let commercialEnabled = false;
    let defaultDiscount = 0;
    const money = value => Number(value).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const date = value => value ? new Date(value + 'T00:00:00').toLocaleDateString('en-PH', { day: 'numeric', month: 'short', year: 'numeric' }) : '—';

    async function getJson(url, values) {
        const response = await fetch(url + '?' + new URLSearchParams(values));
        if (!response.ok || response.redirected) throw new Error('Unable to load billing details. Refresh and check your access.');
        const data = await response.json();
        if (data.success === false) throw new Error(data.message || 'Unable to load billing details.');
        return data;
    }

    function enableActions() {
        $('#saveBilling, #previewBilling').prop('disabled', saving || !customer || !tickets.length);
    }

    function resetServices() {
        cosRequest++;
        tickets = [];
        commercialEnabled = false;
        defaultDiscount = 0;
        $('#sbmaCard').prop('hidden', true);
        $('#CosDiscountPercent, #SkipCosDiscount, #SbmaEnabled, #sbmaFalse, #discountToggle').prop('disabled', true);
        $('#CosDiscountPercent').val('');
        $('#SkipCosDiscount, #SbmaEnabled').prop('checked', false);
        $('#discountCard').removeClass('is-enabled');
        $('#discountToggle').attr('aria-pressed', 'false');
        $('#discountHelp').text('Select a COS to load its default.');
        $('.billing-discount-column').prop('hidden', true);
        $('#serviceAmountsNote').text('Approved rates');
        $('#completedServices').empty().append($('<tr>').append($('<td colspan="6">').text('Select a COS to view completed services.')));
        $('#bookingVessel, #bookingLocation, #bookingVoyage, #bookingDates, #bookingIoc').text('—');
        $('#billingReadiness').text('Select a COS to load completed services.');
        $('#cosDetailsLink').prop('hidden', true);
        JobProgress.load(document.getElementById('financialJobProgress'), {});
        updateSummary();
        enableActions();
    }

    function updateRecipient() {
        const isPrincipal = $recipient.val() === 'PRINCIPAL';
        $('#principalField').prop('hidden', !isPrincipal);
        $principal.prop('disabled', !isPrincipal).prop('required', isPrincipal);
        const selected = isPrincipal ? principals.find(p => String(p.value) === $principal.val()) : customer;
        $('#recipientAddress').text(selected?.address || '—');
        $('#recipientTin').text(selected?.tinNo || '—');
        $('#recipientTerms, #bookingTerms').text(selected?.terms || '—');
        $('#recipientNote').text(isPrincipal ? 'Both Basic and BAF statements are addressed to the selected principal.' : 'The statement is addressed to the service customer.');
    }

    function updateSummary() {
        const split = $('#SeparateBaf').is(':checked');
        const undocumented = $('#IsUndocumented').is(':checked');
        if (undocumented) $('#MsapBillingNumber, #BafBillingNumber').val('');
        $('#MsapBillingNumber').prop('required', !undocumented).prop('disabled', undocumented).attr('placeholder', undocumented ? 'Generated when saved' : 'B000000');
        $('#bafNumberField, .split-only').prop('hidden', !split);
        $('#BafBillingNumber').prop('disabled', !split || undocumented).prop('required', split && !undocumented).attr('placeholder', undocumented ? 'Generated when saved' : 'BAF billing number');
        $('#basicNumberLabel').text((split ? 'Basic billing number' : 'Billing number') + (undocumented ? ' · Automatic' : ' *'));
        $('label[for="BafBillingNumber"]').text(undocumented ? 'BAF billing number · Automatic' : 'BAF billing number *');
        $('#basicSummaryHeading').text('Basic' + (split && $('#MsapBillingNumber').val() ? ' · ' + $('#MsapBillingNumber').val() : ''));
        $('#bafSummaryHeading').text('BAF' + (split && $('#BafBillingNumber').val() ? ' · ' + $('#BafBillingNumber').val() : ''));
        const amounts = tickets.map(charges);
        const basic = roundMoney(amounts.reduce((sum, amount) => sum + amount.basic, 0));
        const baf = roundMoney(amounts.reduce((sum, amount) => sum + amount.baf, 0));
        const basicGross = roundMoney(amounts.reduce((sum, amount) => sum + amount.basicGross, 0));
        const bafGross = roundMoney(amounts.reduce((sum, amount) => sum + amount.bafGross, 0));
        const showDiscount = tickets.some(ticket => appliedDiscount(ticket) > 0);
        $('.billing-discount-row').prop('hidden', !showDiscount);
        $('#basicGross').text(money(basicGross));
        $('#bafGross').text(money(bafGross));
        $('#basicDiscount').text(money(roundMoney(basicGross - basic)));
        $('#bafDiscount').text(money(roundMoney(bafGross - baf)));
        $('#basicCharges').text(money(basic));
        $('#bafCharges').text(money(baf));
        for (const [kind, amount] of [['basic', basic], ['baf', baf]]) {
            const totals = statementTotals(amount);
            $('#' + kind + 'VatRow').prop('hidden', totals.vat === 0);
            $('#' + kind + 'Vat').text(money(totals.vat));
            $('#' + kind + 'EwtRow').prop('hidden', totals.ewt === 0);
            $('#' + kind + 'Ewt').text(money(totals.ewt));
            $('#' + kind + 'WvatRow').prop('hidden', totals.wvat === 0);
            $('#' + kind + 'Wvat').text(money(totals.wvat));
            $('#' + kind + 'Due').text(money(totals.due));
        }
        const sbma = $('#SbmaEnabled').is(':checked')
            ? roundMoney(tickets.reduce((sum, ticket) => sum + Number(ticket.sbmaShareIfEnabled || 0), 0)) : 0;
        $('#sbmaAmount').text(money(sbma));
        $('#combinedSummary').prop('hidden', split);
        $('#combinedDue').text(money(statementTotals(basic + baf).due));
        $('#balanceNote').text(split ? 'Payments and remaining balances are tracked separately for each billing number.' : 'The charges are included in one billing balance.');
        $('#preparationNote').text(split ? 'One preparation · Two billing numbers · Separate balances' : 'One preparation · One billing number');
    }

    function updateDiscount() {
        const enabled = commercialEnabled && !$('#SkipCosDiscount').is(':checked');
        $('#CosDiscountPercent').prop('readOnly', !enabled);
        $('#discountCard').toggleClass('is-enabled', enabled);
        $('#discountToggle').attr('aria-pressed', String(enabled));
        $('#discountHelp').text(!commercialEnabled ? 'No linked schedule.' : enabled ? 'Applies to tariff and BAF.' : defaultDiscount > 0 ? 'Default ' + defaultDiscount + '% skipped. Click to enable.' : 'No discount. Click to enable.');
        renderServices();
    }

    function roundMoney(amount) {
        const cents = amount * 100;
        const floor = Math.floor(cents);
        return (Math.abs(cents - floor - 0.5) < 1e-8 ? floor + floor % 2 : Math.round(cents)) / 100;
    }

    function statementTotals(gross) {
        const vat = customer?.vatType === 'Vatable' ? roundMoney(gross * 0.12 / 1.12) : 0;
        const net = roundMoney(gross - vat);
        const ewt = customer?.withholdingTax ? roundMoney(net * 0.02) : 0;
        const wvat = customer?.withholdingTax && customer?.withholdingVat ? roundMoney(net * 0.05) : 0;
        return { vat, ewt, wvat, due: roundMoney(gross - ewt - wvat) };
    }

    function appliedDiscount(ticket) {
        if (!commercialEnabled) return Math.max(Number(ticket.dispatchDiscount || 0), Number(ticket.bafDiscount || 0));
        return $('#SkipCosDiscount').is(':checked') ? 0 : Number($('#CosDiscountPercent').val());
    }

    function charges(ticket) {
        if (!commercialEnabled) {
            return {
                basicGross: Number(ticket.dispatchAmount ?? ticket.dispatchNetAmount),
                bafGross: Number(ticket.bafAmount ?? ticket.bafNetAmount),
                basic: Number(ticket.dispatchNetAmount), baf: Number(ticket.bafNetAmount)
            };
        }
        const factor = 1 - appliedDiscount(ticket) / 100;
        const hours = Math.max(Number(ticket.actualHours ?? ticket.duration), 1);
        const basicGross = Number(ticket.dispatchRate) * (ticket.dispatchChargeType === 'Per hour' ? hours : 1);
        const bafGross = Number(ticket.bafRate) * (ticket.bafChargeType === 'Per hour' ? hours : 1);
        return {
            basicGross: roundMoney(basicGross), bafGross: roundMoney(bafGross),
            basic: roundMoney(basicGross * factor), baf: roundMoney(bafGross * factor)
        };
    }

    function renderServices() {
        const $body = $('#completedServices').empty();
        const showDiscount = tickets.some(ticket => appliedDiscount(ticket) > 0);
        const columnCount = showDiscount ? 9 : 6;
        $('.billing-discount-column').prop('hidden', !showDiscount);
        $('#serviceAmountsNote').text(showDiscount ? 'Original amounts, discount and net charges' : 'Approved rates');
        const groups = new Map();
        tickets.forEach(ticket => {
            if (!groups.has(ticket.tugboat)) groups.set(ticket.tugboat, []);
            groups.get(ticket.tugboat).push(ticket);
        });
        groups.forEach((services, tugboat) => {
            $body.append($('<tr class="billing-tugboat-row">').append($('<th scope="rowgroup">').attr('colspan', columnCount).append(
                $('<span>').text(tugboat), $('<span class="billing-tugboat-owner ui-caption ui-muted">').text(services[0].isCompanyOwned ? 'Company-owned' : 'Subcontractor'))));
            services.forEach(ticket => {
                const $row = $('<tr>');
                const amount = charges(ticket);
                $row.append($('<td>').append($('<strong>').text(ticket.service), $('<span class="billing-ticket-number">').text(ticket.dispatchNo)));
                [ticket.actualHours == null ? '—' : Number(ticket.actualHours).toFixed(2), Number(ticket.duration).toFixed(2),
                    money(amount.basicGross), money(amount.bafGross)]
                    .forEach(value => $row.append($('<td>').text(value)));
                if (showDiscount) {
                    const discountLabel = !commercialEnabled && Number(ticket.dispatchDiscount || 0) !== Number(ticket.bafDiscount || 0)
                        ? 'Basic ' + Number(ticket.dispatchDiscount || 0) + '% / BAF ' + Number(ticket.bafDiscount || 0) + '%'
                        : appliedDiscount(ticket) + '%';
                    $row.append($('<td>').text(discountLabel));
                    $row.append($('<td>').text(money(amount.basic)), $('<td>').text(money(amount.baf)));
                }
                $row.append($('<td>').text((ticket.isManualTariffRate ? 'Manual' : 'Table') + ' / ' + (ticket.isManualBafRate ? 'Manual' : 'Table')));
                $body.append($row);
            });
        });
        updateSummary();
    }

    $customer.on('change', async function () {
        const request = ++customerRequest;
        customer = null;
        principals = [];
        resetServices();
        $cos.empty().append('<option value="">Select COS</option>').prop('disabled', true);
        $principal.empty().append('<option value="">Select principal</option>');
        $recipient.empty().append('<option value="LOCAL">Customer</option>').val('LOCAL').trigger('change');
        updateRecipient();
        $('#customerVat, #customerWithholding').prop('checked', false);
        const id = $customer.val();
        if (!id) return;
        try {
            const [details, jobs, recipients] = await Promise.all([
                getJson(form.dataset.customerUrl, { customerId: id }),
                getJson(form.dataset.cosListUrl, { customerId: id }),
                getJson(form.dataset.principalsUrl, { customerId: id })
            ]);
            if (request !== customerRequest) return;
            customer = details;
            principals = recipients;
            jobs.forEach(job => $cos.append($('<option>').val(job.value).text(job.name)));
            $cos.prop('disabled', false);
            principals.forEach(principal => $principal.append($('<option>').val(principal.value).text(principal.name)));
            if (principals.length) $recipient.append('<option value="PRINCIPAL">Principal</option>');
            $('#customerVat').prop('checked', customer.vatType === 'Vatable');
            $('#customerWithholding').prop('checked', !!customer.withholdingTax);
            $('#IsUndocumented').prop('checked', customer.isUndoc === 'Undocumented');
            updateSummary();
            updateRecipient();
            if (initialCos && jobs.some(job => String(job.value) === initialCos)) $cos.val(initialCos);
            initialCos = null;
            $cos.trigger('change');
        } catch (error) {
            if (request === customerRequest) $('#billingReadiness').text(error.message);
        }
    });

    $cos.on('change', async function () {
        resetServices();
        const id = $cos.val();
        const request = cosRequest;
        if (!id) return;
        $('#billingReadiness').text('Checking completed services and charges…');
        JobProgress.load(document.getElementById('financialJobProgress'), { customerOrderSlipId: id });
        $('#cosDetailsLink').attr('href', form.dataset.detailsUrl + '?' + new URLSearchParams({ id })).prop('hidden', false);
        try {
            const response = await getJson(form.dataset.ticketsUrl, { customerOrderSlipId: id });
            if (request !== cosRequest) return;
            tickets = response.tickets;
            const header = response.header;
            $('#bookingVessel').text(header.vesselName || '—');
            $('#bookingLocation').text((header.portName || '—') + ' / ' + (header.terminalName || '—'));
            $('#bookingVoyage').text(header.voyageNumber || '—');
            $('#bookingDates').text(date(header.serviceStart) + ' – ' + date(header.serviceEnd));
            $('#bookingIoc').text(header.isIoc ? 'IOC' : 'Non-IOC');
            commercialEnabled = header.hasSchedule;
            $('#CosDiscountPercent, #SkipCosDiscount, #SbmaEnabled, #sbmaFalse, #discountToggle').prop('disabled', !commercialEnabled);
            defaultDiscount = Number(header.cosDiscountPercent) || 0;
            $('#CosDiscountPercent').val(header.skipCosDiscount ? 0 : defaultDiscount);
            $('#SkipCosDiscount').prop('checked', header.skipCosDiscount || defaultDiscount <= 0);
            $('#SbmaEnabled').prop('checked', !!header.sbmaEnabled);
            const canSetSbma = commercialEnabled && /^subic(?:\s|$)/i.test(header.portName?.trim() || '');
            $('#sbmaCard').prop('hidden', !canSetSbma);
            $('#SbmaEnabled, #sbmaFalse').prop('disabled', !canSetSbma);
            updateDiscount();
            $('#billingReadiness').text(tickets.length ? 'Ready for billing · All required services recorded and charges resolved.' : 'No unbilled services are available.');
            enableActions();
        } catch (error) {
            if (request === cosRequest) $('#billingReadiness').text(error.message);
        }
    });

    $('#discountCard').on('click', function (event) {
        if (!commercialEnabled) return;
        const skipped = $('#SkipCosDiscount').is(':checked');
        if (event.target.closest('input') && !$('#CosDiscountPercent').prop('readOnly')) return;
        if (!skipped && !event.target.closest('button')) return;
        $('#SkipCosDiscount').prop('checked', !skipped);
        if (!skipped) $('#CosDiscountPercent').val('0');
        updateDiscount();
        if (skipped) $('#CosDiscountPercent').trigger('focus');
    });
    $('#CosDiscountPercent').on('input', renderServices);
    $('#SbmaEnabled').on('change', updateSummary);
    $('#CosDiscountPercent').on('blur', function () {
        if ($('#CosDiscountPercent').val() === '' || Number($('#CosDiscountPercent').val()) === 0) {
            $('#CosDiscountPercent').val('0');
            $('#SkipCosDiscount').prop('checked', true);
            updateDiscount();
        }
    });
    $recipient.on('change', updateRecipient);
    $principal.on('change', updateRecipient);
    $('#SeparateBaf, #IsUndocumented').on('change', updateSummary);
    $('#MsapBillingNumber, #BafBillingNumber').on('input', updateSummary);
    $(form).on('submit', async function (event) {
        const preview = event.originalEvent?.submitter?.id === 'previewBilling';
        if (!preview) event.preventDefault();
        if ($recipient.val() === 'PRINCIPAL' && !$principal.val()) {
            event.preventDefault();
            ModernAlert.error('Select the principal to receive the billing.');
            return;
        }
        if ($recipient.val() === 'PRINCIPAL' && !$principal.val()) {
            event.preventDefault();
            ModernAlert.error('Select the principal to receive the billing.');
            return;
        }
        if (saving || !tickets.length || !form.reportValidity()) {
            event.preventDefault();
            return;
        }
        if (!$('#IsUndocumented').is(':checked') && $('#SeparateBaf').is(':checked') && $('#MsapBillingNumber').val()?.trim().toUpperCase() === $('#BafBillingNumber').val()?.trim().toUpperCase()) {
            event.preventDefault();
            ModernAlert.error('Basic and BAF billing numbers must be different.');
            return;
        }
        if (preview) return;
        saving = true;
        enableActions();
        try {
            const confirmation = await ModernAlert.confirm({ title: 'Save billing for reviewing?', text: $('#SeparateBaf').is(':checked') ? 'Create separate Basic and BAF billings with their own numbers and balances.' : 'Create the billing using all completed COS services.', confirmText: 'Save billing', cancelText: 'Keep reviewing' });
            if (!confirmation.isConfirmed) return;
            ModernAlert.showLoading('Saving billing…');
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Unable to save billing. Refresh and check your access.');
            const result = await response.json();
            if (!result.success) throw new Error(result.message || 'Unable to save billing.');
            await ModernAlert.success(result.message);
            window.location.href = result.redirectUrl;
        } catch (error) {
            ModernAlert.error(error.message);
        } finally {
            saving = false;
            enableActions();
        }
    });
    updateSummary();
    if ($customer.val()) $customer.trigger('change');
});
