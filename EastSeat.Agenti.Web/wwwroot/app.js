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
        if (!window.matchMedia) return;

        const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

        const handler = (e) => {
            const newTheme = e.matches ? 'dark' : 'light';
            dotnetHelper.invokeMethodAsync('OnSystemThemeChanged', newTheme);
        };

        // Modern browsers
        if (mediaQuery.addEventListener) {
            mediaQuery.addEventListener('change', handler);
        } else {
            // Fallback for older browsers
            mediaQuery.addListener(handler);
        }
    }
};
