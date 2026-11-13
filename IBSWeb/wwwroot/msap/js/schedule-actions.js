document.querySelectorAll('[data-schedule-action]').forEach(form => {
    let reviewing = false;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (reviewing) return;
        reviewing = true;
        const action = form.dataset.scheduleAction;

        if (action === 'Cancel') {
            const result = await ModernAlert.prompt({
                title: 'Cancel schedule?',
                text: 'This cancels the schedule and any linked COS together. The booking becomes read-only and records are retained.',
                placeholder: 'Reason for cancellation (optional)',
                confirmText: 'Cancel Schedule',
                cancelText: 'Keep Schedule'
            });
            if (result.isConfirmed) {
                let reasonInput = form.querySelector('input[name="reason"]');
                if (!reasonInput) {
                    reasonInput = document.createElement('input');
                    reasonInput.type = 'hidden';
                    reasonInput.name = 'reason';
                    form.appendChild(reasonInput);
                }
                reasonInput.value = (result.value || '').trim();
                ModernAlert.showLoading('Cancelling schedule…');
                form.submit();
            } else {
                reviewing = false;
            }
            return;
        }

        const completing = action === 'Complete';
        const result = await ModernAlert.confirm({
            title: completing ? 'Complete vessel service?' : 'Confirm action?',
            text: completing ? 'Confirm that all services are finished and no more Dispatch Tickets are expected. New tickets will be blocked. Charge calculation, billing approval and collection will continue.' : 'Confirm this schedule action.',
            icon: 'warning',
            confirmText: completing ? 'Complete Service' : 'Confirm'
        });
        if (result.isConfirmed) {
            ModernAlert.showLoading('Updating schedule…');
            form.submit();
        } else {
            reviewing = false;
        }
    });
});
