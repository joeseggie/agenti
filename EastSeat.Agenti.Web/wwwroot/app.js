// Clipboard utility function
window.copyToClipboard = async function(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch (err) {
        console.error('Failed to copy to clipboard:', err);
        return false;
    }
};

// Form submission utility
window.submitForm = function(formId) {
    const form = document.getElementById(formId);
    if (form) {
        form.submit();
    } else {
        console.error('submitForm: form not found:', formId);
    }
};

// Theme detection utilities
window.themeHelpers = {
    // Get current system theme preference
    getSystemThemePreference: function() {
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
            return 'dark';
        }
        return 'light';
    },

    // Listen for system theme changes
    watchSystemThemePreference: function(dotnetHelper) {
        if (!window.matchMedia) throw new Error('System theme observation is unavailable.');

        const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

        const handler = (e) => {
            const newTheme = e.matches ? 'dark' : 'light';
            dotnetHelper.invokeMethodAsync('OnSystemThemeChanged', newTheme)
                .catch(() => console.warn('System theme update could not reach the application.'));
        };

        // Modern browsers
        if (mediaQuery.addEventListener) {
            mediaQuery.addEventListener('change', handler);
        } else {
            // Fallback for older browsers
            mediaQuery.addListener(handler);
        }

        return {
            dispose: function() {
                if (mediaQuery.removeEventListener) {
                    mediaQuery.removeEventListener('change', handler);
                } else {
                    mediaQuery.removeListener(handler);
                }
            }
        };
    }
};
