/**
 * Utility script to clear localStorage and sessionStorage
 * Run this in browser console if localStorage quota issues persist
 */

console.log('🧹 Clearing all storage...');

// Show current storage usage
console.log('📊 Current localStorage usage:');
for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i);
    const value = localStorage.getItem(key);
    console.log(`  ${key}: ${(value?.length || 0)} characters`);
}

// Clear all localStorage
localStorage.clear();
console.log('✅ localStorage cleared');

// Clear all sessionStorage
sessionStorage.clear();
console.log('✅ sessionStorage cleared');

// Clear IndexedDB (if any)
if ('indexedDB' in window) {
    indexedDB.databases().then(databases => {
        databases.forEach(db => {
            console.log(`🗑️ Clearing IndexedDB: ${db.name}`);
            indexedDB.deleteDatabase(db.name);
        });
    });
}

console.log('🎉 All storage cleared! Please refresh the page.');