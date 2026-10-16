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
            text: completing ? 'Confirm that all services are finished and no more Dispatch Tickets are expected. New tickets will be blocked. Tariff, approval, billing, posting and collection will continue.' : 'This cancels the schedule and any linked COS together. The booking becomes read-only and records are retained.',
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
document.getElementById('deleteScheduleButton')?.addEventListener('click', function () {
    const form = document.getElementById('deleteSchedule');
    ModernAlert.confirm({ title: 'Delete tentative schedule?', text: 'This removes the plan permanently.', icon: 'warning', confirmText: 'Delete schedule' })
        .then(result => { if (result.isConfirmed) form.submit(); });
});
