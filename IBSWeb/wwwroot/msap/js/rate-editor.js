(() => {
    'use strict';
    const editor = document.getElementById('rateEditor');
    if (!editor) return;
    const canEdit = editor.dataset.canEdit === 'true';
    const model = JSON.parse(document.getElementById('rateEditorData').textContent);
    const body = document.getElementById('rateEditorRows');
    const error = document.getElementById('rateEditorError');
    const save = document.getElementById('rateSave');
    const filters = ['Customer', 'Port', 'Terminal', 'Service'].map(name => document.getElementById('rate' + name));
    const search = document.getElementById('rateSearch');
    const changedOnly = document.getElementById('rateChangedOnly');
    const rows = model.rows.map(row => ({ ...row, amount: row.amountText ?? '', originalAmount: row.amountText ?? '', originalDate: row.effectiveDate }));
    let dirty = false;
    const showError = message => { error.textContent = message; error.hidden = !message; };
    const scaled = value => {
        if (!/^\d{1,14}(?:\.\d{1,4})?$/.test(value)) return null;
        const [whole, fraction = ''] = value.split('.');
        return BigInt(whole) * 10000n + BigInt(fraction.padEnd(4, '0'));
    };
    const validDate = value => /^\d{4}-\d{2}-\d{2}$/.test(value) && value >= '1900-01-01' && value <= '9998-12-31'
        && !Number.isNaN(Date.parse(value)) && new Date(value + 'T00:00:00Z').toISOString().slice(0, 10) === value;
    const changed = row => !row.existingRateId || row.effectiveDate !== row.baselineDate || scaled(row.amount) !== scaled(row.storedAmountText ?? '');
    const isNewDate = row => !row.baselineRateId || row.effectiveDate !== row.baselineDate;
    const visible = row => filters.every((filter, index) => !filter.value || row[['customerId', 'portId', 'terminalId', 'serviceId'][index]] === Number(filter.value))
        && (!changedOnly.checked || changed(row))
        && `${row.customerCode} ${row.customerName} ${row.portName} ${row.terminalName} ${row.serviceName}`.toLowerCase().includes(search.value.trim().toLowerCase());
    function updateState() {
        let edited = 0, hiddenChanges = 0, shown = 0;
        rows.forEach(row => {
            const modified = changed(row);
            row.element.hidden = !visible(row);
            if (modified) edited++;
            if (modified && row.element.hidden) hiddenChanges++;
            if (!row.element.hidden) shown++;
            row.element.classList.toggle('rate-row-new', modified && isNewDate(row));
            row.element.classList.toggle('rate-row-changed', modified && !isNewDate(row));
            row.dateInput.classList.toggle('rate-cell-new', isNewDate(row));
            row.amountInput.classList.toggle('rate-cell-changed', row.storedAmountText != null && scaled(row.amount) !== scaled(row.storedAmountText));
            row.amountInput.classList.toggle('rate-cell-new', row.storedAmountText == null);
            row.discard.hidden = !canEdit || !modified && !model.id;
            const valid = validDate(row.effectiveDate) && scaled(row.amount) !== null;
            row.dateInput.setAttribute('aria-invalid', String(!validDate(row.effectiveDate)));
            row.amountInput.setAttribute('aria-invalid', String(scaled(row.amount) === null));
            row.status.textContent = !valid ? 'Check date / amount' : modified ? isNewDate(row) ? 'New dated rate' : 'Correction' : 'Unchanged';
        });
        document.getElementById('rateCounts').textContent = `${shown} visible · ${edited} changed · ${hiddenChanges} hidden changes`;
    }
    function addCell(tr, text) {
        const cell = document.createElement('td');
        cell.textContent = text;
        tr.append(cell);
        return cell;
    }
    function appendRow(row) {
        const tr = row.element = document.createElement('tr');
        addCell(tr, `${row.customerCode} · ${row.customerName}`);
        addCell(tr, `${row.portName} / ${row.terminalName}`);
        addCell(tr, row.serviceName);
        addCell(tr, row.baselineDate ? `${row.baselineDate} / ${row.storedAmountText ?? ''}` : 'No previous rate');
        ['effectiveDate', 'amount'].forEach((key, column) => {
            const input = document.createElement('input');
            input.type = column === 0 ? 'date' : 'text';
            input.className = 'modern-input';
            input.dataset.column = column;
            input.value = row[key];
            input.setAttribute('aria-label', `${row.customerName}, ${row.serviceName}: ${column === 0 ? 'effective date' : model.kind === 2 ? 'BAF amount' : 'tariff amount'}`);
            input.required = canEdit;
            input.readOnly = !canEdit;
            if (column === 0) { input.min = '1900-01-01'; input.max = '9998-12-31'; }
            else { input.inputMode = 'decimal'; input.autocomplete = 'off'; }
            if (column === 0) row.dateInput = input;
            else row.amountInput = input;
            input.addEventListener('input', () => { if (!canEdit) return; row[key] = input.value.trim(); dirty = true; updateState(); });
            input.addEventListener('keydown', event => {
                if (event.key !== 'Enter') return;
                event.preventDefault();
                const displayed = rows.filter(visible);
                const next = displayed[displayed.indexOf(row) + (event.shiftKey ? -1 : 1)];
                next?.[column === 0 ? 'dateInput' : 'amountInput'].focus();
            });
            input.addEventListener('paste', event => {
                if (!canEdit) { event.preventDefault(); return; }
                const text = event.clipboardData.getData('text').trim();
                if (!/[\t\r\n]/.test(text)) return;
                event.preventDefault();
                const cells = text.split(/\r?\n/).map(line => line.split('\t'));
                const displayed = rows.filter(visible);
                const start = displayed.indexOf(row);
                const changes = [];
                for (let index = 0; index < cells.length; index++) {
                    if (!displayed[start + index] || cells[index].length + column > 2) {
                        showError('Paste only into existing date and amount cells. Add new customer/service combinations before pasting.'); return;
                    }
                    for (let offset = 0; offset < cells[index].length; offset++) {
                        const target = column + offset;
                        let value = cells[index][offset].trim();
                        if (target === 1 && /^\d{1,3}(,\d{3})+(\.\d{1,4})?$/.test(value)) value = value.replaceAll(',', '');
                        if (target === 0 ? !validDate(value) : scaled(value) === null) {
                            showError(`Paste row ${index + 1}: use ISO dates (yyyy-mm-dd) and nonnegative amounts with at most four decimals.`); return;
                        }
                        changes.push([displayed[start + index], target, value]);
                    }
                }
                changes.forEach(([item, target, value]) => {
                    item[target === 0 ? 'effectiveDate' : 'amount'] = value;
                    item[target === 0 ? 'dateInput' : 'amountInput'].value = value;
                });
                dirty = true; showError(''); updateState();
            });
            addCell(tr, '').append(input);
        });
        const action = addCell(tr, '');
        row.status = document.createElement('span');
        action.append(row.status);
        const discard = document.createElement('button');
        discard.type = 'button';
        discard.className = 'rate-discard';
        discard.textContent = 'Remove from draft';
        discard.addEventListener('click', () => { rows.splice(rows.indexOf(row), 1); tr.remove(); dirty = true; updateState(); });
        row.discard = discard;
        action.append(discard);
        body.append(tr);
    }
    rows.forEach(appendRow);
    $(filters).on('change', updateState);
    [search, changedOnly].forEach(control => control.addEventListener('input', updateState));
    $(filters[1]).on('change', () => {
        const terminal = filters[2];
        const previous = terminal.value;
        terminal.replaceChildren(new Option('All terminals', ''));
        model.terminals.filter(item => !filters[1].value || item.portId === Number(filters[1].value))
            .forEach(item => terminal.add(new Option(item.name, item.id)));
        terminal.value = Array.from(terminal.options).some(option => option.value === previous) ? previous : '';
        window.refreshModernSelect($(terminal));
        $(terminal).trigger('change');
        updateState();
    });
    document.getElementById('rateAdd')?.addEventListener('click', () => {
        if (filters.some(filter => !filter.value)) { showError('Select a customer, port, terminal and service first.'); return; }
        const customer = model.customers.find(item => item.id === Number(filters[0].value));
        const port = model.ports.find(item => item.id === Number(filters[1].value));
        const terminal = model.terminals.find(item => item.id === Number(filters[2].value));
        const service = model.services.find(item => item.id === Number(filters[3].value));
        if (terminal.portId !== port.id) { showError('The selected terminal must belong to the port.'); return; }
        const row = { customerId: customer.id, customerCode: customer.code, customerName: customer.name, portId: port.id, portName: port.name,
            terminalId: terminal.id, terminalName: terminal.name, serviceId: service.id, serviceName: service.name,
            effectiveDate: model.asOfDate, amount: '', originalAmount: '', originalDate: model.asOfDate };
        rows.push(row); appendRow(row); dirty = true; showError(''); updateState(); row.amountInput.focus();
    });
    save?.addEventListener('click', async () => {
        const selected = rows.filter(row => model.id || changed(row));
        if (!selected.length) { showError('Edit an amount or effective date, or add a new rate first.'); return; }
        const invalid = selected.find(row => !validDate(row.effectiveDate) || scaled(row.amount) === null);
        if (invalid) { showError('Correct invalid dates and amounts before saving. Hidden edited rows are included.'); return; }
        const keys = new Set();
        for (const row of selected) {
            const key = [row.customerId, row.terminalId, row.serviceId, row.effectiveDate].join('|');
            if (keys.has(key)) { showError('Duplicate customer, terminal, service and effective date. Each combination/date must occur once.'); return; }
            keys.add(key);
        }
        save.disabled = true; showError('');
        try {
            const response = await fetch(editor.dataset.saveUrl, {
                method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: editor.querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ id: model.id, kind: model.kind, rows: selected.map(row => ({
                    customerId: row.customerId, portId: row.portId, terminalId: row.terminalId, serviceId: row.serviceId,
                    effectiveDate: row.effectiveDate, amount: row.amount
                })) })
            });
            if (response.redirected) throw new Error('Your session may have expired. Open the application in another tab to sign in, then retry.');
            const result = await response.json();
            if (!response.ok || !result.url) throw new Error(result.message || 'The draft could not be saved.');
            dirty = false; window.location.assign(result.url);
        } catch (failure) { showError(failure.message || 'The draft could not be saved. Your edits remain here.'); }
        finally { save.disabled = false; }
    });
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    updateState();
})();
