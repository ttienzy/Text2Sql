/**
 * Storage Migration Utility
 * Cleans up deprecated localStorage keys from old token strategy
 * 
 * Run once on app initialization to remove tts_access_token
 * (accessToken is now memory-only per CRIT-4 security requirement)
 */

const DEPRECATED_KEYS = [
    'tts_access_token', // Removed: accessToken is now memory-only
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
