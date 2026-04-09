/**
 * Storage utilities for handling localStorage quota and cleanup
 */

/**
 * Check localStorage usage and available space
 */
export const getStorageInfo = () => {
    try {
        const used = new Blob(Object.values(localStorage)).size;
        const available = 5 * 1024 * 1024; // Approximate 5MB limit
        const usagePercent = (used / available) * 100;

        return {
            used,
            available,
            usagePercent,
            isNearLimit: usagePercent > 80,
        };
    } catch (error) {
        console.error('Error checking storage info:', error);
        return {
            used: 0,
            available: 0,
            usagePercent: 0,
            isNearLimit: false,
        };
    }
};

/**
 * Clear specific localStorage keys to free up space
 */
export const clearStorageKeys = (keys = []) => {
    try {
        keys.forEach(key => {
            localStorage.removeItem(key);
            console.log(`✅ Cleared localStorage key: ${key}`);
        });
        return true;
    } catch (error) {
        console.error('❌ Error clearing storage keys:', error);
        return false;
    }
};

/**
 * Clear all non-essential localStorage data
 */
export const clearNonEssentialStorage = () => {
    try {
        const essentialKeys = [
            'tts_refresh_token',
            'app_theme',
            'app_language',
        ];

        const keysToRemove = [];
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && !essentialKeys.includes(key)) {
                keysToRemove.push(key);
            }
        }

        keysToRemove.forEach(key => localStorage.removeItem(key));

        console.log(`✅ Cleared ${keysToRemove.length} non-essential storage keys`);
        return keysToRemove.length;
    } catch (error) {
        console.error('❌ Error clearing non-essential storage:', error);
        return 0;
    }
};

/**
 * Handle QuotaExceededError with automatic cleanup
 */
export const handleQuotaExceeded = (callback) => {
    try {
        return callback();
    } catch (error) {
        if (error.name === 'QuotaExceededError') {
            console.warn('⚠️ LocalStorage quota exceeded, attempting cleanup...');

            // Clear non-essential data
            const clearedCount = clearNonEssentialStorage();

            if (clearedCount > 0) {
                console.log(`✅ Cleared ${clearedCount} items, retrying operation...`);
                try {
                    return callback();
                } catch (retryError) {
                    console.error('❌ Operation failed even after cleanup:', retryError);
                    throw retryError;
                }
            } else {
                console.error('❌ No items to clear, quota still exceeded');
                throw error;
            }
        } else {
            throw error;
        }
    }
};

/**
 * Safe localStorage setItem with quota handling
 */
export const safeSetItem = (key, value) => {
    return handleQuotaExceeded(() => {
        localStorage.setItem(key, value);
        return true;
    });
};

/**
 * Monitor storage usage and warn when approaching limit
 */
export const monitorStorageUsage = () => {
    const info = getStorageInfo();

    if (info.isNearLimit) {
        console.warn(`⚠️ LocalStorage usage at ${info.usagePercent.toFixed(1)}% (${(info.used / 1024).toFixed(1)}KB used)`);
        return {
            shouldWarn: true,
            message: `Storage is ${info.usagePercent.toFixed(1)}% full. Consider clearing old data.`,
            ...info,
        };
    }

    return {
        shouldWarn: false,
        ...info,
    };
};