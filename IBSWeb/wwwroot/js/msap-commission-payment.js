(function (window, $) {
    'use strict';

    window.MsapCommissionPayment = function (options) {
        let items = [];
        let profileError = null;
        let withholdingAccount = null;
        let withholdingTitle = null;
        let selectedKeys = new Set(
            (options.initialSelected || []).map(x => `${x.billingId}:${x.category}`)
        );
        let requestSeq = 0;

        function keyOf(item) {
            return `${item.billingId}:${item.category}`;
        }

        function isMsapActive() {
            return options.getSource() === 'MsapBilling';
        }

        function setProfileError(message) {
            profileError = message || null;
            $('#rep-profile-error').text(profileError || '');
        }

        function clearWithholdingRow() {
            $('#msapWithholdingRow').hide().find('input[name]').prop('disabled', true);
            $('#msapWithholdingAccount').val('');
            $('#msapWithholdingTitle').val('Expanded Withholding Tax');
            $('#msapWithholdingAmount').val('0.0000');
        }

        function syncWithholdingRow(totalWithholding) {
            if (!isMsapActive() || !withholdingAccount || totalWithholding <= 0) {
                clearWithholdingRow();
                return;
            }
            $('#msapWithholdingAccount').val(withholdingAccount);
            $('#msapWithholdingTitle').val(withholdingTitle || 'Expanded Withholding Tax');
            $('#msapWithholdingAmount').val(formatNumberToFour(totalWithholding));
            $('#msapWithholdingRow').show().find('input[name]').prop('disabled', false);
        }

        function renderItems() {
            const tbody = $('.msapBillingList').empty();
            let totalGross = 0;
            let totalWithholding = 0;
            let totalNet = 0;

            items.forEach(item => {
                const k = keyOf(item);
                const available = !profileError && item.grossAmount > 0;
                const checked = available && selectedKeys.has(k);
                const tr = $('<tr>');

                const cb = $('<input>').attr({
                    type: 'checkbox',
                    class: 'msapSelectCheckbox',
                    'aria-label': `Select ${item.billingNumber} ${item.categoryLabel}`
                }).val(k).prop('disabled', !available).prop('checked', checked);

                tr.append($('<td>').append(cb));
                tr.append($('<td>').text(item.billingNumber));
                tr.append($('<td>').text(item.cosNumber || '—'));
                tr.append($('<td>').text(item.categoryLabel));
                tr.append($('<td>').addClass('text-end').text(formatNumberToFour(item.grossAmount)));
                tr.append($('<td>').addClass('text-end').text(formatNumberToFour(item.withholdingAmount)));
                tr.append($('<td>').addClass('text-end').text(formatNumberToFour(item.netAmount)));
                tbody.append(tr);

                totalGross += item.grossAmount;
                totalWithholding += item.withholdingAmount;
                totalNet += item.netAmount;
            });

            if (items.length === 0) {
                tbody.append($('<tr>').append(
                    $('<td>').attr('colspan', 7).addClass('text-center text-muted py-3')
                        .text('No approved unpaid MSAP commissions or incentives found for this representative.')
                ));
            } else {
                tbody.append(`
                    <tr style="border-bottom:double;" class="sticky-lg-bottom">
                        <td colspan="7" class="pt-1"></td>
                    </tr>
                    <tr class="sticky-bottom bg-white">
                        <td colspan="4" class="fw-bold text-end p-2">Available Total:</td>
                        <td class="text-end">${formatNumberToFour(totalGross)}</td>
                        <td class="text-end">${formatNumberToFour(totalWithholding)}</td>
                        <td class="text-end">${formatNumberToFour(totalNet)}</td>
                    </tr>
                `);
            }

            updateSelectedTotals();
        }

        function updateSelectedTotals() {
            if (!isMsapActive()) {
                return;
            }

            const selected = items.filter(i => selectedKeys.has(keyOf(i)));
            const container = $('#selectedMsapItems').empty();
            selected.forEach((item, idx) => {
                container.append($('<input>').attr({ type: 'hidden', name: `MsapItems[${idx}].BillingId` }).val(item.billingId));
                container.append($('<input>').attr({ type: 'hidden', name: `MsapItems[${idx}].Category` }).val(item.category));
            });

            const totalGross = selected.reduce((sum, i) => sum + i.grossAmount, 0);
            const totalWithholding = selected.reduce((sum, i) => sum + i.withholdingAmount, 0);

            $('#msapSelectedCount').text(selected.length);
            $('.selected-msap-count').toggle(selected.length > 0);
            const enabledCount = $('.msapSelectCheckbox:enabled').length;
            $('#msapSelectAll').prop('checked', enabledCount > 0 && enabledCount === $('.msapSelectCheckbox:checked').length);

            $('.totalNetOfEWT').val(formatNumberToFour(totalGross));
            syncWithholdingRow(totalWithholding);

            if (typeof window.updateTotalDebitAndCredit === 'function') {
                window.updateTotalDebitAndCredit();
            }
            if (typeof window.validateDebitAndCredit === 'function') {
                window.validateDebitAndCredit();
            }
            if (profileError) {
                $('#balance-error-message').text(profileError);
                $("button[type='submit']").prop('disabled', true);
            }
        }

        function loadRepresentative(representativeId, cvId, preserveInitialSelection) {
            const seq = ++requestSeq;
            if (!preserveInitialSelection) {
                selectedKeys.clear();
            }
            items = [];
            withholdingAccount = null;
            withholdingTitle = null;
            setProfileError(null);

            if (!representativeId) {
                $("#SupplierAddress").val('');
                $("#SupplierTinNo").val('');
                $(".supplierName").val('');
                renderItems();
                return;
            }

            $.getJSON(options.endpointUrl, { representativeId: representativeId, cvId: cvId || null })
                .done(function (data) {
                    if (seq !== requestSeq) {
                        return;
                    }
                    $("#SupplierAddress").val(data.address || '');
                    $("#SupplierTinNo").val(data.tin || '');
                    $(".supplierName").val(data.representativeName || '');
                    withholdingAccount = data.withholdingAccountNumber || null;
                    withholdingTitle = data.withholdingAccountTitle || null;
                    setProfileError(data.profileError);
                    items = data.items || [];
                    if (preserveInitialSelection && selectedKeys.size === 0) {
                        items.filter(i => i.isSelected).forEach(i => selectedKeys.add(keyOf(i)));
                    }
                    renderItems();
                })
                .fail(function (xhr) {
                    if (seq !== requestSeq) {
                        return;
                    }
                    setProfileError(xhr.responseText || 'Unable to load representative commissions.');
                    renderItems();
                });
        }

        $(document).on('change', '.msapSelectCheckbox', function () {
            if (this.checked) {
                selectedKeys.add(this.value);
            } else {
                selectedKeys.delete(this.value);
            }
            updateSelectedTotals();
        });

        $(document).on('change', '#msapSelectAll', function () {
            const checkAll = this.checked;
            $('.msapSelectCheckbox:enabled').each(function () {
                if (checkAll) {
                    selectedKeys.add(this.value);
                } else {
                    selectedKeys.delete(this.value);
                }
                $(this).prop('checked', checkAll);
            });
            updateSelectedTotals();
        });

        return {
            loadRepresentative,
            updateSelectedTotals,
            clearWithholdingRow
        };
    };
})(window, jQuery);
