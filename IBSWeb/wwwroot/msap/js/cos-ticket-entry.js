const initializeCosTicketEntry = () => {
    document.querySelectorAll('[data-cancel-service]').forEach(button => button.addEventListener('click', async () => {
        if (button.disabled) return;
        button.disabled = true;
        try {
            const result = await ModernAlert.prompt({
                title: 'Cancel unperformed service?',
                text: 'Use this only when the service did not happen. Its planned row and your reason will be retained.',
                placeholder: 'Reason for cancellation', confirmText: 'Cancel service', cancelText: 'Keep service',
                validator: value => !value?.trim() || value.trim().length > 500 ? 'Enter a reason of up to 500 characters.' : null
            });
            if (!result.isConfirmed) return;
            const body = new FormData();
            body.set('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
            body.set('id', button.dataset.cosId); body.set('entryId', button.dataset.cancelService); body.set('reason', result.value.trim());
            const response = await fetch(button.dataset.url, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Cancellation failed');
            const saved = await response.json();
            if (!saved.success) { await ModernAlert.error(saved.message || 'The service could not be cancelled.'); return; }
            await ModernAlert.success(saved.message);
            window.location.reload();
        } catch {
            await ModernAlert.error('Unable to cancel the service. Refresh the COS and try again.');
        } finally { button.disabled = false; }
    }));
    document.querySelectorAll('[data-void-ticket]').forEach(button => button.addEventListener('click', async () => {
        if (button.disabled) return;
        button.disabled = true;
        try {
            const isFinal = button.dataset.finalized === 'true';
            const dispatchNo = button.dataset.dispatchNumber || '';
            const text = isFinal
                ? 'Voiding this ticket removes it from billing calculations and tags this finalized-period record as Adjusted in the audit history.'
                : 'Voiding this ticket removes it from billing calculations and retains the voided row and your reason.';
            const result = await ModernAlert.promptAdjustment({
                title: dispatchNo ? `Void ticket #${dispatchNo}?` : 'Void ticket?',
                text,
                placeholder: 'Reason for voiding',
                confirmText: 'Void ticket',
                cancelText: 'Keep ticket',
                requireAttachment: isFinal
            });
            if (!result.isConfirmed) return;
            const body = new FormData();
            body.set('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
            body.set('id', button.dataset.voidTicket);
            body.set('customerOrderSlipId', button.dataset.cosId);
            body.set('reason', result.value.reason);
            if (result.value.file) {
                body.set('authorizationFile', result.value.file);
            }
            const response = await fetch(button.dataset.url, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Void failed');
            const saved = await response.json();
            if (!saved.success) { await ModernAlert.error(saved.message || 'The ticket could not be voided.'); return; }
            await ModernAlert.success(saved.message);
            window.location.href = button.dataset.cosUrl || `/MSAP/COS/Details/${encodeURIComponent(button.dataset.cosId)}`;
        } catch {
            await ModernAlert.error('Unable to void the ticket. Refresh the COS and try again.');
        } finally { button.disabled = false; }
    }));
    const ticketPanel = document.getElementById('actualTicketEntry');
    const ticketFormEl = document.getElementById('cosActualTicketForm') || document.getElementById('cosActualTicketDetails');
    if (ticketPanel && ticketFormEl) {
        initializeTicketFocusAndViewer(ticketPanel, ticketFormEl);
    }

    const form = document.getElementById('cosActualTicketForm');
    if (!form) return;

    const actual = document.getElementById('cosActualDuration');
    const billable = document.getElementById('cosBillableDuration');
    const times = () => {
        const startDate = form.elements.DateLeft.value;
        const endDate = form.elements.DateArrived.value;
        const startTime = form.elements.TimeLeft.value;
        const endTime = form.elements.TimeArrived.value;
        if (!startDate || !endDate || !startTime || !endTime) return null;
        return {
            start: Date.parse(`${startDate}T${startTime}+08:00`),
            end: Date.parse(`${endDate}T${endTime}+08:00`)
        };
    };
    const timeError = value => {
        if (!value) return 'Enter complete actual start and end dates/times.';
        if (!Number.isFinite(value.start) || !Number.isFinite(value.end)) return 'Enter valid actual start and end dates/times.';
        if (value.end <= value.start) return 'Actual end must be after actual start.';
        if (value.end > Date.now()) return 'Actual end is in the future. Enter a completed service time in Philippine time (UTC+8).';
        return null;
    };
    const preview = () => {
        const value = times();
        const error = timeError(value);
        if (error) {
            actual.textContent = error;
            billable.textContent = 'Enter valid actual times';
            return;
        }
        const minutes = (value.end - value.start) / 60000;
        actual.textContent = `${minutes.toFixed(0)} min · ${(minutes / 60).toFixed(2)} hours`;
        billable.textContent = `${Math.max(minutes / 60, 1).toFixed(2)} hours`;
    };
    ['DateLeft', 'TimeLeft', 'DateArrived', 'TimeArrived'].forEach(name => form.elements[name].addEventListener('input', preview));

    if (form.dataset.isNewTicket === 'true') {
        const ticketDateInput = form.elements.Date;
        const dateLeftInput = form.elements.DateLeft;
        const dateArrivedInput = form.elements.DateArrived;
        if (ticketDateInput && dateLeftInput && dateArrivedInput) {
            const hadDistinctPlannedStart = Boolean(dateLeftInput.value && ticketDateInput.value && dateLeftInput.value !== ticketDateInput.value);
            let dateLeftTouched = hadDistinctPlannedStart;
            let dateArrivedTouched = Boolean(dateArrivedInput.value && dateLeftInput.value && dateArrivedInput.value !== dateLeftInput.value);

            ticketDateInput.addEventListener('change', () => {
                if (!ticketDateInput.value) return;
                if (!dateLeftTouched) {
                    dateLeftInput.value = ticketDateInput.value;
                }
                if (!dateArrivedTouched) {
                    dateArrivedInput.value = dateLeftInput.value || ticketDateInput.value;
                }
                preview();
            });

            const onDateLeftChange = () => {
                dateLeftTouched = true;
                if (!dateLeftInput.value) return;
                if (!dateArrivedTouched || (dateArrivedInput.value && dateArrivedInput.value < dateLeftInput.value)) {
                    dateArrivedInput.value = dateLeftInput.value;
                }
                preview();
            };
            dateLeftInput.addEventListener('input', onDateLeftChange);
            dateLeftInput.addEventListener('change', onDateLeftChange);

            const onDateArrivedChange = () => {
                dateArrivedTouched = true;
            };
            dateArrivedInput.addEventListener('input', onDateArrivedChange);
            dateArrivedInput.addEventListener('change', onDateArrivedChange);
        }
    }

    form.noValidate = true;
    let submitting = false;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (submitting) return;
        let message = timeError(times());
        if (!message && !form.elements.TugMasterId.value) message = 'Select the captain / tug master for this service.';
        if (!message && form.dataset.imageRequired === 'true' && !form.elements.imageFile?.files?.length) {
            message = 'Attach a photo of the dispatch ticket before saving.';
        }
        if (!message && form.elements.Tons) {
            const tons = Number.parseFloat(form.elements.Tons.value);
            if (!Number.isFinite(tons) || tons <= 0) message = 'Enter the watering tons (greater than 0).';
        }
        if (!message && form.elements.DispatchRate) {
            const rate = Number.parseFloat(form.elements.DispatchRate.value);
            if (!Number.isFinite(rate) || rate <= 0) message = 'Enter a watering rate greater than 0.';
        }
        preview();
        if (message || !form.reportValidity()) {
            if (message) ModernAlert.error(message);
            return;
        }
        submitting = true;
        const button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        const editing = Boolean(form.elements.DispatchTicketId?.value);
        try {
            const confirmation = await ModernAlert.confirm({
                title: editing ? 'Save ticket changes?' : 'Save actual ticket?',
                text: 'Save the ticket details and supporting files for this service.',
                icon: 'question',
                confirmText: editing ? 'Save changes' : 'Save ticket',
                cancelText: 'Keep editing'
            });
            if (!confirmation.isConfirmed) return;
            ModernAlert.showLoading(editing ? 'Saving ticket changes…' : 'Saving actual ticket…');
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok || response.redirected) throw new Error('Save failed');
            const result = await response.json();
            if (!result.success || !result.redirectUrl) {
                await ModernAlert.error(result.message || 'Unable to save the ticket. Please try again.');
                return;
            }
            await ModernAlert.success(result.message || 'Ticket saved successfully.');
            window.location.href = result.redirectUrl;
        } catch {
            ModernAlert.error('Unable to save the ticket. Check your connection and access, then try again.');
        } finally {
            submitting = false;
            button.disabled = false;
        }
    }, true);
    preview();
};

const initializeTicketFocusAndViewer = (panel, formEl) => {
    const backdrop = document.getElementById('cosTicketBackdrop');
    const workspace = document.getElementById('cosEntryWorkspace');
    const entryMain = document.getElementById('cosEntryMain');
    const dropzone = document.getElementById('cosPhotoDropzone');
    const fileInput = document.getElementById('cosTicketImage');
    const previewImg = document.getElementById('cosTicketImagePreview');
    const emptyState = document.getElementById('cosTicketImageEmpty');
    const statusLabel = document.getElementById('cosTicketImageStatus');
    const fileNameLabel = document.getElementById('cosTicketImageFileName');
    const stage = document.getElementById('cosTicketImageStage');

    const isNewTicket = formEl.dataset.isNewTicket === 'true';
    const isReadOnly = formEl.dataset.isReadonly === 'true';
    const initialUrl = previewImg?.dataset.initialUrl || '';
    const closeUrl = panel.dataset.closeUrl || '';

    let blobUrl = null;
    let activeUrl = initialUrl;
    let scale = 1;
    let rotation = 0;
    let panX = 0;
    let panY = 0;

    const closeModernSelects = () => {
        document.querySelectorAll('.modern-select-dropdown.show').forEach(el => el.classList.remove('show'));
        document.querySelectorAll('.modern-select-trigger.active').forEach(el => el.classList.remove('active'));
    };

    const applyTransform = () => {
        if (!previewImg) return;
        previewImg.style.transform = `translate(${panX}px, ${panY}px) scale(${scale}) rotate(${rotation}deg)`;
        if (stage) {
            stage.classList.toggle('is-zoomed', scale > 1);
        }
    };

    const resetTransform = () => {
        scale = 1;
        rotation = 0;
        panX = 0;
        panY = 0;
        applyTransform();
    };

    const setImageSource = (url, label) => {
        activeUrl = url || '';
        const hasImg = Boolean(activeUrl);
        if (workspace) {
            workspace.dataset.hasImage = hasImg ? 'true' : 'false';
        }
        if (previewImg) {
            if (hasImg) {
                previewImg.src = activeUrl;
            } else {
                previewImg.removeAttribute('src');
            }
            previewImg.hidden = !hasImg;
        }
        if (emptyState) {
            emptyState.hidden = hasImg;
        }
        if (statusLabel) {
            statusLabel.textContent = label || (hasImg ? 'Ticket photo' : 'No photo');
        }
        resetTransform();
    };

    const setStep = (step, { focusTicketNumber = false } = {}) => {
        panel.dataset.ticketStep = step;
        if (workspace) {
            const isUploadStep = step === 'upload';
            workspace.inert = isUploadStep;
            if (isUploadStep) {
                workspace.setAttribute('aria-hidden', 'true');
            } else {
                workspace.removeAttribute('aria-hidden');
            }
        }
        if (focusTicketNumber && !isReadOnly) {
            const ticketInput = formEl.elements.DispatchNumber;
            if (ticketInput) {
                setTimeout(() => {
                    try { ticketInput.focus(); } catch { /* ignore focus errors */ }
                }, 180);
            }
        }
    };

    const closeModal = () => {
        if (!closeUrl) return;
        window.location.href = closeUrl;
    };

    const assignFileToInput = file => {
        if (!fileInput || !file) return false;
        if (!file.type || !file.type.startsWith('image/')) {
            ModernAlert.warning('Please choose an image file for the dispatch ticket photo.');
            return false;
        }
        try {
            const dt = new DataTransfer();
            dt.items.add(file);
            fileInput.files = dt.files;
            fileInput.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        } catch {
            ModernAlert.warning('Use Choose photo to attach this image on your browser.');
            return false;
        }
    };

    const handleFileSelected = () => {
        if (!fileInput) return;
        const file = fileInput.files && fileInput.files[0];
        if (file && (!file.type || !file.type.startsWith('image/'))) {
            fileInput.value = '';
            if (blobUrl) {
                URL.revokeObjectURL(blobUrl);
                blobUrl = null;
            }
            setImageSource(initialUrl, initialUrl ? 'Saved photo' : 'No photo');
            ModernAlert.warning('Please select an image file for the dispatch ticket photo.');
            return;
        }
        if (blobUrl) {
            URL.revokeObjectURL(blobUrl);
            blobUrl = null;
        }
        if (file) {
            blobUrl = URL.createObjectURL(file);
            setImageSource(blobUrl, 'Selected photo (unsaved)');
            if (fileNameLabel) fileNameLabel.textContent = file.name;
            if (workspace) {
                workspace.dataset.imageCollapsed = 'false';
                const toggleBtn = panel.querySelector('[data-image-toggle]');
                if (toggleBtn) {
                    toggleBtn.setAttribute('aria-expanded', 'true');
                    toggleBtn.title = 'Hide photo';
                    const icon = toggleBtn.querySelector('.material-symbols-outlined');
                    if (icon) icon.textContent = 'expand_less';
                }
            }
            if (panel.dataset.ticketStep === 'upload') {
                setStep('split', { focusTicketNumber: true });
            }
        } else if (initialUrl) {
            setImageSource(initialUrl, 'Saved photo');
            if (fileNameLabel) fileNameLabel.textContent = 'Saved photo';
        } else {
            setImageSource('', 'No photo');
            if (fileNameLabel) fileNameLabel.textContent = 'No file selected';
        }
    };

    if (fileInput) {
        fileInput.addEventListener('change', handleFileSelected);
    }

    panel.querySelectorAll('[data-trigger-photo-upload]').forEach(btn => {
        btn.addEventListener('click', event => {
            event.stopPropagation();
            if (fileInput) fileInput.click();
        });
    });

    panel.querySelectorAll('[data-skip-photo]').forEach(btn => {
        btn.addEventListener('click', event => {
            event.stopPropagation();
            setStep('split', { focusTicketNumber: true });
        });
    });

    if (dropzone) {
        dropzone.addEventListener('click', event => {
            if (event.target.closest('button')) return;
            if (fileInput) fileInput.click();
        });
    }

    const bindDropTarget = target => {
        if (!target || isReadOnly) return;
        target.addEventListener('dragover', event => {
            event.preventDefault();
            target.classList.add('is-dragover');
        });
        target.addEventListener('dragleave', () => {
            target.classList.remove('is-dragover');
        });
        target.addEventListener('drop', event => {
            event.preventDefault();
            target.classList.remove('is-dragover');
            const dropped = event.dataTransfer?.files?.[0];
            if (dropped) assignFileToInput(dropped);
        });
    };
    bindDropTarget(dropzone);
    bindDropTarget(stage);

    if (!isReadOnly) {
        document.addEventListener('paste', event => {
            const items = event.clipboardData?.items;
            if (!items) return;
            for (const item of items) {
                if (item.type && item.type.startsWith('image/')) {
                    const pastedFile = item.getAsFile();
                    if (pastedFile) {
                        event.preventDefault();
                        assignFileToInput(pastedFile);
                    }
                    break;
                }
            }
        });
    }

    // Close on backdrop click or Escape when viewing or on the upload gate
    if (backdrop) {
        backdrop.addEventListener('click', () => {
            if (isReadOnly || panel.dataset.ticketStep === 'upload') {
                closeModal();
            }
        });
    }

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        if (document.querySelector('.swal2-container.swal2-shown')) return;
        if (document.querySelector('.modern-select-dropdown.show')) return;
        if (isReadOnly || panel.dataset.ticketStep === 'upload') {
            closeModal();
        }
    });

    // Keep ModernSelect dropdowns aligned if the lifted form scrolls
    if (entryMain) entryMain.addEventListener('scroll', closeModernSelects, { passive: true });
    formEl.addEventListener('scroll', closeModernSelects, { passive: true });

    // Image toolbar controls
    const zoomInBtn = panel.querySelector('[data-image-zoom-in]');
    const zoomOutBtn = panel.querySelector('[data-image-zoom-out]');
    const rotateBtn = panel.querySelector('[data-image-rotate]');
    const resetBtn = panel.querySelector('[data-image-reset]');
    const fullscreenBtn = panel.querySelector('[data-image-fullscreen]');
    const collapseBtn = panel.querySelector('[data-image-toggle]');

    if (zoomInBtn) {
        zoomInBtn.addEventListener('click', () => {
            if (!activeUrl) return;
            scale = Math.min(4, +(scale + 0.25).toFixed(2));
            applyTransform();
        });
    }
    if (zoomOutBtn) {
        zoomOutBtn.addEventListener('click', () => {
            if (!activeUrl) return;
            scale = Math.max(0.5, +(scale - 0.25).toFixed(2));
            if (scale <= 1) {
                panX = 0;
                panY = 0;
            }
            applyTransform();
        });
    }
    if (rotateBtn) {
        rotateBtn.addEventListener('click', () => {
            if (!activeUrl) return;
            rotation = (rotation + 90) % 360;
            applyTransform();
        });
    }
    if (resetBtn) {
        resetBtn.addEventListener('click', () => resetTransform());
    }
    if (fullscreenBtn) {
        fullscreenBtn.addEventListener('click', () => {
            if (!activeUrl) return;
            ModernAlert.image(activeUrl, 'Dispatch Ticket Image Preview', { rotation });
        });
    }
    if (collapseBtn && workspace) {
        collapseBtn.addEventListener('click', () => {
            const collapsed = workspace.dataset.imageCollapsed === 'true';
            const next = !collapsed;
            workspace.dataset.imageCollapsed = next ? 'true' : 'false';
            collapseBtn.setAttribute('aria-expanded', next ? 'false' : 'true');
            collapseBtn.title = next ? 'Show photo' : 'Hide photo';
            collapseBtn.setAttribute('aria-label', next ? 'Show photo' : 'Hide photo');
            const icon = collapseBtn.querySelector('.material-symbols-outlined');
            if (icon) icon.textContent = next ? 'expand_more' : 'expand_less';
        });
    }

    // Pointer pan + wheel zoom + double-click zoom on stage
    if (stage) {
        let dragging = false;
        let pointerId = null;
        let startX = 0;
        let startY = 0;

        stage.addEventListener('pointerdown', event => {
            if (!activeUrl || scale <= 1 || event.button !== 0) return;
            dragging = true;
            pointerId = event.pointerId;
            startX = event.clientX - panX;
            startY = event.clientY - panY;
            stage.classList.add('is-dragging');
            try { stage.setPointerCapture(pointerId); } catch { /* ignore */ }
        });

        stage.addEventListener('pointermove', event => {
            if (!dragging || event.pointerId !== pointerId) return;
            panX = event.clientX - startX;
            panY = event.clientY - startY;
            applyTransform();
        });

        const stopDrag = event => {
            if (!dragging || (pointerId !== null && event.pointerId !== pointerId)) return;
            dragging = false;
            stage.classList.remove('is-dragging');
            try { stage.releasePointerCapture(pointerId); } catch { /* ignore */ }
            pointerId = null;
        };

        stage.addEventListener('pointerup', stopDrag);
        stage.addEventListener('pointercancel', stopDrag);

        stage.addEventListener('wheel', event => {
            if (!activeUrl) return;
            event.preventDefault();
            const delta = event.deltaY > 0 ? -0.15 : 0.15;
            scale = Math.min(4, Math.max(0.5, +(scale + delta).toFixed(2)));
            if (scale <= 1) {
                panX = 0;
                panY = 0;
            }
            applyTransform();
        }, { passive: false });

        stage.addEventListener('dblclick', () => {
            if (!activeUrl) return;
            if (scale > 1) {
                scale = 1;
                panX = 0;
                panY = 0;
            } else {
                scale = 2;
            }
            applyTransform();
        });
    }

    window.addEventListener('beforeunload', () => {
        if (blobUrl) {
            URL.revokeObjectURL(blobUrl);
            blobUrl = null;
        }
    });

    // Lock body scroll and initialize modal step
    document.body.classList.add('cos-ticket-focus-open');
    const initialStep = isNewTicket && !activeUrl ? 'upload' : 'split';
    setStep(initialStep);
};

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeCosTicketEntry);
} else {
    initializeCosTicketEntry();
}
