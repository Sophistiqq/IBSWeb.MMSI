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
            for (let attempt = 0; attempt < 3 && !result.success; attempt++) {
                if (result.rateAcknowledgement) {
                    const manual = await ModernAlert.confirm({
                        title: 'Manual rates are not published', text: escape(result.message),
                        icon: 'warning', confirmText: 'Approve using manual rates', cancelText: 'Keep reviewing'
                    });
                    if (!manual.isConfirmed) return;
                    body.set('rateAcknowledgement', result.rateAcknowledgement);
                    result = await submit();
                    continue;
                }
                if (!result.requiresConflictAcknowledgement) break;
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
                const kind = cell.dataset.reviewRate;
                const rate = kind === 'tariff' ? data.tariffRate : data.bafRate;
                const found = data.exists && rate != null;
                const manual = cell.dataset.manual === 'true';
                const formattedRate = found ? Number(rate).toLocaleString('en-PH', {minimumFractionDigits: 2, maximumFractionDigits: 4}) : '';
                const cellVal = Number((cell.textContent || '').replace(/,/g, '').trim());
                const isZero = manual && cellVal === 0;
                const isZeroBaf = kind === 'baf' && isZero;
                const isZeroTariff = kind === 'tariff' && isZero;

                cell.parentElement.classList.toggle('schedule-review-attention', manual && !isZeroBaf);
                cell.parentElement.classList.toggle('schedule-review-override', manual && found);
                if (!manual) cell.textContent = found ? formattedRate : 'No table match';
                const sourceEl = cell.parentElement.querySelector('[data-rate-source]');
                sourceEl.textContent = manual
                    ? isZeroBaf
                        ? (found ? 'Zero BAF · table estimate: ' + formattedRate : 'Zero BAF')
                        : isZeroTariff
                            ? (found ? 'Zero tariff · table estimate: ' + formattedRate : 'Zero tariff')
                            : (found ? 'Manual override · table estimate: ' + formattedRate : 'Manual rate · no table match')
                    : found ? 'Table estimate · planned date' : 'Rate pending · no table match';

                if (manual && found && review.dataset.adoptUrl && row.dataset.entryId) {
                    let adoptBtn = cell.parentElement.querySelector('.schedule-adopt-rate');
                    if (!adoptBtn) {
                        adoptBtn = document.createElement('button');
                        adoptBtn.type = 'button';
                        adoptBtn.className = 'schedule-adopt-rate';
                        adoptBtn.textContent = 'Adopt table rate';
                        adoptBtn.title = `Adopt active table rate of ₱${formattedRate}`;
                        adoptBtn.addEventListener('click', async e => {
                            e.preventDefault();
                            adoptBtn.disabled = true;
                            try {
                                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                                const body = new URLSearchParams({
                                    scheduleId: review.dataset.scheduleId,
                                    entryId: row.dataset.entryId,
                                    kind: kind === 'tariff' ? 'Tariff' : 'BAF'
                                });
                                const res = await fetch(review.dataset.adoptUrl, {
                                    method: 'POST',
                                    headers: { 'RequestVerificationToken': token, 'X-Requested-With': 'XMLHttpRequest' },
                                    body
                                });
                                const result = await res.json();
                                if (!res.ok || !result.success) throw new Error(result.message || 'Could not adopt table rate.');
                                cell.textContent = result.newRate || formattedRate;
                                cell.dataset.manual = 'false';
                                cell.classList.remove('schedule-review-manual');
                                cell.parentElement.classList.remove('schedule-review-attention', 'schedule-review-override');
                                sourceEl.textContent = 'Table estimate · planned date';
                                cell.parentElement.querySelectorAll('.schedule-adopt-rate, .schedule-adopt-sep').forEach(el => el.remove());
                                const captureWrapper = cell.parentElement.querySelector('.schedule-rate-capture-wrapper');
                                if (captureWrapper) {
                                    captureWrapper.innerHTML = '<small class="ui-caption text-muted">Standard table rate</small>';
                                }
                            } catch (err) {
                                ModernAlert.error(err.message || 'Could not adopt table rate.');
                                adoptBtn.disabled = false;
                            }
                        });
                        const sep = document.createElement('span');
                        sep.className = 'schedule-adopt-sep ui-caption ui-muted';
                        sep.textContent = ' · ';
                        sourceEl.after(sep, adoptBtn);
                    }
                }
            });
        } catch (_) {
            cells.forEach(cell => {
                const kind = cell.dataset.reviewRate;
                const manual = cell.dataset.manual === 'true';
                const cellVal = Number((cell.textContent || '').replace(/,/g, '').trim());
                const isZero = manual && cellVal === 0;
                const isZeroBaf = kind === 'baf' && isZero;
                const isZeroTariff = kind === 'tariff' && isZero;
                cell.parentElement.classList.toggle('schedule-review-attention', manual && !isZeroBaf);
                cell.parentElement.classList.toggle('schedule-review-override', false);
                if (!manual) cell.textContent = 'Unavailable';
                cell.parentElement.querySelector('[data-rate-source]').textContent = manual
                    ? isZeroBaf ? 'Zero BAF' : isZeroTariff ? 'Zero tariff' : 'Saved manual rate · table lookup unavailable'
                    : 'Could not check table rate';
            });
        }
    });
})();
