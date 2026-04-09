/**
 * Storage Migration Utility
 * Cleans up deprecated localStorage keys from old token strategies
 * 
 * CURRENT STRATEGY (2026-04-08):
 * - Both accessToken and refreshToken stored in localStorage for simplicity
 * - Keys: 'tts_access_token' and 'tts_refresh_token' are VALID
 * 
 * Run once on app initialization to clean up truly deprecated keys
 */

const DEPRECATED_KEYS = [
    // No deprecated keys currently - both tokens are stored in localStorage
];

/**
 * Remove deprecated storage keys
 */
export const migrateStorage = () => {
    try {
        let removedCount = 0;

        DEPRECATED_KEYS.forEach(key => {
            if (localStorage.getItem(key) !== null) {
                localStorage.removeItem(key);
                removedCount++;
                console.log(`🧹 Removed deprecated storage key: ${key}`);
            }
        });

        if (removedCount > 0) {
            console.log(`✅ Storage migration complete: ${removedCount} deprecated keys removed`);
        }

        return removedCount;
    } catch (error) {
        console.error('❌ Storage migration failed:', error);
        return 0;
    }
};
