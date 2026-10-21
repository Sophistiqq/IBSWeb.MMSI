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
                    popup.querySelector('form')?.addEventListener('submit', event => {
                        event.preventDefault();
                        Swal.clickConfirm();
                    });
                },
                preConfirm: () => {
                    const form = Swal.getPopup().querySelector('#collectionBankingForm');
                    if (!form || !form.reportValidity()) return false;
                    postUrl = form.action;
                    return new URLSearchParams(new FormData(form));
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
