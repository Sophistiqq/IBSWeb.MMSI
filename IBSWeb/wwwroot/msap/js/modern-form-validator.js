const ModernFormValidator = {
    validate: function (formId) {
        const form = document.getElementById(formId);
        if (!form) return true;
        if (!form.checkValidity()) {
            form.reportValidity();
            return false;
        }
        return true;
    },

    setCustomValidity: function (elementId, message) {
        const el = document.getElementById(elementId);
        if (el) {
            el.setCustomValidity(message);
            el.checkValidity();
        }
    },

    clearCustomValidity: function (elementId) {
        const el = document.getElementById(elementId);
        if (el) {
            el.setCustomValidity('');
        }
    }
};
