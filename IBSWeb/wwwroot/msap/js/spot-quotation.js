document.addEventListener('DOMContentLoaded', () => {
    let modal = document.getElementById('spotQuotationModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'spotQuotationModal';
        modal.className = 'hidden';
        modal.style.cssText = 'position:fixed; top:0; left:0; width:100vw; height:100vh; background:rgba(0,0,0,0.5); z-index:9999; display:none; align-items:center; justify-content:center;';
        document.body.appendChild(modal);
    }

    const closeModal = () => {
        modal.classList.add('hidden');
        modal.removeAttribute('open');
        modal.style.display = 'none';
        modal.innerHTML = '';
    };

    const attachSpotHandlers = () => {
        document.querySelectorAll('[data-spot-quotation]').forEach(button => {
            if (button.dataset.spotInitialized) return;
            button.dataset.spotInitialized = 'true';

            button.addEventListener('click', async (e) => {
                e.preventDefault();
                const scheduleId = button.dataset.scheduleId;
                const entryId = button.dataset.entryId;
                if (!scheduleId || !entryId) return;

                try {
                    ModernAlert.showLoading('Loading spot quotation details…');
                    const response = await fetch(`/MSAP/VesselSchedule/GetSpotQuotation?scheduleId=${scheduleId}&entryId=${entryId}`, {
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    });
                    ModernAlert.close();
                    if (!response.ok) {
                        ModernAlert.error('Failed to load spot quotation form.');
                        return;
                    }
                    const html = await response.text();
                    modal.innerHTML = html;
                    modal.classList.remove('hidden');
                    modal.setAttribute('open', '');
                    modal.style.display = 'flex';

                    modal.querySelectorAll('[data-close-spot-modal]').forEach(btn => btn.addEventListener('click', closeModal));
                    const newForm = modal.querySelector('#spotQuotationForm');
                    if (newForm) {
                        newForm.addEventListener('submit', handleSpotSubmit);
                    }
                } catch (err) {
                    ModernAlert.error(err.message || 'Error loading spot quotation modal.');
                }
            });
        });
    };

    async function handleSpotSubmit(e) {
        e.preventDefault();
        const form = e.target;
        const submitBtn = form.querySelector('button[type="submit"]');
        if (submitBtn) submitBtn.disabled = true;

        const formData = new FormData(form);
        try {
            ModernAlert.showLoading('Applying spot quotation…');
            const res = await fetch(form.action || '/MSAP/VesselSchedule/SaveSpotQuotation', {
                method: 'POST',
                body: formData,
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            const data = await res.json();
            if (data.success) {
                await ModernAlert.success(data.message || 'Spot quotation applied successfully.');
                window.location.reload();
            } else {
                ModernAlert.error(data.message || 'Failed to apply spot quotation.');
            }
        } catch (err) {
            ModernAlert.error(err.message || 'Error saving spot quotation.');
        } finally {
            if (submitBtn) submitBtn.disabled = false;
        }
    }

    attachSpotHandlers();

    // Support re-attachment when dynamic content loads (e.g. review modal)
    window.attachSpotQuotationHandlers = attachSpotHandlers;
});
