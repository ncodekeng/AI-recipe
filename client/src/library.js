const SAVED_KEY = 'plate.saved.v1'
const HISTORY_KEY = 'plate.history.v1'
const RECENT_RECIPES_KEY = 'plate.recent-recipes.v1'
const MAX_SAVED = 20
const MAX_HISTORY = 10
const MAX_RECENT_RECIPES = 6
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

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

  const saved = recipe.sourceVerified && recipe.sourceUrl
    ? {
        id: recipe.id,
        title: recipe.title,
        sourceTitle: recipe.sourceTitle,
        sourceName: recipe.sourceName,
        sourceUrl: recipe.sourceUrl,
        sourceVerified: recipe.sourceVerified,
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

export function loadRecentlyViewedRecipes() {
  return readList(RECENT_RECIPES_KEY).slice(0, MAX_RECENT_RECIPES)
}

export function addRecentlyViewedRecipe(current, recipe) {
  if (!recipe?.id || !recipe?.title || !recipe?.sourceUrl || !recipe?.sourceVerified || !recipe?.imageUrl) return current

  const preview = {
    id: recipe.id,
    title: recipe.title,
    cuisine: recipe.cuisine || '',
    cookingMinutes: Number.isFinite(recipe.cookingMinutes) && recipe.cookingMinutes > 0
      ? Math.round(recipe.cookingMinutes)
      : null,
    caloriesPerServing: Number.isFinite(recipe.caloriesPerServing) && recipe.caloriesPerServing > 0
      ? Math.round(recipe.caloriesPerServing)
      : null,
    sourceName: recipe.sourceName || 'Original publisher',
    sourceTitle: recipe.sourceTitle || recipe.title,
    sourceUrl: recipe.sourceUrl,
    sourceVerified: recipe.sourceVerified,
    publisherPageVerified: recipe.publisherPageVerified === true,
    displayImageUrl: recipe.displayImageUrl,
    imageUrl: recipe.imageUrl,
    imageSourceUrl: recipe.imageSourceUrl,
    imageLicenseType: recipe.imageLicenseType,
    imageLicenseUrl: recipe.imageLicenseUrl,
    imageAttributionRequirements: recipe.imageAttributionRequirements,
    imageRightsStatus: recipe.imageRightsStatus,
    imageProvider: recipe.imageProvider,
    imageCreator: recipe.imageCreator,
    imageCommercialUseAllowed: recipe.imageCommercialUseAllowed,
    imageAttributionRequired: recipe.imageAttributionRequired,
    imageVerified: recipe.imageVerified,
    viewedAt: new Date().toISOString(),
  }
  const next = [preview, ...current.filter((item) => item.id !== recipe.id)].slice(0, MAX_RECENT_RECIPES)
  return writeList(RECENT_RECIPES_KEY, next)
}

export function addHistoryEntry(current, request, response) {
  const entry = {
    id: crypto.randomUUID(),
    createdAt: new Date().toISOString(),
    ingredients: request.ingredients.map(({ name, quantity }) => ({ name, quantity })),
    allergens: [...request.allergens],
    avoidIngredients: [...request.avoidIngredients],
    dietaryPreference: request.dietaryPreference,
    mainIngredient: request.mainIngredient,
    maxCookingMinutes: request.maxCookingMinutes,
    servings: request.servings,
    maxRecipes: request.maxRecipes,
    resultCount: response.recipes.length,
    recipeIds: response.recipes.map((recipe) => recipe.id).filter(Boolean).slice(0, 6),
    recipeTitles: response.recipes.map((recipe) => recipe.title).filter(Boolean).slice(0, 6),
    provider: response.provider,
  }
  return writeList(HISTORY_KEY, [entry, ...current].slice(0, MAX_HISTORY))
}

export function getRecentlyShownRecipeIds(history) {
  return [...new Set((Array.isArray(history) ? history : [])
    .flatMap((entry) => Array.isArray(entry.recipeIds) ? entry.recipeIds : [])
    .filter((id) => typeof id === 'string' && GUID_PATTERN.test(id)))]
    .slice(0, 30)
}

export function clearLibrary() {
  try {
    localStorage.removeItem(SAVED_KEY)
    localStorage.removeItem(HISTORY_KEY)
    localStorage.removeItem(RECENT_RECIPES_KEY)
  } catch {
    // There is nothing else to clear when browser storage is unavailable.
  }
}
