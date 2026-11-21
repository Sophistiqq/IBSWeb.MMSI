const initializeCosTicketEntry = () => {
    document.querySelectorAll('[data-cancel-service]').forEach(button => button.addEventListener('click', async () => {
        if (button.disabled) return;
        button.disabled = true;
        try {
            const result = await ModernAlert.prompt({
                title: 'Cancel unperformed service?',
                text: 'Use this only when the service did not happen. Its planned row and your reason will be retained.',
                placeholder: 'Reason for cancellation', confirmText: 'Cancel service', cancelText: 'Keep service',
                validator: value => !value?.trim() || value.trim().length > 500 ? 'Enter a reason of up to 500 characters.' : null
            });
            if (!result.isConfirmed) return;
            const body = new FormData();
            body.set('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
            body.set('id', button.dataset.cosId); body.set('entryId', button.dataset.cancelService); body.set('reason', result.value.trim());
            const response = await fetch(button.dataset.url, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Cancellation failed');
            const saved = await response.json();
            if (!saved.success) { await ModernAlert.error(saved.message || 'The service could not be cancelled.'); return; }
            await ModernAlert.success(saved.message);
            window.location.reload();
        } catch {
            await ModernAlert.error('Unable to cancel the service. Refresh the COS and try again.');
        } finally { button.disabled = false; }
    }));
    document.querySelectorAll('[data-void-ticket]').forEach(button => button.addEventListener('click', async () => {
        if (button.disabled) return;
        button.disabled = true;
        try {
            const isFinal = button.dataset.finalized === 'true';
            const dispatchNo = button.dataset.dispatchNumber || '';
            const text = isFinal
                ? 'Voiding this ticket removes it from billing calculations and tags this finalized-period record as Adjusted in the audit history.'
                : 'Voiding this ticket removes it from billing calculations and retains the voided row and your reason.';
            const result = await ModernAlert.promptAdjustment({
                title: dispatchNo ? `Void ticket #${dispatchNo}?` : 'Void ticket?',
                text,
                placeholder: 'Reason for voiding',
                confirmText: 'Void ticket',
                cancelText: 'Keep ticket',
                requireAttachment: isFinal
            });
            if (!result.isConfirmed) return;
            const body = new FormData();
            body.set('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
            body.set('id', button.dataset.voidTicket);
            body.set('customerOrderSlipId', button.dataset.cosId);
            body.set('reason', result.value.reason);
            if (result.value.file) {
                body.set('authorizationFile', result.value.file);
            }
            const response = await fetch(button.dataset.url, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Void failed');
            const saved = await response.json();
            if (!saved.success) { await ModernAlert.error(saved.message || 'The ticket could not be voided.'); return; }
            await ModernAlert.success(saved.message);
            window.location.href = `/MSAP/CustomerOrderSlip/Details/${encodeURIComponent(button.dataset.cosId)}`;
        } catch {
            await ModernAlert.error('Unable to void the ticket. Refresh the COS and try again.');
        } finally { button.disabled = false; }
    }));
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
        if (!message && form.elements.Tons) {
            const tons = Number.parseFloat(form.elements.Tons.value);
            if (!Number.isFinite(tons) || tons <= 0) message = 'Enter the watering tons (greater than 0).';
        }
        if (!message && form.elements.DispatchRate) {
            const rate = Number.parseFloat(form.elements.DispatchRate.value);
            if (!Number.isFinite(rate) || rate < 0) message = 'Enter a valid watering rate.';
        }
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
