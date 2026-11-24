/**
 * ModernAlert Utility
 * MSAP-native dialog, notification, prompt, and media preview wrapper for SweetAlert2.
 * Uses MSAP design tokens and classes from modern-ui.css.
 */

const ModernAlert = {
    _iconConfigs: {
        success: { icon: 'check_circle', tone: 'success' },
        error: { icon: 'error', tone: 'error' },
        warning: { icon: 'warning', tone: 'warning' },
        info: { icon: 'info', tone: 'info' },
        question: { icon: 'help', tone: 'primary' }
    },

    _sizeWidths: {
        sm: 'min(420px, calc(100vw - 2rem))',
        md: 'min(540px, calc(100vw - 2rem))',
        lg: 'min(720px, calc(100vw - 2rem))'
    },

    _escapeHtml: function(value) {
        if (value === null || value === undefined) return '';
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    },

    _getIconBadge: function(type) {
        const config = this._iconConfigs[type] || this._iconConfigs.info;
        return `
            <div class="modern-swal-icon modern-swal-icon--${config.tone}" aria-hidden="true">
                <span class="material-symbols-outlined">${config.icon}</span>
            </div>
        `;
    },

    _renderHeader: function(type, title, subtitle = '') {
        const safeSubtitle = subtitle
            ? `<p class="modern-swal-subtitle">${subtitle}</p>`
            : '';

        return `
            <div class="modern-swal-header">
                ${this._getIconBadge(type)}
                <div class="modern-swal-header-text">
                    <h2 class="modern-swal-title">${title}</h2>
                    ${safeSubtitle}
                </div>
            </div>
        `;
    },

    _renderDetails: function(details) {
        if (!Array.isArray(details) || details.length === 0) return '';

        const rows = details
            .filter(item => item && (item.label || item.value !== undefined))
            .map(item => `
                <div class="modern-swal-detail-row">
                    <dt class="modern-swal-detail-label">${this._escapeHtml(item.label ?? '')}</dt>
                    <dd class="modern-swal-detail-value">${item.html ? item.value : this._escapeHtml(item.value ?? '')}</dd>
                </div>
            `)
            .join('');

        return rows ? `<dl class="modern-swal-details">${rows}</dl>` : '';
    },

    _renderCallout: function(callout) {
        if (!callout) return '';
        const config = typeof callout === 'string'
            ? { text: callout, tone: 'info' }
            : callout;
        if (!config.text) return '';

        const tone = config.tone || 'info';
        const iconMap = {
            info: 'info',
            warning: 'warning',
            error: 'error',
            success: 'check_circle'
        };
        const icon = iconMap[tone] || 'info';

        return `
            <div class="modern-swal-callout modern-swal-callout--${tone}">
                <span class="material-symbols-outlined modern-swal-callout-icon" aria-hidden="true">${icon}</span>
                <div class="modern-swal-callout-text">${config.text}</div>
            </div>
        `;
    },

    _resolveWidth: function({ width, size, content = '', details = null, callout = null } = {}) {
        if (width) return width;
        if (size && this._sizeWidths[size]) return this._sizeWidths[size];

        const raw = String(content || '');
        if (/<table|<form|schedule-batch-preview/i.test(raw)) {
            return this._sizeWidths.lg;
        }
        if (
            (Array.isArray(details) && details.length > 0) ||
            callout ||
            /<ul|<ol|<dl|schedule-conflict-details/i.test(raw) ||
            raw.length > 140
        ) {
            return this._sizeWidths.md;
        }
        return this._sizeWidths.sm;
    },

    _getConfirmButtonClass: function(variant = 'primary') {
        if (variant === 'error') return 'modern-swal-confirm modern-swal-confirm--error swal2-confirm-btn';
        if (variant === 'warning') return 'modern-swal-confirm modern-swal-confirm--warning swal2-confirm-btn';
        return 'modern-swal-confirm swal2-confirm-btn';
    },

    // Base configuration for MSAP dialogs
    _baseConfig: {
        customClass: {
            popup: 'modern-swal-popup',
            title: 'modern-swal-title-hidden',
            htmlContainer: 'modern-swal-html',
            confirmButton: 'modern-swal-confirm swal2-confirm-btn',
            cancelButton: 'modern-swal-cancel swal2-cancel-btn',
            actions: 'modern-swal-actions',
            closeButton: 'modern-swal-close',
            validationMessage: 'modern-swal-validation'
        },
        didOpen: function(popup) {
            $(popup).find('.swal2-confirm').attr('data-testid', 'swal-confirm-btn');
            $(popup).find('.swal2-cancel').attr('data-testid', 'swal-cancel-btn');
        },
        buttonsStyling: false,
        showCloseButton: false,
        allowOutsideClick: true,
        width: 'min(420px, calc(100vw - 2rem))',
        padding: '0',
        reverseButtons: true,
        showClass: {
            popup: 'animate__animated animate__fadeInUp animate__faster'
        },
        hideClass: {
            popup: 'animate__animated animate__fadeOutDown animate__faster'
        }
    },

    _fireNotice: function(type, message, title, extra = {}) {
        const defaultConfirmLabels = {
            success: 'Done',
            error: 'Close',
            warning: 'Got it',
            info: 'Got it'
        };
        const width = this._resolveWidth({
            width: extra.width,
            size: extra.size,
            content: message,
            details: extra.details,
            callout: extra.callout
        });

        const bodyParts = [
            message ? `<div class="modern-swal-message">${message}</div>` : '',
            this._renderDetails(extra.details),
            this._renderCallout(extra.callout)
        ].filter(Boolean).join('');

        return Swal.fire({
            ...this._baseConfig,
            width,
            titleText: title,
            html: `
                ${this._renderHeader(type, title, extra.subtitle)}
                <div class="modern-swal-body">
                    ${bodyParts}
                </div>
            `,
            confirmButtonText: extra.confirmText || defaultConfirmLabels[type] || 'Got it',
            ...(extra.timer ? { timer: extra.timer } : {})
        });
    },

    /**
     * Show a success alert
     */
    success: function(message, title = 'Success', options = {}) {
        return this._fireNotice('success', message, title, options);
    },

    /**
     * Show an error alert
     */
    error: function(message, title = 'Error', options = {}) {
        return this._fireNotice('error', message, title, options);
    },

    /**
     * Show a warning alert
     */
    warning: function(message, title = 'Warning', options = {}) {
        return this._fireNotice('warning', message, title, options);
    },

    /**
     * Show an info alert
     */
    info: function(message, title = 'Info', options = {}) {
        const hasRichInfo = Boolean(
            options?.details?.length ||
            options?.callout ||
            (message && String(message).length > 140) ||
            /<table|<ul|<ol|<dl/i.test(String(message || ''))
        );
        return this._fireNotice('info', message, title, {
            timer: hasRichInfo ? undefined : 2500,
            ...options
        });
    },

    /**
     * Show a confirmation dialog
     */
    confirm: function({
        title = 'Confirmation',
        subtitle = '',
        text = 'Are you sure you want to proceed?',
        html = null,
        details = null,
        callout = null,
        confirmText = 'Confirm',
        cancelText = 'Cancel',
        confirmVariant = 'primary',
        showCancelButton = true,
        icon = 'question',
        size,
        width,
        didOpen,
        preConfirm
    } = {}) {
        const rawContent = html ?? text;
        const resolvedWidth = this._resolveWidth({
            width,
            size,
            content: rawContent,
            details,
            callout
        });

        const bodyContent = html
            ? `<div class="modern-swal-rich">${html}</div>`
            : `<div class="modern-swal-message">${text}</div>`;

        const detailsHtml = this._renderDetails(details);
        const calloutHtml = this._renderCallout(callout);

        return Swal.fire({
            ...this._baseConfig,
            customClass: {
                ...this._baseConfig.customClass,
                confirmButton: this._getConfirmButtonClass(confirmVariant)
            },
            width: resolvedWidth,
            titleText: title,
            html: `
                ${this._renderHeader(icon, title, subtitle)}
                <div class="modern-swal-body">
                    ${bodyContent}
                    ${detailsHtml}
                    ${calloutHtml}
                </div>
            `,
            showCancelButton: showCancelButton,
            confirmButtonText: confirmText,
            cancelButtonText: cancelText,
            didOpen: popup => {
                this._baseConfig.didOpen?.(popup);
                didOpen?.(popup);
            },
            preConfirm
        });
    },

    /**
     * Show a simple alert
     */
    alert: function(title, text, icon = 'info', options = {}) {
        return this._fireNotice(icon, text, title, options);
    },

    /**
     * Show a loading state
     */
    showLoading: function(title = 'Processing...', message = 'Please wait while we process your request.') {
        Swal.fire({
            ...this._baseConfig,
            customClass: {
                ...this._baseConfig.customClass,
                actions: 'modern-swal-actions modern-swal-actions--hidden'
            },
            width: this._sizeWidths.sm,
            titleText: title,
            html: `
                <div class="modern-swal-loading">
                    <div class="modern-loader-inline"></div>
                    <div class="modern-swal-loading-text">
                        <h2 class="modern-swal-title">${title}</h2>
                        <p class="modern-swal-message">${message}</p>
                    </div>
                </div>
            `,
            allowOutsideClick: false,
            showConfirmButton: false
        });
    },

    /**
     * Show an image preview with zoom and pan controls
     */
    image: function(imageUrl, title = 'Image Preview', options = {}) {
        return Swal.fire({
            ...this._baseConfig,
            customClass: {
                ...this._baseConfig.customClass,
                popup: 'modern-swal-popup modern-swal-popup--media'
            },
            titleText: title,
            html: `
                <div class="modern-swal-media-header">
                    <div class="modern-swal-header-text">
                        <h2 class="modern-swal-title">${title}</h2>
                    </div>
                </div>
                <div class="modern-image-viewer-container">
                    <div class="modern-image-viewer-toolbar">
                        <button type="button" class="modern-image-viewer-btn zoom-in" title="Zoom In"><span class="material-symbols-outlined">zoom_in</span></button>
                        <button type="button" class="modern-image-viewer-btn zoom-out" title="Zoom Out"><span class="material-symbols-outlined">zoom_out</span></button>
                        <button type="button" class="modern-image-viewer-btn rotate-right" title="Rotate 90°"><span class="material-symbols-outlined">rotate_right</span></button>
                        <button type="button" class="modern-image-viewer-btn reset" title="Reset"><span class="material-symbols-outlined">restart_alt</span></button>
                        <button type="button" class="modern-image-viewer-btn modern-image-viewer-btn--close close-viewer" title="Close"><span class="material-symbols-outlined">close</span></button>
                    </div>
                    <div class="modern-image-viewer-wrapper">
                        <img src="${imageUrl}" class="modern-image-viewer-img" alt="${this._escapeHtml(title)}" />
                    </div>
                    <div class="modern-image-viewer-hint">
                        Scroll to Zoom • Drag to Pan • Close to Exit
                    </div>
                </div>
            `,
            showConfirmButton: false,
            showCloseButton: false,
            width: 'min(92vw, 1120px)',
            padding: '0',
            didOpen: () => {
                const img = document.querySelector('.modern-image-viewer-img');
                const wrapper = document.querySelector('.modern-image-viewer-wrapper');
                let scale = 1;
                let rotation = Number.isFinite(options?.rotation) ? options.rotation : 0;
                let isDragging = false;
                let startX, startY, translateX = 0, translateY = 0;

                const updateTransform = () => {
                    img.style.transform = `translate(${translateX}px, ${translateY}px) scale(${scale}) rotate(${rotation}deg)`;
                };
                updateTransform();

                document.querySelector('.zoom-in').onclick = (e) => { e.stopPropagation(); scale += 0.3; updateTransform(); };
                document.querySelector('.zoom-out').onclick = (e) => { e.stopPropagation(); if (scale > 0.4) scale -= 0.3; updateTransform(); };
                document.querySelector('.rotate-right').onclick = (e) => { e.stopPropagation(); rotation = (rotation + 90) % 360; updateTransform(); };
                document.querySelector('.reset').onclick = (e) => { e.stopPropagation(); scale = 1; rotation = 0; translateX = 0; translateY = 0; updateTransform(); };
                document.querySelector('.close-viewer').onclick = (e) => { e.stopPropagation(); Swal.close(); };

                wrapper.onmousedown = (e) => {
                    if (e.button !== 0) return;
                    isDragging = true;
                    startX = e.clientX - translateX;
                    startY = e.clientY - translateY;
                    wrapper.style.cursor = 'grabbing';
                };

                const onMouseMove = (e) => {
                    if (!isDragging) return;
                    translateX = e.clientX - startX;
                    translateY = e.clientY - startY;
                    updateTransform();
                };

                const onMouseUp = () => {
                    isDragging = false;
                    wrapper.style.cursor = 'grab';
                };

                window.addEventListener('mousemove', onMouseMove);
                window.addEventListener('mouseup', onMouseUp);

                wrapper.onwheel = (e) => {
                    e.preventDefault();
                    const delta = e.deltaY > 0 ? -0.15 : 0.15;
                    scale = Math.min(Math.max(0.3, scale + delta), 8);
                    updateTransform();
                };

                Swal.getPopup().addEventListener('remove', () => {
                    window.removeEventListener('mousemove', onMouseMove);
                    window.removeEventListener('mouseup', onMouseUp);
                });
            }
        });
    },

    /**
     * Show a video preview
     */
    video: function(videoUrl, title = 'Video Preview') {
        return Swal.fire({
            ...this._baseConfig,
            customClass: {
                ...this._baseConfig.customClass,
                popup: 'modern-swal-popup modern-swal-popup--media'
            },
            titleText: title,
            html: `
                <div class="modern-swal-media-header">
                    <div class="modern-swal-header-text">
                        <h2 class="modern-swal-title">${title}</h2>
                    </div>
                </div>
                <div class="modern-swal-video-wrap">
                    <video width="100%" height="auto" controls autoplay class="modern-swal-video">
                        <source src="${videoUrl}" type="video/mp4">
                        Your browser does not support the video tag.
                    </video>
                </div>
            `,
            showConfirmButton: false,
            showCloseButton: true,
            width: 'min(92vw, 640px)',
            padding: '0'
        });
    },

    /**
     * Show a prompt (input) dialog
     */
    prompt: function({
        title = 'Input Required',
        subtitle = '',
        text = 'Please enter a value:',
        placeholder = '',
        confirmText = 'Submit',
        cancelText = 'Cancel',
        confirmVariant = 'primary',
        icon = 'question',
        validator = (value) => value ? null : 'Input is required'
    } = {}) {
        return Swal.fire({
            ...this._baseConfig,
            customClass: {
                ...this._baseConfig.customClass,
                confirmButton: this._getConfirmButtonClass(confirmVariant),
                input: 'modern-swal-input'
            },
            width: this._sizeWidths.md,
            titleText: title,
            html: `
                ${this._renderHeader(icon, title, subtitle)}
                <div class="modern-swal-body">
                    <div class="modern-swal-message">${text}</div>
                </div>
            `,
            input: 'text',
            inputPlaceholder: placeholder,
            showCancelButton: true,
            confirmButtonText: confirmText,
            cancelButtonText: cancelText,
            inputValidator: validator
        });
    },

    /**
     * Show a prompt dialog with reason and optional/required authorization document upload
     */
    promptAdjustment: function({
        title = 'Authorization Required',
        subtitle = '',
        text = 'Please provide a reason:',
        placeholder = 'Reason for adjustment',
        confirmText = 'Submit',
        cancelText = 'Cancel',
        confirmVariant = 'primary',
        icon = 'warning',
        requireAttachment = false,
        minReasonLength = 5,
        maxReasonLength = 500
    } = {}) {
        const attachmentHtml = requireAttachment
            ? `
                <div class="modern-swal-field">
                    <label for="swalAdjustmentFile" class="modern-swal-field-label">
                        Approved Authorization Document <span class="modern-swal-required">*</span>
                    </label>
                    <input id="swalAdjustmentFile" type="file" accept=".pdf,.png,.jpg,.jpeg" class="modern-swal-input modern-swal-file" />
                    <div class="modern-swal-field-hint">PDF, PNG, or JPEG (max 10 MB)</div>
                </div>
            `
            : '';

        return Swal.fire({
            ...this._baseConfig,
            customClass: {
                ...this._baseConfig.customClass,
                confirmButton: this._getConfirmButtonClass(confirmVariant)
            },
            width: this._sizeWidths.md,
            titleText: title,
            html: `
                ${this._renderHeader(icon, title, subtitle)}
                <div class="modern-swal-body">
                    <div class="modern-swal-message">${text}</div>
                    <div class="modern-swal-field">
                        <label for="swalAdjustmentReason" class="modern-swal-field-label">
                            Reason <span class="modern-swal-required">*</span>
                        </label>
                        <textarea id="swalAdjustmentReason" rows="3" maxlength="${maxReasonLength}" placeholder="${placeholder}" class="modern-swal-input modern-swal-textarea"></textarea>
                    </div>
                    ${attachmentHtml}
                </div>
            `,
            showCancelButton: true,
            confirmButtonText: confirmText,
            cancelButtonText: cancelText,
            preConfirm: () => {
                const reasonEl = document.getElementById('swalAdjustmentReason');
                const reason = reasonEl ? reasonEl.value.trim() : '';
                if (!reason || reason.length < minReasonLength || reason.length > maxReasonLength) {
                    Swal.showValidationMessage(`Enter a reason between ${minReasonLength} and ${maxReasonLength} characters.`);
                    return false;
                }

                let file = null;
                if (requireAttachment) {
                    const fileEl = document.getElementById('swalAdjustmentFile');
                    file = fileEl && fileEl.files && fileEl.files.length > 0 ? fileEl.files[0] : null;
                    if (!file) {
                        Swal.showValidationMessage('Upload an approved authorization document (PDF, PNG, or JPEG).');
                        return false;
                    }
                    const allowed = ['.pdf', '.png', '.jpg', '.jpeg'];
                    const lowerName = file.name.toLowerCase();
                    if (!allowed.some(ext => lowerName.endsWith(ext))) {
                        Swal.showValidationMessage('Only PDF, PNG, or JPEG files are supported.');
                        return false;
                    }
                    if (file.size <= 0 || file.size > 10 * 1024 * 1024) {
                        Swal.showValidationMessage('Authorization document must be 10 MB or smaller.');
                        return false;
                    }
                }

                return { reason, file };
            }
        });
    },

    /**
     * Hide current SweetAlert
     */
    close: function() {
        Swal.close();
    }
};

/**
 * Global Compatibility Layer
 */
window.toast = {
    success: (msg) => ModernAlert.success(msg),
    error: (msg) => ModernAlert.error(msg),
    warning: (msg) => ModernAlert.warning(msg),
    info: (msg) => ModernAlert.info(msg)
};
window.toastr = window.toast;
window.swal = (title, text, icon) => (typeof title === 'object') ? Swal.fire(title) : ModernAlert.alert(title, text, icon);
window.swalConfirm = (title, text, callback) => {
    ModernAlert.confirm({ title, text }).then(result => {
        if (result.isConfirmed && typeof callback === 'function') callback();
    });
};
window.modernAlert = ModernAlert;
