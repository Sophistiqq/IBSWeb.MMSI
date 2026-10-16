(() => {
    const form = document.getElementById('scheduleForm');
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
        if (!acknowledgement.isConfirmed || !ModernFormValidator.validate('scheduleForm')) return;
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
        message.textContent = '';
        if (!port.value) return;
        message.textContent = 'Loading terminals…';
        try {
            const response = await fetch(form.dataset.terminalsUrl + '?portId=' + encodeURIComponent(port.value), { signal: request.signal });
            if (!response.ok) throw new Error('Unable to load terminals.');
            const items = await response.json();
            if (request.signal.aborted) return;
            items.forEach(item => terminal.add(new Option(item.text, item.value)));
            window.refreshModernSelect($(terminal));
            $(terminal).trigger('change');
            message.textContent = items.length ? '' : 'No terminals registered for this port.';
        } catch (error) {
            if (error.name !== 'AbortError') message.textContent = 'Could not load terminals. Select the port again to retry.';
        }
    });
    const start = document.getElementById('PlannedStart');
    const end = document.getElementById('PlannedEnd');
    const tugOptions = [...form.querySelectorAll('[data-tug-option]')];
    const tugSearch = document.getElementById('tugPickerSearch');
    const tugScope = document.getElementById('tugPickerScope');
    const selectedSummary = document.getElementById('selectedTugSummary');
    const bookingMessage = document.getElementById('tugBookingMessage');
    let bookingRequest;
    function filterTugOptions() {
        const search = tugSearch.value.trim().toLowerCase();
        const scope = tugScope.value;
        const selected = tugOptions.filter(row => row.querySelector('input').checked);
        const unbooked = tugOptions.filter(row => row.dataset.state === 'clear');
        const attention = tugOptions.filter(row => row.dataset.state === 'conflict');
        const counts = { all: tugOptions.length, selected: selected.length, unbooked: unbooked.length, attention: attention.length };
        const labels = { all: 'All', selected: 'Selected', unbooked: 'Unbooked', attention: 'Needs attention' };
        [...tugScope.options].forEach(option => option.textContent = labels[option.value] + ' (' + counts[option.value] + ')');
        tugOptions.forEach(row => {
            const matchesScope = scope === 'all' || (scope === 'selected' && row.querySelector('input').checked)
                || (scope === 'unbooked' && row.dataset.state === 'clear') || (scope === 'attention' && row.dataset.state === 'conflict');
            row.hidden = !matchesScope || !row.dataset.name.toLowerCase().includes(search);
        });
        document.getElementById('noPickerTugs').hidden = tugOptions.some(row => !row.hidden);
    }
    function showSelectedTugs() {
        tugOptions.forEach(row => {
            const count = Number(row.dataset.bookingCount || 0);
            row.querySelector('.schedule-tug-booking-state').textContent = row.dataset.state === 'unknown' ? 'Not checked'
                : count ? (row.querySelector('input').checked ? 'Assignment overlaps ' : 'Booked · ') + count + ' booking(s)'
                : 'No bookings in this window';
        });
        selectedSummary.replaceChildren();
        const selected = tugOptions.filter(row => row.querySelector('input').checked);
        document.getElementById('assignedCount').textContent = selected.length;
        selectedSummary.hidden = selected.length === 0;
        selected.forEach(row => {
            const button = document.createElement('button');
            button.type = 'button';
            button.dataset.state = row.dataset.state;
            button.textContent = row.dataset.name + (row.dataset.state === 'conflict' ? ' · Overlap' : '') + ' ×';
            button.setAttribute('aria-label', 'Remove ' + row.dataset.name + ' from assignments');
            button.addEventListener('click', () => {
                const checkbox = row.querySelector('input');
                checkbox.checked = false;
                checkbox.dispatchEvent(new Event('change', { bubbles: true }));
            });
            selectedSummary.append(button);
        });
        filterTugOptions();
    }
    function resetBookingStates() {
        tugOptions.forEach(row => {
            row.dataset.state = 'unknown';
            row.dataset.bookingCount = '0';
            row.querySelector('.schedule-tug-booking-state').textContent = 'Not checked';
            const details = row.querySelector('details');
            details.hidden = true;
            details.open = false;
            details.querySelector('div').replaceChildren();
        });
        showSelectedTugs();
    }
    async function refreshTugBookings() {
        bookingRequest?.abort();
        resetBookingStates();
        if (!start.value || !end.value || end.value <= start.value) {
            bookingMessage.textContent = 'Select a valid planned start and end to check bookings.';
            return;
        }
        const request = bookingRequest = new AbortController();
        bookingMessage.textContent = 'Checking bookings for the planned window…';
        const query = new URLSearchParams({ start: start.value, end: end.value, scheduleId: document.getElementById('VesselScheduleId').value || '0' });
        try {
            const response = await fetch(form.dataset.bookingsUrl + '?' + query, { signal: request.signal });
            if (!response.ok) throw new Error('Unable to check bookings.');
            const data = await response.json();
            if (request.signal.aborted) return;
            const bookingsByTug = new Map();
            data.bookings.forEach(booking => {
                const id = String(booking.tugboatId);
                if (!bookingsByTug.has(id)) bookingsByTug.set(id, []);
                bookingsByTug.get(id).push(booking);
            });
            tugOptions.forEach(row => {
                const bookings = bookingsByTug.get(row.querySelector('input').value) || [];
                row.dataset.state = bookings.length ? 'conflict' : 'clear';
                row.dataset.bookingCount = String(bookings.length);
                const details = row.querySelector('details');
                details.hidden = bookings.length === 0;
                details.querySelector('summary').textContent = 'View bookings in this window (' + bookings.length + ')';
                bookings.forEach(booking => {
                    const link = document.createElement('a');
                    link.href = form.dataset.detailsUrl + '?id=' + encodeURIComponent(booking.scheduleId);
                    link.target = '_blank';
                    link.rel = 'noopener';
                    link.textContent = '#' + booking.scheduleId + ' · ' + booking.vessel + ' · ' + booking.start + ' → ' + booking.end + ' · ' + booking.status;
                    link.setAttribute('aria-label', link.textContent + ' (opens in a new tab)');
                    details.querySelector('div').append(link);
                });
            });
            bookingMessage.textContent = 'Bookings checked for the planned window. Cancelled plans and this schedule are excluded.';
            showSelectedTugs();
        } catch (error) {
            if (error.name !== 'AbortError') bookingMessage.textContent = 'Could not check bookings. Change the dates or use Refresh bookings. Save still checks conflicts.';
        }
    }
    tugSearch.addEventListener('input', filterTugOptions);
    $(tugScope).on('change', filterTugOptions);
    form.addEventListener('change', event => {
        if (event.target.name === 'SelectedTugboatIds') showSelectedTugs();
    });
    start.addEventListener('input', refreshTugBookings);
    end.addEventListener('input', refreshTugBookings);
    document.getElementById('retryTugBookings').addEventListener('click', refreshTugBookings);
    refreshTugBookings();
    function validateTime() {
        end.setCustomValidity(start.value && end.value && end.value <= start.value ? 'Planned end must be after planned start.' : '');
    }
    start.addEventListener('input', validateTime);
    end.addEventListener('input', validateTime);
    form.addEventListener('submit', event => {
        validateTime();
        if (!ModernFormValidator.validate('scheduleForm')) event.preventDefault();
    });
})();
