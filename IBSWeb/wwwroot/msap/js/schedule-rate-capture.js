(() => {
    const review = document.getElementById('scheduleReview');
    if (!review) return;
    const escape = value => $('<div>').text(value ?? '').html();
    let saving = false;
    review.querySelectorAll('[data-rate-capture]').forEach(button => button.addEventListener('click', async () => {
        if (saving) return;
        saving = true;
        button.disabled = true;
        const kind = button.dataset.kind === '1' ? 'Basic' : 'BAF';
        try {
            const date = await ModernAlert.confirm({
                title: `Save ${kind} for reuse`,
                text: '<label for="captureEffectiveDate">Effective date</label>' +
                    `<input id="captureEffectiveDate" type="date" class="modern-input" min="1900-01-01" max="9998-12-31" required value="${escape(button.dataset.effectiveDate)}">` +
                    (kind === 'BAF' ? '<p>BAF coverage ends at the next Tuesday, 12:00 AM. Review zero explicitly before saving it.</p>' : '<p>A one-time override can stay on this schedule without changing reusable rates.</p>'),
                confirmText: 'Review rate', cancelText: 'Keep reviewing',
                didOpen: () => document.getElementById('captureEffectiveDate').focus(),
                preConfirm: () => {
                    const input = document.getElementById('captureEffectiveDate');
                    if (!input.reportValidity()) return false;
                    return input.value;
                }
            });
            if (!date.isConfirmed) return;
            const body = new URLSearchParams({
                scheduleId: button.dataset.scheduleId, entryId: button.dataset.entryId,
                kind: button.dataset.kind, effectiveDate: date.value
            });
            const response = await fetch(review.dataset.capturePreviewUrl + '?' + body, {headers: {'X-Requested-With': 'XMLHttpRequest'}});
            if (response.redirected) throw new Error('Refresh and check your rate approval access.');
            const preview = await response.json();
            if (!response.ok || !preview.fingerprint) throw new Error(preview.message || 'Rate preview is unavailable.');
            const row = preview.rate;
            const confirmation = await ModernAlert.confirm({
                title: `Approve reusable ${kind} rate?`,
                text: `<p>${escape(row.customerName)} · ${escape(row.portName)} / ${escape(row.terminalName)} · ${escape(row.serviceName)}</p>` +
                    `<p>${escape(preview.chargingContext)} · effective ${escape(date.value)}</p>` +
                    `<p>Previous: ${escape(row.storedAmountText ?? 'None')} → Approved amount: ${escape(row.amountText)}</p>` +
                    '<p>This saves an active rate immediately. Saved schedule amounts, tickets and bills remain unchanged.</p>',
                confirmText: 'Approve and save rate', cancelText: 'Keep reviewing'
            });
            if (!confirmation.isConfirmed) return;
            body.set('fingerprint', preview.fingerprint);
            body.set('__RequestVerificationToken', document.querySelector('#captureRateToken input[name="__RequestVerificationToken"]').value);
            const saved = await fetch(review.dataset.captureUrl, {
                method: 'POST',
                body,
                headers: {'X-Requested-With': 'XMLHttpRequest'}
            });
            if (saved.redirected) throw new Error('Refresh and check your rate approval access.');
            const result = await saved.json();
            if (!saved.ok || !result.url) throw new Error(result.message || 'The rate could not be saved.');
            window.location.reload();
        } catch (error) {
            ModernAlert.error(escape(error.message));
        } finally {
            saving = false;
            button.disabled = false;
        }
    }));
})();
