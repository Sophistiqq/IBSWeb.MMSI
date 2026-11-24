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
    let representatives = [];
    let tickets = [];
    let customerRequest = 0;
    let cosRequest = 0;
    let saving = false;
    let initialCos = form.dataset.initialCos;
    let initialTerms = (form.dataset.initialTerms || '').trim();
    const initialAgencyInc = form.dataset.initialAgencyInc || '';
    const initialAgentCom = form.dataset.initialAgentCom || '';
    let syncingTerms = false;
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
        $('#CosDiscountPercent, #SkipCosDiscount, #DiscountOnTariff, #DiscountOnBaf, #DiscountScopeTotal, #SbmaEnabled, #sbmaFalse, #discountToggle').prop('disabled', true);
        $('#SeparateBaf').prop('disabled', form.dataset.editingDraft === 'true');
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

    function updateTermsOverrideHint() {
        const defaultTerms = ($('#recipientTerms').text() || '').trim();
        const currentTerms = ($('#billingTerms').val() || '').trim();
        const isTermsOverride = defaultTerms !== '' && defaultTerms !== '—' && currentTerms !== '' && currentTerms !== defaultTerms;
        $('#termsDefaultNote').prop('hidden', isTermsOverride);
        $('#termsOverrideHint').prop('hidden', !isTermsOverride).text(isTermsOverride ? `Terms override (default ${defaultTerms}) — flagged for reviewer.` : '');
    }

    function updateRecipient() {
        const isPrincipal = $recipient.val() === 'PRINCIPAL';
        $('#principalField').prop('hidden', !isPrincipal);
        $principal.prop('disabled', !isPrincipal).prop('required', isPrincipal);
        const selected = isPrincipal ? principals.find(p => String(p.value) === $principal.val()) : customer;
        $('#recipientAddress').text(selected?.address || '—');
        $('#recipientTin').text(selected?.tinNo || '—');
        $('#recipientTerms, #bookingTerms').text(selected?.terms || '—');
        if (selected?.terms && !$('#billingTerms').data('user-overridden')) {
            syncingTerms = true;
            $('#billingTerms').val(selected.terms).trigger('change');
            syncingTerms = false;
        }
        updateTermsOverrideHint();
        $('#recipientNote').text(isPrincipal ? 'Both Basic and BAF statements are addressed to the selected principal.' : 'The statement is addressed to the service customer.');
    }

    function incrementSeries(value) {
        const trimmed = (value || '').trim();
        if (!/^\d+$/.test(trimmed)) return '';
        const next = String(parseInt(trimmed, 10) + 1);
        return trimmed.length > 1 && trimmed.startsWith('0')
            ? next.padStart(trimmed.length, '0')
            : next;
    }

    function updateSummary() {
        const split = $('#SeparateBaf').is(':checked');
        const undocumented = $('#IsUndocumented').is(':checked');
        const suggestedBir = form.dataset.suggestedBirNumber || '';
        const suggestedBaf = form.dataset.suggestedBirBafNumber || '';
        if (undocumented) {
            $('#MsapBillingNumber, #BafBillingNumber').val('');
        } else {
            if (!$('#MsapBillingNumber').val()?.trim() && suggestedBir) {
                $('#MsapBillingNumber').val(suggestedBir);
            }
            if (split && !$('#BafBillingNumber').val()?.trim()) {
                const nextBaf = incrementSeries($('#MsapBillingNumber').val()) || suggestedBaf;
                if (nextBaf) $('#BafBillingNumber').val(nextBaf);
            }
        }
        $('#MsapBillingNumber').prop('required', !undocumented).prop('disabled', undocumented).attr('placeholder', undocumented ? 'Generated when saved' : (suggestedBir || 'Invoice number'));
        $('#bafNumberField, .split-only').prop('hidden', !split);
        $('#BafBillingNumber').prop('disabled', !split || undocumented).prop('required', split && !undocumented).attr('placeholder', undocumented ? 'Generated when saved' : (suggestedBaf || 'BAF billing number'));
        const currentBir = $('#MsapBillingNumber').val()?.trim() || '';
        const isSeriesOverride = !undocumented && form.dataset.editingDraft !== 'true' && suggestedBir && currentBir && currentBir !== suggestedBir;
        $('#seriesOverrideHint').prop('hidden', !isSeriesOverride).text(isSeriesOverride ? `Series override (expected ${suggestedBir}) — flagged for reviewer.` : '');
        const currentBaf = $('#BafBillingNumber').val()?.trim() || '';
        const expectedBaf = incrementSeries(currentBir) || suggestedBaf;
        const isBafSeriesOverride = split && !undocumented && form.dataset.editingDraft !== 'true' && expectedBaf && currentBaf && currentBaf !== expectedBaf;
        $('#bafSeriesOverrideHint').prop('hidden', !isBafSeriesOverride).text(isBafSeriesOverride ? `Series override (expected ${expectedBaf}) — flagged for reviewer.` : '');
        $('#basicNumberLabel').text((split ? 'Basic billing number' : 'Billing number') + (undocumented ? ' · Automatic' : ' *'));
        $('label[for="BafBillingNumber"]').text(undocumented ? 'BAF billing number · Automatic' : 'BAF billing number *');
        $('#basicSummaryHeading').text('Basic' + (split && $('#MsapBillingNumber').val() ? ' · ' + $('#MsapBillingNumber').val() : ''));
        $('#bafSummaryHeading').text('BAF' + (split && $('#BafBillingNumber').val() ? ' · ' + $('#BafBillingNumber').val() : ''));
        const amounts = tickets.map(charges);
        const basic = roundMoney(amounts.reduce((sum, amount) => sum + amount.basic, 0));
        const baf = roundMoney(amounts.reduce((sum, amount) => sum + amount.baf, 0));
        const basicGross = roundMoney(amounts.reduce((sum, amount) => sum + amount.basicGross, 0));
        const bafGross = roundMoney(amounts.reduce((sum, amount) => sum + amount.bafGross, 0));
        const showDiscount = tickets.some(ticket => appliedDiscount(ticket) > 0)
            && ($('#DiscountOnTariff').is(':checked') || $('#DiscountOnBaf').is(':checked'));
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
            $('#' + kind + 'Due').text(money(totals.due));
        }
        const sbma = $('#SbmaEnabled').is(':checked')
            ? roundMoney(tickets.reduce((sum, ticket) => sum + Number(ticket.sbmaShareIfEnabled || 0), 0)) : 0;
        $('#sbmaAmount').text(money(sbma));
        $('#combinedSummary').prop('hidden', split);
        $('#combinedDue').text(money(statementTotals(basic + baf).due));
        $('#balanceNote').text(split ? 'Payments and remaining balances are tracked separately for each billing number.' : 'The charges are included in one billing balance.');
        $('#preparationNote').text(split ? 'One preparation · Two billing numbers · Separate balances' : 'One preparation · One billing number');
        updateCommissionPreviews(basicGross, bafGross, basic + baf);
    }

    function computeCommission(rep, basicGross, bafGross, netOfDiscount) {
        if (!rep || Number(rep.rateValue) <= 0) return 0;
        const rate = Number(rep.rateValue);
        if (rep.rateMode === 'Flat') return roundMoney(rate);
        let baseAmount = netOfDiscount;
        if (rep.calculationBase === 'Gross') baseAmount = basicGross + bafGross;
        else if (rep.calculationBase === 'NetOfVat') baseAmount = customer?.vatType === 'Vatable' ? roundMoney(netOfDiscount / 1.12) : netOfDiscount;
        else if (rep.calculationBase === 'BasicOnly') baseAmount = basicGross;
        else if (rep.calculationBase === 'BafOnly') baseAmount = bafGross;
        return baseAmount > 0 ? roundMoney(baseAmount * (rate / 100)) : 0;
    }

    function updateCommissionPreviews(basicGross, bafGross, netOfDiscount) {
        const baseLabels = { NetOfDiscount: 'Net of Discount', NetOfVat: 'Net of VAT', Gross: 'Gross', BasicOnly: 'Basic Only', BafOnly: 'BAF Only' };
        [['AgencyIncentiveSelect', 'agencyIncentivePreview'], ['AgentCommissionSelect', 'agentCommissionPreview']].forEach(([selectId, previewId]) => {
            const repId = $('#' + selectId).val();
            const rep = representatives.find(r => String(r.representativeId) === String(repId));
            if (!rep) {
                $('#' + previewId).text('Hidden from client invoices.');
                return;
            }
            const amount = computeCommission(rep, basicGross, bafGross, netOfDiscount);
            const rateText = rep.rateMode === 'Flat'
                ? 'Flat ₱' + money(rep.rateValue)
                : Number(rep.rateValue) + '% of ' + (baseLabels[rep.calculationBase] || rep.calculationBase);
            $('#' + previewId).text(`${rateText} · Computed: ₱${money(amount)} (internal only)`);
        });
    }

    function populateRepresentatives(isInitialEdit) {
        const incList = representatives.filter(r => r.category === 'AgencyIncentive');
        const comList = representatives.filter(r => r.category === 'AgentCommission');
        const customerId = Number($customer.val() || 0);
        const $inc = $('#AgencyIncentiveSelect').empty().append('<option value="">None</option>');
        const $com = $('#AgentCommissionSelect').empty().append('<option value="">None</option>');
        incList.forEach(r => $inc.append($('<option>').val(r.representativeId).text(`${r.representativeName} (${r.rateMode === 'Flat' ? '₱' + money(r.rateValue) : r.rateValue + '%'})`)));
        comList.forEach(r => $com.append($('<option>').val(r.representativeId).text(`${r.representativeName} (${r.rateMode === 'Flat' ? '₱' + money(r.rateValue) : r.rateValue + '%'})`)));
        if (isInitialEdit) {
            if (initialAgencyInc) $inc.val(initialAgencyInc);
            if (initialAgentCom) $com.val(initialAgentCom);
        } else {
            const defaultInc = incList.find(r => r.customerId === customerId);
            const defaultCom = comList.find(r => r.customerId === customerId);
            if (defaultInc) $inc.val(defaultInc.representativeId);
            if (defaultCom) $com.val(defaultCom.representativeId);
        }
        $('#commissionOptionsSection').prop('hidden', representatives.length === 0);
        $inc.trigger('change');
        $com.trigger('change');
    }

    function updateDiscount() {
        const enabled = commercialEnabled && !$('#SkipCosDiscount').is(':checked');
        $('#CosDiscountPercent').prop('readOnly', true);
        $('#discountToggle').prop('disabled', true).attr('aria-pressed', String(enabled));
        $('#discountCard').toggleClass('is-enabled', enabled);
        $('#discountHelp').text(!commercialEnabled
            ? 'No linked schedule.'
            : defaultDiscount > 0
                ? 'Locked to COS default (' + defaultDiscount + '%). Override requires reviewer revision.'
                : 'No COS discount configured.');
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
        return { vat, ewt, due: roundMoney(gross - ewt) };
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
        const pct = appliedDiscount(ticket);
        const tariffFactor = $('#DiscountOnTariff').is(':checked') ? (1 - pct / 100) : 1;
        const bafFactor = $('#DiscountOnBaf').is(':checked') ? (1 - pct / 100) : 1;
        const hours = Math.max(Number(ticket.actualHours ?? ticket.duration), 1);
        const basicGross = Number(ticket.dispatchRate) * (ticket.dispatchChargeType === 'Per hour' ? hours : 1);
        const bafGross = Number(ticket.bafRate) * (ticket.bafChargeType === 'Per hour' ? hours : 1);
        return {
            basicGross: roundMoney(basicGross), bafGross: roundMoney(bafGross),
            basic: roundMoney(basicGross * tariffFactor), baf: roundMoney(bafGross * bafFactor)
        };
    }

    function buildBasisControl(ticket, field, currentValue) {
        const isToggleable = currentValue === 'Per hour' || currentValue === 'Per move';
        if (!isToggleable) {
            return currentValue ? $('<span class="billing-basis-badge">').text(currentValue) : null;
        }
        const $toggle = $('<div class="billing-basis-toggle" role="group">');
        [['Per hour', 'Per hr'], ['Per move', 'Per move']].forEach(([value, label]) => {
            $('<button type="button" class="billing-basis-btn">')
                .toggleClass('is-active', currentValue === value)
                .attr('aria-pressed', currentValue === value ? 'true' : 'false')
                .text(label)
                .on('click', () => {
                    if (ticket[field] === value) return;
                    ticket[field] = value;
                    renderServices();
                })
                .appendTo($toggle);
        });
        return $toggle;
    }

    function renderServices() {
        const $body = $('#completedServices').empty();
        const showDiscount = tickets.some(ticket => appliedDiscount(ticket) > 0)
            && ($('#DiscountOnTariff').is(':checked') || $('#DiscountOnBaf').is(':checked'));
        const columnCount = 6 + (commercialEnabled ? 2 : 0) + (showDiscount ? 3 : 0);
        $('.billing-basis-column').prop('hidden', !commercialEnabled);
        $('.billing-discount-column').prop('hidden', !showDiscount);
        $('#serviceAmountsNote').text(showDiscount ? 'Original amounts, discount and net charges' : 'Approved rates');
        const groups = new Map();
        tickets.forEach(ticket => {
            if (!groups.has(ticket.tugboat)) groups.set(ticket.tugboat, []);
            groups.get(ticket.tugboat).push(ticket);
        });
        let ticketIndex = 0;
        groups.forEach((services, tugboat) => {
            $body.append($('<tr class="billing-tugboat-row">').append($('<th scope="rowgroup">').attr('colspan', columnCount).append(
                $('<span>').text(tugboat), $('<span class="billing-tugboat-owner ui-caption ui-muted">').text(services[0].isCompanyOwned ? 'Company-owned' : 'Subcontractor'))));
            services.forEach(ticket => {
                const idx = ticketIndex++;
                const $row = $('<tr>');
                const amount = charges(ticket);
                $row.append($('<td>').append($('<strong>').text(ticket.service), $('<span class="billing-ticket-number">').text(ticket.dispatchNo)));
                [ticket.actualHours == null ? '—' : Number(ticket.actualHours).toFixed(2), Number(ticket.duration).toFixed(2)]
                    .forEach(value => $row.append($('<td>').text(value)));
                $row.append($('<td>').text(money(amount.basicGross)));
                if (commercialEnabled) {
                    $row.append($('<td class="billing-basis-cell">').append(
                        buildBasisControl(ticket, 'dispatchChargeType', ticket.dispatchChargeType),
                        $('<input type="hidden">').attr('name', `TicketBasisOverrides[${idx}].DispatchTicketId`).val(ticket.dispatchTicketId),
                        $('<input type="hidden">').attr('name', `TicketBasisOverrides[${idx}].DispatchChargeType`).val(ticket.dispatchChargeType)));
                }
                $row.append($('<td>').text(money(amount.bafGross)));
                if (commercialEnabled) {
                    $row.append($('<td class="billing-basis-cell">').append(
                        buildBasisControl(ticket, 'bafChargeType', ticket.bafChargeType),
                        $('<input type="hidden">').attr('name', `TicketBasisOverrides[${idx}].BAFChargeType`).val(ticket.bafChargeType)));
                }
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
        representatives = [];
        $('#commissionOptionsSection').prop('hidden', true);
        $('#AgencyIncentiveSelect, #AgentCommissionSelect').empty().append('<option value="">None</option>');
        resetServices();
        $cos.empty().append('<option value="">Select COS</option>').prop('disabled', true);
        $principal.empty().append('<option value="">Select principal</option>');
        $recipient.empty().append('<option value="LOCAL">Customer</option>').val('LOCAL').trigger('change');
        updateRecipient();
        $('#customerVat, #customerWithholding, #customerPrintDiscount').prop('checked', false);
        const id = $customer.val();
        if (!id) return;
        try {
            const [details, jobs, recipients, reps] = await Promise.all([
                getJson(form.dataset.customerUrl, { customerId: id }),
                getJson(form.dataset.cosListUrl, initialCos ? { customerId: id, currentCustomerOrderSlipId: initialCos } : { customerId: id }),
                getJson(form.dataset.principalsUrl, { customerId: id }),
                form.dataset.repsUrl ? getJson(form.dataset.repsUrl, { customerId: id }) : Promise.resolve([])
            ]);
            if (request !== customerRequest) return;
            customer = details;
            principals = recipients;
            representatives = reps || [];
            jobs.forEach(job => $cos.append($('<option>').val(job.value).text(job.name)));
            $cos.prop('disabled', false);
            principals.forEach(principal => $principal.append($('<option>').val(principal.value).text(principal.name)));
            if (principals.length) $recipient.append('<option value="PRINCIPAL">Principal</option>');
            $('#customerVat').prop('checked', customer.vatType === 'Vatable');
            $('#customerWithholding').prop('checked', !!customer.withholdingTax);
            $('#customerPrintDiscount, #billingPrintDiscount').prop('checked', !!customer.printDiscount);
            const isInitialEdit = Boolean(initialCos) && form.dataset.editingDraft === 'true';
            populateRepresentatives(isInitialEdit);
            if (!isInitialEdit) {
                $('#IsUndocumented').prop('checked', customer.isUndoc === 'Undocumented');
                $('#billingTerms').data('user-overridden', false);
            } else {
                if (form.dataset.initialBilledTo === 'PRINCIPAL' && principals.length) {
                    $recipient.val('PRINCIPAL');
                }
                if (form.dataset.initialPrincipal) {
                    $principal.val(form.dataset.initialPrincipal);
                }
                if (initialTerms) {
                    syncingTerms = true;
                    $('#billingTerms').val(initialTerms).trigger('change');
                    syncingTerms = false;
                    $('#billingTerms').data('user-overridden', true);
                    initialTerms = '';
                }
            }
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
            $('#SeparateBaf').prop('disabled', form.dataset.editingDraft === 'true');
            commercialEnabled = header.hasSchedule;
            $('#CosDiscountPercent, #SkipCosDiscount, #DiscountOnTariff, #DiscountOnBaf, #DiscountScopeTotal, #SbmaEnabled, #sbmaFalse').prop('disabled', !commercialEnabled);
            $('#DiscountScopeTotal').prop('checked', $('#DiscountOnTariff').is(':checked') && $('#DiscountOnBaf').is(':checked'));
            $('#discountToggle').prop('disabled', true);
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

    $('#DiscountScopeTotal').on('change', function () {
        const checked = $(this).is(':checked');
        $('#DiscountOnTariff, #DiscountOnBaf').prop('checked', checked);
        renderServices();
    });
    $('#DiscountOnTariff, #DiscountOnBaf').on('change', function () {
        $('#DiscountScopeTotal').prop('checked', $('#DiscountOnTariff').is(':checked') && $('#DiscountOnBaf').is(':checked'));
        renderServices();
    });
    $('#SbmaEnabled').on('change', updateSummary);
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
    $('#billingTerms').on('change', function () {
        if (!syncingTerms) {
            $(this).data('user-overridden', Boolean(($(this).val() || '').trim()));
        }
        updateTermsOverrideHint();
    });
    $('#AgencyIncentiveSelect, #AgentCommissionSelect').on('change', updateSummary);
    updateSummary();
    if ($customer.val()) $customer.trigger('change');
});
