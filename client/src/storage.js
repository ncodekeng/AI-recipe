const PREFERENCES_KEY = 'plate.preferences.v1'
const CLIENT_ID_KEY = 'plate.client-id'

export function loadPreferences() {
  try {
    const value = JSON.parse(localStorage.getItem(PREFERENCES_KEY))
    return value && typeof value === 'object' ? value : {}
  } catch {
    return {}
  }
}

export function savePreferences(preferences) {
  try {
    localStorage.setItem(PREFERENCES_KEY, JSON.stringify(preferences))
  } catch {
    // Private browsing and full storage can reject writes; the app still works in memory.
  }
}

export function clearLocalData() {
  try {
    localStorage.removeItem(PREFERENCES_KEY)
    localStorage.removeItem(CLIENT_ID_KEY)
  } catch {
    // There is nothing else to clear when browser storage is unavailable.
  }
}
