const SAVED_KEY = 'plate.saved.v1'
const HISTORY_KEY = 'plate.history.v1'
const MAX_SAVED = 20
const MAX_HISTORY = 10

function readList(key) {
  try {
    const value = JSON.parse(localStorage.getItem(key))
    return Array.isArray(value) ? value : []
  } catch {
    return []
  }
}

function writeList(key, value) {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
    // The in-memory React state still provides a working session.
  }
  return value
}

export function loadSavedRecipes() {
  return readList(SAVED_KEY).slice(0, MAX_SAVED)
}

export function toggleSavedRecipe(current, recipe) {
  const exists = current.some((item) => item.id === recipe.id)
  if (exists) {
    return writeList(SAVED_KEY, current.filter((item) => item.id !== recipe.id))
  }

  const saved = recipe.sourceUrl
    ? {
        id: recipe.id,
        title: recipe.title,
        sourceName: recipe.sourceName,
        sourceUrl: recipe.sourceUrl,
        savedAt: new Date().toISOString(),
        bookmarkOnly: true,
      }
    : { ...recipe, savedAt: new Date().toISOString(), bookmarkOnly: false }
  return writeList(SAVED_KEY, [saved, ...current].slice(0, MAX_SAVED))
}

export function removeSavedRecipe(current, id) {
  return writeList(SAVED_KEY, current.filter((item) => item.id !== id))
}

export function loadHistory() {
  return readList(HISTORY_KEY).slice(0, MAX_HISTORY)
}

export function addHistoryEntry(current, request, response) {
  const entry = {
    id: crypto.randomUUID(),
    createdAt: new Date().toISOString(),
    ingredients: request.ingredients.map(({ name, quantity }) => ({ name, quantity })),
    allergens: [...request.allergens],
    avoidIngredients: [...request.avoidIngredients],
    dietaryPreference: request.dietaryPreference,
    maxCookingMinutes: request.maxCookingMinutes,
    servings: request.servings,
    resultCount: response.recipes.length,
    provider: response.provider,
  }
  return writeList(HISTORY_KEY, [entry, ...current].slice(0, MAX_HISTORY))
}

export function clearLibrary() {
  try {
    localStorage.removeItem(SAVED_KEY)
    localStorage.removeItem(HISTORY_KEY)
  } catch {
    // There is nothing else to clear when browser storage is unavailable.
  }
}
