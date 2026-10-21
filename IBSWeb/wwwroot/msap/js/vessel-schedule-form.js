(() => {
    const form = document.getElementById('scheduleForm');
    if (!form) return;
    // ModernSelect hides native selects, so browser validation cannot focus them.
    form.noValidate = true;

    function validateForm() {
        const invalidSelect = [...form.querySelectorAll('select.js-modern-select')].find(select => !select.checkValidity());
        if (invalidSelect) {
            const label = invalidSelect.getAttribute('aria-label') || invalidSelect.labels?.[0]?.textContent.trim() || 'tugboat';
            ModernAlert.error('Please choose a value for ' + label.replace('*', '').trim() + '.', 'Selection required');
            return false;
        }
        return ModernFormValidator.validate('scheduleForm');
    }

    const saveErrors = [...document.querySelectorAll('#scheduleSaveErrors [data-save-error]')];
    if (saveErrors.length) {
        // ModernAlert accepts HTML; escape server messages containing reference names.
        const errorMessage = saveErrors.map(error => $('<div>').text(error.textContent).html()).join('<br><br>');
        if (saveErrors.some(error => error.dataset.conflict === 'true')) {
            acknowledgeConflicts(errorMessage);
        } else {
            ModernAlert.error(errorMessage, 'Schedule could not be saved');
        }
    }

    async function acknowledgeConflicts(errorMessage) {
        const review = await ModernAlert.confirm({
            title: 'Review schedule conflicts', text: errorMessage, icon: 'warning',
            confirmText: 'Continue to final confirmation', cancelText: 'Keep editing'
        });
        if (!review.isConfirmed) return;
        const acknowledgement = await ModernAlert.confirm({
            title: 'Save this overlapping plan?',
            text: 'I acknowledge the overlapping vessel, terminal or tugboat bookings. Save this plan with these assignments.',
            icon: 'warning', confirmText: 'Acknowledge and save', cancelText: 'Keep editing'
        });
        if (!acknowledgement.isConfirmed || !validateForm()) return;
        document.getElementById('AllowConflicts').value = 'true';
        ModernAlert.showLoading('Saving acknowledged schedule…');
        form.submit();
    }

    const port = document.getElementById('PortId');
    const terminal = document.getElementById('TerminalId');
    const message = document.getElementById('terminalMessage');
    let terminalRequest;

    $(port).on('change', async () => {
        terminalRequest?.abort();
        const request = terminalRequest = new AbortController();
        terminal.replaceChildren(new Option('Select terminal', ''));
        $(terminal).trigger('change');
        if (message) message.textContent = '';
        updateSummary();
        if (!port.value) return;
        if (message) message.textContent = 'Loading terminals…';
        try {
            const response = await fetch(form.dataset.terminalsUrl + '?portId=' + encodeURIComponent(port.value), { signal: request.signal });
            if (!response.ok) throw new Error('Unable to load terminals.');
            const items = await response.json();
            if (request.signal.aborted) return;
            items.forEach(item => terminal.add(new Option(item.text, item.value)));
            window.refreshModernSelect($(terminal));
            $(terminal).trigger('change');
            if (message) message.textContent = items.length ? '' : 'No terminals registered for this port.';
            updateSummary();
        } catch (error) {
            if (error.name !== 'AbortError' && message) {
                message.textContent = 'Could not load terminals. Select the port again to retry.';
            }
        }
    });

    $(terminal).on('change', () => {
        updateSummary();
    });

    const customer = document.getElementById('CustomerId');
    const discountInput = document.getElementById('CosDiscountPercent');
    const skipDiscount = document.getElementById('SkipCosDiscount');

    let discountRequest;
    $(customer).on('change', async () => {
        discountRequest?.abort();
        const request = discountRequest = new AbortController();
        const customerId = customer.value;
        discountInput.value = '';
        updateSummary();
        if (!customerId || !form.dataset.discountUrl) return;
        try {
            const response = await fetch(form.dataset.discountUrl + '?customerId=' + encodeURIComponent(customerId), { signal: request.signal });
            if (!response.ok) throw new Error('Unable to load customer discount.');
            const data = await response.json();
            if (request.signal.aborted || discountInput.value !== '') return;
            discountInput.value = data.defaultDiscount;
            updateSummary();
        } catch (error) {
            if (error.name !== 'AbortError') {
                ModernAlert.error('Could not load the customer discount. Check the discount before saving.', 'Customer discount unavailable');
            }
        }
    });

    function updateDiscountState() {
        discountInput.readOnly = skipDiscount.checked;
        discountInput.classList.toggle('schedule-input-disabled', skipDiscount.checked);
        updateSummary();
    }
    skipDiscount.addEventListener('change', updateDiscountState);
    discountInput.addEventListener('input', updateSummary);

    const start = document.getElementById('PlannedStart');
    const end = document.getElementById('PlannedEnd');
    const servicesContainer = document.getElementById('tugServicesContainer');
    const noTugsNotice = document.getElementById('noTugsAssignedNotice');
    const addTugboatBtn = document.getElementById('addTugboatBtn');
    const tugboatCardTemplate = document.getElementById('tugboatCardTemplate');
    const serviceRowTemplate = document.getElementById('serviceRowTemplate');
    let bookingRequest;
    const cachedBookings = new Map();
    let bookingState = 'unchecked';

    function formatPhilippineDate(isoStr) {
        if (!isoStr) return '';
        const d = new Date(isoStr);
        if (isNaN(d.getTime())) return '';
        return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
    }

    function formatPhilippineTime(isoStr) {
        if (!isoStr) return '';
        const d = new Date(isoStr);
        if (isNaN(d.getTime())) return '';
        return d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
    }

    function updateSummary() {
        // Location
        const summaryLocation = document.getElementById('summaryLocation');
        if (summaryLocation) {
            const portText = port.options[port.selectedIndex]?.text || '';
            const termText = terminal.options[terminal.selectedIndex]?.text || '';
            if (port.value && terminal.value) {
                summaryLocation.textContent = portText + ' / ' + termText;
            } else if (port.value) {
                summaryLocation.textContent = portText;
            } else {
                summaryLocation.textContent = 'Select port / terminal';
            }
        }

        // Planned Window
        const summaryDate = document.getElementById('summaryDate');
        const summaryTime = document.getElementById('summaryTime');
        if (summaryDate && start.value) {
            summaryDate.textContent = formatPhilippineDate(start.value)
                + (end.value && end.value.slice(0, 10) !== start.value.slice(0, 10) ? ' – ' + formatPhilippineDate(end.value) : '');
        }
        if (summaryTime && start.value && end.value) {
            summaryTime.textContent = formatPhilippineTime(start.value) + ' – ' + formatPhilippineTime(end.value) + ' · Philippine time';
        }

        // Tugboats count
        const tugCards = servicesContainer ? [...servicesContainer.querySelectorAll('.schedule-tug-card')] : [];
        const summaryTugCount = document.getElementById('summaryTugCount');
        if (summaryTugCount) {
            summaryTugCount.textContent = new Set(tugCards.map(card => card.querySelector('.js-tug-select')?.value).filter(Boolean)).size;
        }

        // Services count
        const allServiceRows = servicesContainer ? [...servicesContainer.querySelectorAll('[data-service-row]')] : [];
        const summaryServiceCount = document.getElementById('summaryServiceCount');
        if (summaryServiceCount) {
            summaryServiceCount.textContent = allServiceRows.length;
        }

        // Customer discount
        const summaryDiscount = document.getElementById('summaryDiscount');
        if (summaryDiscount) {
            if (skipDiscount?.checked) {
                summaryDiscount.textContent = '0%';
            } else if (discountInput?.value) {
                const val = parseFloat(discountInput.value);
                summaryDiscount.textContent = isNaN(val) ? '0%' : val + '%';
            } else {
                summaryDiscount.textContent = '0%';
            }
        }

        const selectedRows = allServiceRows.filter(row => row.querySelector('.js-service-select')?.value);
        let manualCount = 0;
        function rateSummary(selector) {
            if (!selectedRows.length) return 'No services selected';
            let manual = 0;
            let found = 0;
            selectedRows.forEach(row => {
                const input = row.querySelector(selector);
                if (input.value !== '') manual++;
                else if (input.dataset.lookup === 'found') found++;
            });
            manualCount += manual;
            const unchecked = selectedRows.length - manual - found;
            return `${found} table estimate · ${manual} manual` + (unchecked ? ` · ${unchecked} unresolved` : '');
        }
        document.getElementById('summaryTariffRates').textContent = rateSummary('.js-tariff-rate');
        document.getElementById('summaryBafRates').textContent = rateSummary('.js-baf-rate');
        document.getElementById('summaryRecurringBadge').textContent = manualCount ? 'Non-recurring' : 'Recurring';
        const summaryNotice = document.getElementById('summaryRatesNotice');
        summaryNotice.textContent = manualCount
            ? 'Manual rates need review. Table estimates use the planned date; final rates use actual service start.'
            : 'Table estimates use the planned date. Final rates use actual service start.';
        summaryNotice.classList.toggle('schedule-rate-warning', manualCount > 0);

        if (noTugsNotice) {
            noTugsNotice.hidden = tugCards.length > 0;
        }
    }

    function updateTugOverlap(card) {
        const tugId = card.dataset.tugId;
        const overlapEl = card.querySelector('.js-tug-overlap');
        if (!overlapEl) return;
        if (!tugId || tugId === '__TUG_ID__') {
            overlapEl.textContent = 'Select a tugboat';
            overlapEl.classList.remove('schedule-tug-overlap-warning');
            return;
        }
        if (bookingState !== 'ready') {
            overlapEl.textContent = bookingState === 'loading' ? 'Checking planned overlaps…' : 'Planned overlaps not checked';
            overlapEl.classList.remove('schedule-tug-overlap-warning');
            return;
        }
        const bookings = cachedBookings.get(String(tugId)) || [];
        if (bookings.length > 0) {
            const b = bookings[0];
            overlapEl.textContent = 'Overlapping: ' + b.vessel + ' (' + b.start + ')';
            overlapEl.classList.add('schedule-tug-overlap-warning');
        } else {
            overlapEl.textContent = 'No planned overlap';
            overlapEl.classList.remove('schedule-tug-overlap-warning');
        }
    }

    async function refreshTugBookings() {
        bookingRequest?.abort();
        bookingState = 'unchecked';
        if (!start.value || !end.value || end.value <= start.value) {
            cachedBookings.clear();
            if (servicesContainer) {
                [...servicesContainer.querySelectorAll('.schedule-tug-card')].forEach(updateTugOverlap);
            }
            return;
        }
        bookingState = 'loading';
        servicesContainer.querySelectorAll('.schedule-tug-card').forEach(updateTugOverlap);
        const request = bookingRequest = new AbortController();
        const query = new URLSearchParams({
            start: start.value,
            end: end.value,
            scheduleId: document.getElementById('VesselScheduleId').value || '0'
        });
        try {
            const response = await fetch(form.dataset.bookingsUrl + '?' + query, { signal: request.signal });
            if (!response.ok) throw new Error('Unable to check bookings.');
            const data = await response.json();
            if (request.signal.aborted) return;
            bookingState = 'ready';
            cachedBookings.clear();
            data.bookings.forEach(booking => {
                const id = String(booking.tugboatId);
                if (!cachedBookings.has(id)) cachedBookings.set(id, []);
                cachedBookings.get(id).push(booking);
            });
            if (servicesContainer) {
                [...servicesContainer.querySelectorAll('.schedule-tug-card')].forEach(updateTugOverlap);
            }
        } catch (error) {
            if (error.name === 'AbortError') return;
            bookingState = 'unchecked';
            servicesContainer.querySelectorAll('.schedule-tug-card').forEach(updateTugOverlap);
        }
    }

    function removeAssignment(element) {
        $(element).find('.js-modern-select').each(function () {
            $(this).data('modern-select-dropdown')?.remove();
        });
        element?.remove();
        updateSummary();
    }

    function updateTugBadge(card, value, selectedOption) {
        const badge = card.querySelector('.js-tug-badge');
        if (!badge) return;
        if (!value) {
            badge.textContent = 'Unassigned';
            badge.classList.add('schedule-badge-muted');
        } else {
            const isCompany = selectedOption?.dataset?.companyOwned === 'true';
            badge.textContent = isCompany ? 'Company-owned' : 'Subcontractor';
            badge.classList.remove('schedule-badge-muted');
        }
    }

    function getScheduleContext() {
        return {
            customerId: parseInt(customer?.value || '0', 10),
            terminalId: parseInt(terminal?.value || '0', 10),
            date: start?.value || ''
        };
    }

    function updateRateProvenance(row) {
        for (const kind of ['tariff', 'baf']) {
            const input = row.querySelector(`.js-${kind}-rate`);
            row.querySelector(`.js-${kind}-provenance`).textContent = input.value !== ''
                ? 'Manual rate'
                : ({ found: 'Table estimate · planned date', missing: 'No table match', loading: 'Checking table…', error: 'Lookup unavailable' }[input.dataset.lookup] || 'Rate not checked');
        }
    }

    async function fetchTariffAndBaf(row, serviceId) {
        row.rateRequest?.abort();
        const request = row.rateRequest = new AbortController();
        const inputs = [row.querySelector('.js-tariff-rate'), row.querySelector('.js-baf-rate')];
        const ctx = getScheduleContext();
        const canLookup = form.dataset.rateUrl && ctx.customerId && ctx.terminalId && ctx.date && serviceId;
        inputs.forEach(input => {
            input.dataset.lookup = canLookup ? 'loading' : 'unchecked';
            input.placeholder = 'From table';
            input.readOnly = true;
        });
        updateRateProvenance(row);
        updateSummary();
        if (!canLookup) return;
        const params = new URLSearchParams({ customerId: ctx.customerId, terminalId: ctx.terminalId, serviceId, date: ctx.date });
        try {
            const response = await fetch(form.dataset.rateUrl + '?' + params, { signal: request.signal });
            if (!response.ok) throw new Error('Unable to check rates.');
            const data = await response.json();
            if (request.signal.aborted || !row.isConnected) return;
            inputs.forEach((input, index) => {
                const rate = index === 0 ? data.tariffRate : data.bafRate;
                input.dataset.lookup = data.exists && rate != null ? 'found' : 'missing';
                input.readOnly = input.dataset.lookup === 'found';
                if (input.readOnly) input.value = '';
                // Estimates stay out of Manual* fields so saving cannot turn a table rate into a manual override.
                input.placeholder = input.dataset.lookup === 'found' ? Number(rate).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 4 }) : 'Enter manual rate';
            });
        } catch (error) {
            if (error.name === 'AbortError') return;
            inputs.forEach(input => input.dataset.lookup = 'error');
        }
        updateRateProvenance(row);
        updateSummary();
    }

    function refreshAllRates() {
        if (!servicesContainer) return;
        servicesContainer.querySelectorAll('[data-service-row]').forEach(row => {
            const svcSelect = row.querySelector('.js-service-select');
            fetchTariffAndBaf(row, parseInt(svcSelect?.value || '0', 10));
        });
    }

    function addServiceRow(tugId, targetContainer) {
        if (!serviceRowTemplate) return;
        const uniqueKey = 'srv_' + tugId + '_' + Date.now() + '_' + Math.random().toString(36).substring(2, 7);
        const html = serviceRowTemplate.innerHTML.replace(/__KEY__/g, uniqueKey).replace(/__TUG_ID__/g, tugId);
        const temp = document.createElement('div');
        temp.innerHTML = html.trim();
        const newRow = temp.firstElementChild;
        targetContainer.appendChild(newRow);
        ModernSelect.init($(newRow).find('.js-modern-select'));
        updateSummary();
        // No auto-fetch on new row: service not yet selected
    }

    if (addTugboatBtn && tugboatCardTemplate && servicesContainer) {
        addTugboatBtn.addEventListener('click', () => {
            const tempId = 'tug_temp_' + Date.now();
            const html = tugboatCardTemplate.innerHTML.replace(/__TUG_ID__/g, tempId);
            const temp = document.createElement('div');
            temp.innerHTML = html.trim();
            const newCard = temp.firstElementChild;
            servicesContainer.appendChild(newCard);
            ModernSelect.init($(newCard).find('.js-modern-select'));
            const rowsContainer = newCard.querySelector('.js-service-rows');
            if (rowsContainer) {
                addServiceRow(tempId, rowsContainer);
            }
            updateSummary();
        });
    }

    if (servicesContainer) {
        ModernSelect.init($(servicesContainer).find('.js-modern-select'));
    }

    if (servicesContainer) {
        $(servicesContainer).on('change', e => {
            const tugSelect = e.target.closest('.js-tug-select');
            if (tugSelect) {
                const card = tugSelect.closest('.schedule-tug-card');
                const newTugId = tugSelect.value;
                card.dataset.tugId = newTugId;
                card.id = 'tugCard_' + newTugId;
                card.querySelectorAll('.js-tug-id').forEach(hidden => hidden.value = newTugId);
                const addSrvBtn = card.querySelector('.js-add-service-btn');
                if (addSrvBtn) addSrvBtn.dataset.tugId = newTugId;

                const selectedOption = tugSelect.options[tugSelect.selectedIndex];
                updateTugBadge(card, newTugId, selectedOption);
                updateTugOverlap(card);
                updateSummary();
                return;
            }

            const svcSelect = e.target.closest('.js-service-select');
            if (svcSelect) {
                const row = svcSelect.closest('[data-service-row]');
                if (row) fetchTariffAndBaf(row, parseInt(svcSelect.value || '0', 10));
                updateSummary();
                return;
            }

            const bafSelect = e.target.closest('.js-baf-method');
            if (bafSelect) {
                const notice = bafSelect.closest('.col-baf-method')?.querySelector('.schedule-pending-notice');
                if (notice) {
                    notice.hidden = !(bafSelect.value === 'Lumpsum' || bafSelect.value === 'Industry');
                }
                updateSummary();
                return;
            }
        });

        servicesContainer.addEventListener('input', e => {
            if (e.target.matches('.js-tariff-rate, .js-baf-rate')) {
                updateRateProvenance(e.target.closest('[data-service-row]'));
                updateSummary();
            }
        });

        servicesContainer.addEventListener('click', e => {
            const addSrvBtn = e.target.closest('.js-add-service-btn');
            if (addSrvBtn) {
                e.preventDefault();
                const card = addSrvBtn.closest('.schedule-tug-card');
                const tugId = card?.dataset.tugId || '0';
                const rowsContainer = card?.querySelector('.js-service-rows');
                if (rowsContainer) {
                    addServiceRow(tugId, rowsContainer);
                }
                return;
            }

            const removeTugBtn = e.target.closest('.js-remove-tug-btn');
            if (removeTugBtn) {
                e.preventDefault();
                const card = removeTugBtn.closest('.schedule-tug-card');
                removeAssignment(card);
                return;
            }

            const removeSrvBtn = e.target.closest('.js-remove-service-btn');
            if (removeSrvBtn) {
                e.preventDefault();
                const row = removeSrvBtn.closest('[data-service-row]');
                removeAssignment(row);
                return;
            }
        });
    }

    function validateTime() {
        end.setCustomValidity(start.value && end.value && end.value <= start.value ? 'Planned end must be after planned start.' : '');
    }

    $(customer).on('change', () => {
        refreshAllRates();
    });

    $(terminal).on('change', () => {
        updateSummary();
        refreshAllRates();
    });

    start.addEventListener('input', () => {
        validateTime();
        updateSummary();
        refreshTugBookings();
        refreshAllRates();
    });

    end.addEventListener('input', () => {
        validateTime();
        updateSummary();
        refreshTugBookings();
    });

    form.addEventListener('submit', event => {
        validateTime();
        if (!validateForm()) {
            event.preventDefault();
            return;
        }

        // Validate that all tugboat cards have a selected tugboat
        const tugCards = servicesContainer ? [...servicesContainer.querySelectorAll('.schedule-tug-card')] : [];
        const selectedTugs = new Set();
        for (const card of tugCards) {
            const select = card.querySelector('.js-tug-select');
            if (!select || !select.value) {
                event.preventDefault();
                ModernAlert.error('Please select a tugboat for all assigned tugboat cards.', 'Tugboat selection required');
                select?.focus();
                return;
            }
            if (selectedTugs.has(select.value)) {
                event.preventDefault();
                ModernAlert.error('Each tugboat can only be assigned once. Add its services to the existing tugboat card.', 'Duplicate tugboat');
                return;
            }
            selectedTugs.add(select.value);
        }
    });

    // Initialize summary metrics and bookings check
    updateDiscountState();
    refreshTugBookings();
    refreshAllRates();
})();
