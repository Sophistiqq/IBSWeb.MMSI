(() => {
    let busy = false;
    const escape = text => $('<div>').text(text).html();
    document.addEventListener('click', async event => {
        const button = event.target.closest('[data-collection-banking]');
        if (!button) return;
        event.preventDefault();
        if (busy) return;
        busy = true;
        try {
            const response = await fetch(button.href, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Unable to load collection details. Refresh and check your access.');
            const html = await response.text();
            if (!html.includes('id="collectionBankingForm"')) throw new Error('Collection inputs are unavailable. Refresh and check your access.');
            let postUrl;
            const review = await ModernAlert.confirm({
                title: button.dataset.collectionBanking,
                text: html,
                confirmText: 'Save',
                didOpen: popup => {
                    ModernSelect.init('#collectionBankingForm .js-modern-select');
                    const form = popup.querySelector('#collectionBankingForm');
                    const syncAuthSection = () => {
                        const authSection = form?.querySelector('#bankingAuthSection');
                        if (!authSection) return;
                        const closedMonths = new Set((authSection.dataset.closedMonths || '').split(',').filter(Boolean));
                        const dateValue = form.querySelector('#bankingDate')?.value || '';
                        const ym = dateValue.length >= 7 ? dateValue.slice(0, 7) : '';
                        const needsAuth = authSection.dataset.finalized === 'true' && closedMonths.has(ym);
                        authSection.hidden = !needsAuth;
                        const reasonInput = authSection.querySelector('#bankingReason');
                        const fileInput = authSection.querySelector('#bankingAuthFile');
                        if (reasonInput) reasonInput.required = needsAuth;
                        if (fileInput) fileInput.required = needsAuth;
                    };
                    syncAuthSection();
                    form?.querySelector('#bankingDate')?.addEventListener('change', syncAuthSection);
                    form?.querySelector('#bankingDate')?.addEventListener('input', syncAuthSection);
                    form?.addEventListener('submit', event => {
                        event.preventDefault();
                        Swal.clickConfirm();
                    });
                },
                preConfirm: () => {
                    const form = Swal.getPopup().querySelector('#collectionBankingForm');
                    if (!form || !form.reportValidity()) return false;
                    const authSection = form.querySelector('#bankingAuthSection');
                    const requiresAuth = authSection && !authSection.hidden;
                    if (requiresAuth) {
                        const reasonInput = form.querySelector('#bankingReason');
                        if (reasonInput && (reasonInput.value.trim().length < 5 || reasonInput.value.trim().length > 500)) {
                            Swal.showValidationMessage('Please provide a valid reason (5 to 500 characters).');
                            return false;
                        }
                        const fileInput = form.querySelector('#bankingAuthFile');
                        if (fileInput && (!fileInput.files || fileInput.files.length === 0)) {
                            Swal.showValidationMessage('Upload an approved authorization document (PDF, PNG, or JPEG).');
                            return false;
                        }
                    }
                    postUrl = form.action;
                    return new FormData(form);
                }
            });
            if (!review.isConfirmed) return;
            ModernAlert.showLoading('Updating collection...');
            const saved = await fetch(postUrl, {
                method: 'POST', body: review.value, headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!saved.ok || saved.redirected) throw new Error('Unable to update this collection. Refresh and try again.');
            const result = await saved.json();
            if (!result.success) throw new Error(result.message || 'Collection update failed.');
            window.location.reload();
        } catch (error) {
            ModernAlert.error(escape(error.message));
        } finally {
            busy = false;
        }
    });
})();
