document.querySelectorAll('[data-schedule-action]').forEach(form => {
    let reviewing = false;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (reviewing) return;
        reviewing = true;
        const action = form.dataset.scheduleAction;
        const completing = action === 'Complete';
        const result = await ModernAlert.confirm({
            title: completing ? 'Complete vessel service?' : 'Cancel schedule?',
            text: completing ? 'Confirm that all services are finished and no more Dispatch Tickets are expected. New tickets will be blocked. Charge calculation, billing approval and collection will continue.' : 'This cancels the schedule and any linked COS together. The booking becomes read-only and records are retained.',
            icon: 'warning',
            confirmText: completing ? 'Complete Service' : 'Cancel Schedule'
        });
        if (result.isConfirmed) {
            ModernAlert.showLoading('Updating schedule…');
            form.submit();
        } else {
            reviewing = false;
        }
    });
});
