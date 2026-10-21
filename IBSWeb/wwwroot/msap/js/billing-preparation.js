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
        $('#completedServices').empty().append($('<tr>').append($('<td colspan="6">').text('Select a COS to view completed services.')));
        $('#bookingVessel, #bookingLocation, #bookingVoyage, #bookingDates, #bookingDiscount').text('—');
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
        const basic = tickets.reduce((sum, ticket) => sum + Number(ticket.dispatchNetAmount), 0);
        const baf = tickets.reduce((sum, ticket) => sum + Number(ticket.bafNetAmount), 0);
        $('#basicCharges').text(money(basic));
        $('#bafCharges').text(money(baf));
        $('#balanceNote').text(split ? 'Payments and remaining balances are tracked separately for each billing number.' : 'The charges are included in one billing balance.');
        $('#preparationNote').text(split ? 'One preparation · Two billing numbers · Separate balances' : 'One preparation · One billing number');
    }

    function renderServices() {
        const $body = $('#completedServices').empty();
        const groups = new Map();
        tickets.forEach(ticket => {
            if (!groups.has(ticket.tugboat)) groups.set(ticket.tugboat, []);
            groups.get(ticket.tugboat).push(ticket);
        });
        groups.forEach((services, tugboat) => {
            $body.append($('<tr class="billing-tugboat-row">').append($('<th colspan="6" scope="rowgroup">').append(
                $('<span>').text(tugboat), $('<span class="billing-tugboat-owner ui-caption ui-muted">').text(services[0].isCompanyOwned ? 'Company-owned' : 'Subcontractor'))));
            services.forEach(ticket => {
                const $row = $('<tr>');
                $row.append($('<td>').append($('<strong>').text(ticket.service), $('<span class="billing-ticket-number">').text(ticket.dispatchNo)));
                [ticket.actualHours == null ? '—' : Number(ticket.actualHours).toFixed(2), Number(ticket.duration).toFixed(2),
                    money(ticket.dispatchNetAmount), money(ticket.bafNetAmount),
                    (ticket.isManualTariffRate ? 'Manual' : 'Table') + ' / ' + (ticket.isManualBafRate ? 'Manual' : 'Table')]
                    .forEach(value => $row.append($('<td>').text(value)));
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
            $('#bookingDiscount').text(header.skipCosDiscount ? 'Skipped' : header.cosDiscountPercent == null ? 'As recorded on tickets' : header.cosDiscountPercent + '% · Not skipped');
            $('#billingReadiness').text(tickets.length ? 'Ready for billing · All required services recorded and charges resolved.' : 'No unbilled services are available.');
            renderServices();
            enableActions();
        } catch (error) {
            if (request === cosRequest) $('#billingReadiness').text(error.message);
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
