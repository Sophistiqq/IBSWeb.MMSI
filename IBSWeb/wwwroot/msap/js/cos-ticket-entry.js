const initializeCosTicketEntry = () => {
    const recalculate = document.querySelector('[data-recalculate-ticket]');
    recalculate?.addEventListener('click', async () => {
        if (recalculate.disabled) return;
        recalculate.disabled = true;
        try {
            const confirmation = await ModernAlert.confirm({
                title: 'Recalculate ticket charges?',
                text: 'Use the recorded service times, approved COS terms and applicable service-date rates.',
                confirmText: 'Recalculate charges',
                cancelText: 'Keep reviewing'
            });
            if (!confirmation.isConfirmed) return;
            ModernAlert.showLoading('Recalculating charges…');
            const body = new FormData(recalculate.form);
            body.set('id', recalculate.dataset.recalculateTicket);
            body.set('CustomerOrderSlipId', recalculate.dataset.cosId);
            const response = await fetch(recalculate.dataset.url, {
                method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok || response.redirected) throw new Error('Recalculation failed');
            const result = await response.json();
            if (!result.success || !result.redirectUrl) {
                await ModernAlert.error(result.message || 'Charges could not be recalculated.');
                return;
            }
            await ModernAlert.success(result.message);
            window.location.href = result.redirectUrl;
        } catch {
            ModernAlert.error('Unable to recalculate charges. Check your connection and access, then try again.');
        } finally {
            recalculate.disabled = false;
        }
    });
    const form = document.getElementById('cosActualTicketForm');
    if (!form) return;

    const actual = document.getElementById('cosActualDuration');
    const billable = document.getElementById('cosBillableDuration');
    const times = () => {
        const startDate = form.elements.DateLeft.value;
        const endDate = form.elements.DateArrived.value;
        const startTime = form.elements.TimeLeft.value;
        const endTime = form.elements.TimeArrived.value;
        if (!startDate || !endDate || !startTime || !endTime) return null;
        return {
            start: Date.parse(`${startDate}T${startTime}+08:00`),
            end: Date.parse(`${endDate}T${endTime}+08:00`)
        };
    };
    const timeError = value => {
        if (!value) return 'Enter complete actual start and end dates/times.';
        if (!Number.isFinite(value.start) || !Number.isFinite(value.end)) return 'Enter valid actual start and end dates/times.';
        if (value.end <= value.start) return 'Actual end must be after actual start.';
        if (value.end > Date.now()) return 'Actual end is in the future. Enter a completed service time in Philippine time (UTC+8).';
        return null;
    };
    const preview = () => {
        const value = times();
        const error = timeError(value);
        if (error) {
            actual.textContent = error;
            billable.textContent = 'Enter valid actual times';
            return;
        }
        const minutes = (value.end - value.start) / 60000;
        actual.textContent = `${minutes.toFixed(0)} min · ${(minutes / 60).toFixed(2)} hours`;
        billable.textContent = `${Math.max(minutes / 60, 1).toFixed(2)} hours`;
    };
    ['DateLeft', 'TimeLeft', 'DateArrived', 'TimeArrived'].forEach(name => form.elements[name].addEventListener('input', preview));
    form.noValidate = true;
    let submitting = false;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (submitting) return;
        let message = timeError(times());
        if (!message && !form.elements.TugMasterId.value) message = 'Select the captain / tug master for this service.';
        preview();
        if (message || !form.reportValidity()) {
            if (message) ModernAlert.error(message);
            return;
        }
        submitting = true;
        const button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        const editing = Boolean(form.elements.DispatchTicketId?.value);
        try {
            const confirmation = await ModernAlert.confirm({
                title: editing ? 'Save ticket changes?' : 'Save actual ticket?',
                text: 'Save the ticket details and supporting files for this service.',
                icon: 'question',
                confirmText: editing ? 'Save changes' : 'Save ticket',
                cancelText: 'Keep editing'
            });
            if (!confirmation.isConfirmed) return;
            ModernAlert.showLoading(editing ? 'Saving ticket changes…' : 'Saving actual ticket…');
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok || response.redirected) throw new Error('Save failed');
            const result = await response.json();
            if (!result.success || !result.redirectUrl) {
                await ModernAlert.error(result.message || 'Unable to save the ticket. Please try again.');
                return;
            }
            await ModernAlert.success(result.message || 'Ticket saved successfully.');
            window.location.href = result.redirectUrl;
        } catch {
            ModernAlert.error('Unable to save the ticket. Check your connection and access, then try again.');
        } finally {
            submitting = false;
            button.disabled = false;
        }
    }, true);
    preview();
};

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeCosTicketEntry);
} else {
    initializeCosTicketEntry();
}
