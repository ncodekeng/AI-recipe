export const RECIPE_MODES = Object.freeze({
  ALL: 'all',
  AVAILABLE_ONLY: 'availableOnly',
})

export const INITIAL_RECIPE_SEARCH_STATE = Object.freeze({
  mode: RECIPE_MODES.ALL,
  recipes: [],
  availableOnlyRecipes: [],
  hasCompletedSearch: false,
})

export function recipeSearchReducer(state, action) {
  switch (action.type) {
    case 'invalidate':
      return {
        ...state,
        recipes: [],
        availableOnlyRecipes: [],
        hasCompletedSearch: false,
      }
    case 'modeRequested':
      return {
        ...state,
        mode: action.mode,
      }
    case 'searchStarted':
      return {
        ...state,
        recipes: [],
        availableOnlyRecipes: [],
      }
    case 'searchSucceeded':
      return {
        ...state,
        recipes: Array.isArray(action.recipes) ? action.recipes : [],
        availableOnlyRecipes: [],
        hasCompletedSearch: true,
      }
    case 'searchFailed':
      return {
        ...state,
        recipes: [],
        availableOnlyRecipes: [],
        hasCompletedSearch: true,
      }
    case 'availableOnlySearchSucceeded':
      return {
        ...state,
        availableOnlyRecipes: Array.isArray(action.recipes) ? action.recipes : [],
      }
    case 'replaceRecipes':
      return {
        ...state,
        recipes: Array.isArray(action.recipes) ? action.recipes : state.recipes,
      }
    case 'replaceAvailableOnlyRecipes':
      return {
        ...state,
        availableOnlyRecipes: Array.isArray(action.recipes)
          ? action.recipes
          : state.availableOnlyRecipes,
      }
    default:
      return state
  }
}

export function usesOnlyAvailableIngredients(mode) {
  return mode === RECIPE_MODES.AVAILABLE_ONLY
}

export function isCompleteRecipeMatch(recipe) {
  if (Array.isArray(recipe?.missingIngredients)) {
    return recipe.missingIngredients.length === 0
  }

  return Number(recipe?.ingredientMatch) === 100
}

export function getRecipesForMode(recipes, mode, availableOnlyRecipes = []) {
  if (!Array.isArray(recipes)) return []
  if (!usesOnlyAvailableIngredients(mode)) return recipes

  const candidates = [
    ...(Array.isArray(availableOnlyRecipes) ? availableOnlyRecipes : []),
    ...recipes,
  ]
  const seen = new Set()
  return candidates
    .filter(isCompleteRecipeMatch)
    .filter((recipe) => {
      const identity = recipe?.sourceUrl || recipe?.id
      if (!identity || seen.has(identity)) return false
      seen.add(identity)
      return true
    })
    .slice(0, 6)
}

export function getRecipeSearchEmptyState({ mode, hasCompletedSearch, recipes }, busy, error) {
  if (!hasCompletedSearch || busy === 'generating' || recipes.length > 0) return null

  if (error) {
    return {
      title: 'Recipe search didn’t finish.',
      message: error,
      canRetry: true,
      canShowAll: usesOnlyAvailableIngredients(mode),
      retryLabel: usesOnlyAvailableIngredients(mode) ? 'Find 100% matches again' : 'Try again',
    }
  }

  return usesOnlyAvailableIngredients(mode)
    ? {
        title: 'No 100% matches in these results.',
        message: 'Show all recipes now, or run one additional sourced search for recipes needing nothing extra.',
        canRetry: true,
        canShowAll: true,
        retryLabel: 'Find 100% matches',
        retryCostNote: 'Uses 1 recipe search.',
      }
    : {
        title: 'No sourced recipes were found.',
        message: 'Try again or adjust your ingredients and preferences.',
        canRetry: true,
        canShowAll: false,
        retryLabel: 'Try again',
      }
}
