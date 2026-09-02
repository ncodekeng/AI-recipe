const PREFERENCES_KEY = 'plate.preferences.v1'
const CLIENT_ID_KEY = 'plate.client-id'
const KITCHEN_MEMORY_KEY = 'plate.kitchen-memory.v1'
const MAX_KITCHEN_ITEMS = 50

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

function sanitizeKitchenIngredient(item) {
  if (!item || typeof item.name !== 'string' || !item.name.trim()) return null

  return {
    id: typeof item.id === 'string' && item.id ? item.id : crypto.randomUUID(),
    name: item.name.trim().slice(0, 100),
    quantity: typeof item.quantity === 'string' && item.quantity.trim()
      ? item.quantity.trim().slice(0, 80)
      : 'quantity unknown',
    confidence: Number.isFinite(Number(item.confidence))
      ? Math.max(0, Math.min(100, Math.round(Number(item.confidence))))
      : 0,
    sourceImage: typeof item.sourceImage === 'string' && item.sourceImage.trim()
      ? item.sourceImage.trim().slice(0, 120)
      : 'Kitchen Memory',
    kind: item.kind === 'Frozen meal' ? 'Frozen meal' : 'Ingredient',
  }
}

export function loadKitchenMemory() {
  try {
    const value = JSON.parse(localStorage.getItem(KITCHEN_MEMORY_KEY))
    return Array.isArray(value)
      ? value.map(sanitizeKitchenIngredient).filter(Boolean).slice(0, MAX_KITCHEN_ITEMS)
      : []
  } catch {
    return []
  }
}

export function saveKitchenMemory(ingredients) {
  const sanitized = Array.isArray(ingredients)
    ? ingredients.map(sanitizeKitchenIngredient).filter(Boolean).slice(0, MAX_KITCHEN_ITEMS)
    : []
  try {
    localStorage.setItem(KITCHEN_MEMORY_KEY, JSON.stringify(sanitized))
  } catch {
    // The current session still works when browser storage is unavailable.
  }
  return sanitized
}

export function mergeKitchenMemory(current, detected) {
  const merged = loadSafeIngredients(current)
  const indexes = new Map(merged.map((item, index) => [item.name.toLowerCase(), index]))

  for (const candidate of loadSafeIngredients(detected)) {
    const key = candidate.name.toLowerCase()
    const existingIndex = indexes.get(key)
    if (existingIndex === undefined) {
      indexes.set(key, merged.length)
      merged.push(candidate)
    } else {
      merged[existingIndex] = candidate
    }
  }

  return merged.slice(0, MAX_KITCHEN_ITEMS)
}

function loadSafeIngredients(value) {
  return Array.isArray(value) ? value.map(sanitizeKitchenIngredient).filter(Boolean) : []
}

export function clearLocalData() {
  try {
    localStorage.removeItem(PREFERENCES_KEY)
    localStorage.removeItem(CLIENT_ID_KEY)
    localStorage.removeItem('plate.saved.v1')
    localStorage.removeItem('plate.history.v1')
    localStorage.removeItem(KITCHEN_MEMORY_KEY)
  } catch {
    // There is nothing else to clear when browser storage is unavailable.
  }
}
