const PREFERENCES_KEY = 'plate.preferences.v1'
const CLIENT_ID_KEY = 'plate.client-id'
const KITCHEN_MEMORY_KEY = 'plate.kitchen-memory.v1'
const MAX_KITCHEN_ITEMS = 100
const INGREDIENT_IDENTITY_NOISE_WORDS = new Set([
  'big', 'bigger', 'block', 'bottle', 'bottles', 'box', 'fresh', 'jar', 'large',
  'larger', 'medium', 'of', 'pack', 'package', 'packaged', 'piece', 'pieces', 'raw',
  'slice', 'slices', 'sliced', 'small', 'smaller', 'tub', 'wheel',
])
const EGG_IDENTITY_NOISE_WORDS = new Set(['brown', 'free', 'range', 'white'])

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
      ? deduplicateKitchenIngredients(value).slice(0, MAX_KITCHEN_ITEMS)
      : []
  } catch {
    return []
  }
}

export function saveKitchenMemory(ingredients) {
  const sanitized = Array.isArray(ingredients)
    ? deduplicateKitchenIngredients(ingredients).slice(0, MAX_KITCHEN_ITEMS)
    : []
  try {
    localStorage.setItem(KITCHEN_MEMORY_KEY, JSON.stringify(sanitized))
  } catch {
    // The current session still works when browser storage is unavailable.
  }
  return sanitized
}

export function mergeKitchenMemory(current, detected) {
  const merged = deduplicateKitchenIngredients(current)
  const indexes = new Map(merged.map((item, index) => [ingredientIdentity(item.name), index]))

  for (const candidate of loadSafeIngredients(detected)) {
    const key = ingredientIdentity(candidate.name)
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

function deduplicateKitchenIngredients(value) {
  const deduplicated = []
  const indexes = new Map()

  for (const ingredient of loadSafeIngredients(value)) {
    const key = ingredientIdentity(ingredient.name)
    const existingIndex = indexes.get(key)
    if (existingIndex === undefined) {
      indexes.set(key, deduplicated.length)
      deduplicated.push(ingredient)
    } else {
      deduplicated[existingIndex] = ingredient
    }
  }

  return deduplicated
}

function ingredientIdentity(name) {
  const originalTokens = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
    .split(/\s+/)
    .filter(Boolean)
  if (!originalTokens.length) return name.trim().toLowerCase()

  let tokens = originalTokens.filter((token) => !INGREDIENT_IDENTITY_NOISE_WORDS.has(token))
  if (!tokens.length) tokens = originalTokens
  tokens[tokens.length - 1] = singularize(tokens[tokens.length - 1])

  if (tokens.includes('egg')) {
    tokens = tokens.filter((token) => !EGG_IDENTITY_NOISE_WORDS.has(token))
  }

  return tokens.join(' ')
}

function singularize(token) {
  if (token.length > 4 && token.endsWith('ies')) return `${token.slice(0, -3)}y`
  if (token.length > 4 && token.endsWith('oes')) return token.slice(0, -2)
  return token.length > 3 && token.endsWith('s') && !token.endsWith('ss')
    ? token.slice(0, -1)
    : token
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
