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
            title: 'Schedule overlaps found',
            text: '<span class="schedule-conflict-details">' + errorMessage.replace(/^Overlapping plans:\s*/, '') + '</span>', icon: 'warning',
            confirmText: 'Continue', cancelText: 'Keep editing'
        });
        if (!review.isConfirmed) return;
        const acknowledgement = await ModernAlert.confirm({
            title: 'Save despite these overlaps?',
            text: 'I have reviewed the overlapping bookings and want to save these assignments.',
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
            items.forEach(item => {
                const option = new Option(item.text, item.value);
                option.dataset.isIoc = String(item.isIoc);
                terminal.add(option);
            });
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

    const customer = document.getElementById('CustomerId');
    let defaultDiscount = 0;
    let discountRequest;
    async function loadCustomerDiscount() {
        discountRequest?.abort();
        const request = discountRequest = new AbortController();
        defaultDiscount = 0;
        updateSummary();
        if (!customer.value) return;
        try {
            const response = await fetch(form.dataset.discountUrl + '?customerId=' + encodeURIComponent(customer.value), { signal: request.signal });
            if (!response.ok) throw new Error('Unable to load customer discount.');
            const data = await response.json();
            if (request.signal.aborted) return;
            defaultDiscount = Number(data.defaultDiscount) || 0;
            updateSummary();
        } catch (error) {
            if (error.name !== 'AbortError') document.getElementById('summaryDiscount').textContent = 'Applied when saved';
        }
    }
    $(customer).on('change', loadCustomerDiscount);
    $(terminal).on('change', updateSummary);

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

        if (noTugsNotice) {
            noTugsNotice.hidden = tugCards.length > 0;
        }

        document.getElementById('summaryDiscount').textContent = defaultDiscount + '%';
        const iocDefault = terminal.selectedOptions[0]?.dataset.isIoc;
        document.getElementById('summaryIoc').textContent = iocDefault == null ? 'Select terminal' : iocDefault === 'true' ? 'IOC' : 'Non-IOC';
        document.getElementById('summarySbma').textContent = /^subic(?:\s|$)/i.test(port.selectedOptions[0]?.textContent.trim() || '') ? 'Enabled' : 'Disabled';
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
                updateSummary();
                return;
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
        updateSummary();
    });

    $(terminal).on('change', () => {
        updateSummary();
    });

    start.addEventListener('input', () => {
        validateTime();
        updateSummary();
        refreshTugBookings();
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
    loadCustomerDiscount();
    refreshTugBookings();
})();
