document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('otherTugChargeForm');
    if (!form) return;
    const rows = [...form.querySelectorAll('[data-charge-ticket]')];
    const error = message => {
        const output = document.getElementById('otherTugChargeError');
        output.textContent = message; output.hidden = !message;
    };
    let dirty = false;
    let busy = false;
    const update = () => {
        let total = 0;
        let pending = false;
        rows.forEach(row => {
            const checked = row.querySelector('[data-include]').checked;
            const amount = row.querySelector('[data-amount]');
            amount.disabled = form.dataset.editable !== 'true' || !checked;
            amount.required = checked;
            if (checked && amount.value === '') pending = true;
            else if (checked) total += Number(amount.value);
        });
        document.getElementById('otherTugChargeTotal').textContent = pending ? 'Pending' : total.toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    };
    form.addEventListener('input', () => { dirty = true; update(); });
    form.addEventListener('change', () => { dirty = true; update(); });
    async function submit(confirming) {
        if (busy) return;
        error('');
        if (confirming && dirty) { error('Save your edits and review the saved amounts before confirming.'); return; }
        if (!confirming && !form.reportValidity()) return;
        const selected = rows.filter(row => row.querySelector('[data-include]').checked);
        if (!selected.length || selected.length > 500) { error('Select between 1 and 500 actual tickets.'); return; }
        busy = true;
        const buttons = form.querySelectorAll('button'); buttons.forEach(button => { button.disabled = true; });
        try {
            const result = await ModernAlert.confirm({
                title: confirming ? 'Confirm the saved provider charges?' : 'Save provider charges for reviewing?',
                text: confirming ? 'Apply the reviewed allocations to AP Other Tugs reporting. The confirmed provider bill will be locked. Payment remains in IBS.' : 'Save the provider reference, document and ticket allocations. AP reports remain unchanged until confirmation.',
                confirmText: confirming ? 'Confirm charges' : 'Save for reviewing', cancelText: 'Keep reviewing'
            });
            if (!result.isConfirmed) return;
            const body = new FormData(form);
            body.set('id', form.elements.Id.value);
            body.set('reviewedRevision', form.elements.Revision.value);
            if (!confirming) selected.forEach((row, index) => {
                body.set(`Allocations[${index}].DispatchTicketId`, row.dataset.chargeTicket);
                body.set(`Allocations[${index}].Amount`, row.querySelector('[data-amount]').value);
            });
            const response = await fetch(confirming ? document.getElementById('confirmOtherTugCharge').dataset.url : form.action,
                { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Request failed');
            const saved = await response.json();
            if (!saved.success || !confirming && !saved.redirectUrl) { error(saved.message || 'Unable to save the charge.'); return; }
            dirty = false;
            await ModernAlert.success(saved.message);
            if (confirming) window.location.reload(); else window.location.href = saved.redirectUrl;
        } catch { error('Unable to save provider charges. Check your connection and access, then try again.'); }
        finally { busy = false; buttons.forEach(button => { button.disabled = false; }); }
    }
    form.addEventListener('submit', event => { event.preventDefault(); if (form.dataset.editable === 'true') submit(false); });
    document.getElementById('confirmOtherTugCharge')?.addEventListener('click', () => submit(true));
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    update();
});
