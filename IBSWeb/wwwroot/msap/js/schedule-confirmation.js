(() => {
    const links = document.querySelectorAll('[data-schedule-confirm="true"]');
    if (links.length && new URLSearchParams(window.location.search).get('review') === 'true') {
        window.location.replace(links[0].href);
        return;
    }
    const form = document.getElementById('confirmSchedule');
    let submitting = false;
    const escape = text => $('<div>').text(text).html();
    form?.addEventListener('submit', async event => {
        event.preventDefault();
        if (submitting) return;
        submitting = true;
        const reapproval = form.dataset.reapproval === 'true';
        const button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        try {
            const confirmation = await ModernAlert.confirm({
                title: reapproval ? 'Approve this revision?' : 'Approve this schedule?',
                text: reapproval ? 'Approve the reviewed revision and retain the existing COS.' : 'Approve the reviewed booking and create or retain its linked COS and any urgent tickets.',
                confirmText: reapproval ? 'Reapprove schedule' : 'Approve schedule', cancelText: 'Keep reviewing'
            });
            if (!confirmation.isConfirmed) return;
            const body = new URLSearchParams(new FormData(form));
            body.set('allowConflicts', 'false');
            const submit = async () => {
                ModernAlert.showLoading(reapproval ? 'Reapproving schedule…' : 'Approving schedule…');
                const response = await fetch(form.action, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!response.ok || response.redirected) throw new Error('Unable to approve this schedule. Refresh and check your access.');
                return response.json();
            };
            let result = await submit();
            if (result.requiresConflictAcknowledgement) {
                const conflicts = await ModernAlert.confirm({title: 'Schedule overlaps found', text: '<span class="schedule-conflict-details">' + escape(result.message).replace(/^Overlapping plans:\s*/, '') + '</span>', icon: 'warning', confirmText: 'Continue', cancelText: 'Keep reviewing'});
                if (!conflicts.isConfirmed) return;
                const acknowledgement = await ModernAlert.confirm({
                    title: 'Approve this overlapping booking?', text: 'I acknowledge the overlapping bookings and want to approve this schedule.',
                    icon: 'warning', confirmText: 'Acknowledge and approve', cancelText: 'Keep reviewing'
                });
                if (!acknowledgement.isConfirmed) return;
                body.set('allowConflicts', 'true');
                result = await submit();
            }
            if (result.success && result.redirectUrl) window.location.href = result.redirectUrl;
            else ModernAlert.error(escape(result.message || 'Schedule approval failed. Refresh the review if the booking changed.'));
        } catch (error) {
            ModernAlert.error(escape(error.message));
        } finally {
            submitting = false;
            button.disabled = false;
        }
    });

    const review = document.getElementById('scheduleReview');
    if (!review) return;
    review.querySelectorAll('[data-review-service]').forEach(async row => {
        const cells = [...row.querySelectorAll('[data-review-rate]')];
        if (!cells.length) return;
        const params = new URLSearchParams({ customerId: review.dataset.customerId, terminalId: review.dataset.terminalId, serviceId: row.dataset.reviewService, date: review.dataset.plannedStart });
        try {
            const response = await fetch(review.dataset.rateUrl + '?' + params);
            if (!response.ok || response.redirected) throw new Error('Rate lookup unavailable');
            const data = await response.json();
            cells.forEach(cell => {
                const rate = cell.dataset.reviewRate === 'tariff' ? data.tariffRate : data.bafRate;
                const found = data.exists && rate != null;
                const manual = cell.dataset.manual === 'true';
                const formattedRate = found ? Number(rate).toLocaleString('en-PH', {minimumFractionDigits: 2, maximumFractionDigits: 4}) : '';
                cell.parentElement.classList.toggle('schedule-review-attention', manual);
                if (!manual) cell.textContent = found ? formattedRate : 'No table match';
                cell.parentElement.querySelector('[data-rate-source]').textContent = manual
                    ? found ? 'Manual override · table estimate: ' + formattedRate : 'Manual rate · no table match'
                    : found ? 'Table estimate · planned date' : 'Rate needs review';
            });
        } catch (_) {
            cells.forEach(cell => {
                const manual = cell.dataset.manual === 'true';
                cell.parentElement.classList.toggle('schedule-review-attention', manual);
                if (!manual) cell.textContent = 'Unavailable';
                cell.parentElement.querySelector('[data-rate-source]').textContent = manual ? 'Saved manual rate · table lookup unavailable' : 'Could not check table rate';
            });
        }
    });
})();
