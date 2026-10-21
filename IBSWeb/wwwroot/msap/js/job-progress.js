window.JobProgress = {
    async load(container, values) {
        if (!container) return;
        container.progressRequest?.abort();
        const request = new AbortController();
        container.progressRequest = request;
        const parameters = new URLSearchParams({ actionStage: container.dataset.actionStage });
        if (values.customerOrderSlipId) parameters.set('customerOrderSlipId', values.customerOrderSlipId);
        (values.billingIds || []).forEach(id => parameters.append('billingIds', id));
        (values.dispatchTicketIds || []).forEach(id => parameters.append('dispatchTicketIds', id));
        container.replaceChildren();
        if (!values.customerOrderSlipId && !values.billingIds?.length && !values.dispatchTicketIds?.length) return;
        try {
            const response = await fetch(`${container.dataset.progressUrl}?${parameters}`, { signal: request.signal });
            if (!response.ok || response.redirected) throw new Error('Unable to load Job Progress. Refresh and check your access.');
            const html = await response.text();
            if (!request.signal.aborted) container.innerHTML = html;
        } catch (error) {
            if (error.name !== 'AbortError' && !request.signal.aborted) container.textContent = error.message;
        }
    }
};
