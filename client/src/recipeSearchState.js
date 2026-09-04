export const RECIPE_MODES = Object.freeze({
  ALL: 'all',
  AVAILABLE_ONLY: 'availableOnly',
})

export const INITIAL_RECIPE_SEARCH_STATE = Object.freeze({
  mode: RECIPE_MODES.AVAILABLE_ONLY,
  recipes: [],
  hasCompletedSearch: false,
})

export function recipeSearchReducer(state, action) {
  switch (action.type) {
    case 'invalidate':
      return {
        ...state,
        recipes: [],
        hasCompletedSearch: false,
      }
    case 'modeRequested':
      return {
        ...state,
        mode: action.mode,
        recipes: [],
      }
    case 'searchStarted':
      return {
        ...state,
        recipes: [],
      }
    case 'searchSucceeded':
      return {
        ...state,
        recipes: Array.isArray(action.recipes) ? action.recipes : [],
        hasCompletedSearch: true,
      }
    case 'replaceRecipes':
      return {
        ...state,
        recipes: Array.isArray(action.recipes) ? action.recipes : state.recipes,
      }
    default:
      return state
  }
}

export function usesOnlyAvailableIngredients(mode) {
  return mode === RECIPE_MODES.AVAILABLE_ONLY
}

export function getRecipeSearchEmptyState({ mode, hasCompletedSearch, recipes }, busy, error) {
  if (!hasCompletedSearch || busy === 'generating' || recipes.length > 0) return null

  if (error) {
    return {
      title: 'Recipe search didn’t finish.',
      message: error,
      canRetry: true,
      canShowAll: usesOnlyAvailableIngredients(mode),
    }
  }

  return usesOnlyAvailableIngredients(mode)
    ? {
        title: 'No recipes found using only what you have.',
        message: 'Try Show all recipes to include dishes needing a few extra ingredients.',
        canRetry: true,
        canShowAll: true,
      }
    : {
        title: 'No sourced recipes were found.',
        message: 'Try again or adjust your ingredients and preferences.',
        canRetry: true,
        canShowAll: false,
      }
}
