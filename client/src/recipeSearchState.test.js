import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import {
  getRecipeSearchEmptyState,
  getRecipesForMode,
  INITIAL_RECIPE_SEARCH_STATE,
  RECIPE_MODES,
  recipeSearchReducer,
  usesOnlyAvailableIngredients,
} from './recipeSearchState.js'

test('mode switch stays hidden before the first successful search', () => {
  assert.equal(INITIAL_RECIPE_SEARCH_STATE.hasCompletedSearch, false)
})

test('mode switch becomes available after a successful search', () => {
  const state = recipeSearchReducer(INITIAL_RECIPE_SEARCH_STATE, {
    type: 'searchSucceeded',
    recipes: [{ id: 'recipe-a' }],
  })

  assert.equal(state.hasCompletedSearch, true)
})

test('App gates the accessible segmented control on completed results', () => {
  const source = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')

  assert.match(source, /\{hasCompletedSearch && \([\s\S]*?className="recipe-scope-toggle"/)
  assert.match(source, /role="group" aria-label="Recipe range"/)
  assert.match(source, /aria-pressed=/)
  assert.match(source, />\s*Cook with what I have\s*</)
  assert.match(source, />\s*Show all recipes\s*</)
})

test('the initial recipe mode is Show all recipes', () => {
  assert.equal(INITIAL_RECIPE_SEARCH_STATE.mode, RECIPE_MODES.ALL)
  assert.equal(usesOnlyAvailableIngredients(INITIAL_RECIPE_SEARCH_STATE.mode), false)
})

test('Cook with what I have activates the local complete-match filter', () => {
  assert.equal(usesOnlyAvailableIngredients(RECIPE_MODES.AVAILABLE_ONLY), true)
})

test('Show all recipes disables the local complete-match filter', () => {
  assert.equal(usesOnlyAvailableIngredients(RECIPE_MODES.ALL), false)
})

test('available-only zero results show the required empty state and exit action', () => {
  const state = {
    mode: RECIPE_MODES.AVAILABLE_ONLY,
    recipes: [],
    hasCompletedSearch: true,
  }

  const emptyState = getRecipeSearchEmptyState(state, '', '')

  assert.equal(emptyState.title, 'No 100% matches in these results.')
  assert.equal(emptyState.canShowAll, true)
  assert.equal(emptyState.canRetry, true)
  assert.equal(emptyState.retryLabel, 'Find 100% matches')
  assert.equal(emptyState.retryCostNote, 'Uses 1 recipe search.')
})

test('ingredient invalidation clears stale recipes but preserves selected mode', () => {
  const state = recipeSearchReducer({
    mode: RECIPE_MODES.AVAILABLE_ONLY,
    recipes: [{ id: 'stale-recipe' }],
    hasCompletedSearch: true,
  }, { type: 'invalidate' })

  assert.deepEqual(state.recipes, [])
  assert.equal(state.hasCompletedSearch, false)
  assert.equal(state.mode, RECIPE_MODES.AVAILABLE_ONLY)
})

test('a new search continues to use the mode preserved after ingredient edits', () => {
  const invalidated = recipeSearchReducer({
    mode: RECIPE_MODES.AVAILABLE_ONLY,
    recipes: [{ id: 'old' }],
    hasCompletedSearch: true,
  }, { type: 'invalidate' })

  assert.equal(usesOnlyAvailableIngredients(invalidated.mode), true)
})

test('switching modes preserves the sourced result set', () => {
  const state = recipeSearchReducer({
    mode: RECIPE_MODES.ALL,
    recipes: [{ id: 'near-match' }],
    hasCompletedSearch: true,
  }, { type: 'modeRequested', mode: RECIPE_MODES.AVAILABLE_ONLY })

  assert.equal(state.mode, RECIPE_MODES.AVAILABLE_ONLY)
  assert.deepEqual(state.recipes, [{ id: 'near-match' }])
  assert.equal(state.hasCompletedSearch, true)
})

test('Cook with what I have locally selects only zero-missing recipes', () => {
  const recipes = [
    { id: 'complete', ingredientMatch: 100, missingIngredients: [] },
    { id: 'near-match', ingredientMatch: 80, missingIngredients: [{ name: 'garlic' }] },
    { id: 'fallback-complete', ingredientMatch: 100 },
  ]

  assert.deepEqual(
    getRecipesForMode(recipes, RECIPE_MODES.AVAILABLE_ONLY).map((recipe) => recipe.id),
    ['complete', 'fallback-complete'],
  )
  assert.strictEqual(getRecipesForMode(recipes, RECIPE_MODES.ALL), recipes)
})

test('an explicit exact search preserves broad results and supplies the filtered view', () => {
  const broadRecipes = [
    { id: 'near-match', ingredientMatch: 80, missingIngredients: [{ name: 'garlic' }] },
  ]
  const state = recipeSearchReducer({
    mode: RECIPE_MODES.AVAILABLE_ONLY,
    recipes: broadRecipes,
    availableOnlyRecipes: [],
    hasCompletedSearch: true,
  }, {
    type: 'availableOnlySearchSucceeded',
    recipes: [{ id: 'exact-match', ingredientMatch: 100, missingIngredients: [] }],
  })

  assert.strictEqual(state.recipes, broadRecipes)
  assert.deepEqual(
    getRecipesForMode(state.recipes, state.mode, state.availableOnlyRecipes).map((recipe) => recipe.id),
    ['exact-match'],
  )
  assert.strictEqual(getRecipesForMode(state.recipes, RECIPE_MODES.ALL, state.availableOnlyRecipes), broadRecipes)
})

test('changing recipe range never starts another recipe search', () => {
  const source = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  const handler = source.match(/function handleRecipeScopeChange[\s\S]*?\n  }\n\n  async function handlePhotoPreferenceChange/)?.[0] || ''

  assert.match(handler, /dispatchRecipeSearch\(\{ type: 'modeRequested'/)
  assert.doesNotMatch(handler, /handleGenerate|generateRecipes|await/)
})

test('only the explicit complete-match action requests exact recipes', () => {
  const source = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')

  assert.match(source, /function handleGenerate\(\)\s*\{\s*return runRecipeSearch\(false, false\)/)
  assert.match(source, /function handleFindCompleteMatches\(\)\s*\{\s*return runRecipeSearch\(true, true\)/)
})

test('mode search errors remain visible with retry and Show all actions', () => {
  const state = {
    mode: RECIPE_MODES.AVAILABLE_ONLY,
    recipes: [],
    hasCompletedSearch: true,
  }

  const emptyState = getRecipeSearchEmptyState(state, '', 'Provider unavailable')

  assert.equal(emptyState.message, 'Provider unavailable')
  assert.equal(emptyState.canRetry, true)
  assert.equal(emptyState.canShowAll, true)
})

test('an initial failed search exposes retry and Show all controls', () => {
  const state = recipeSearchReducer(INITIAL_RECIPE_SEARCH_STATE, { type: 'searchFailed' })
  const emptyState = getRecipeSearchEmptyState(state, '', 'No cited recipes were found')

  assert.equal(state.hasCompletedSearch, true)
  assert.deepEqual(state.recipes, [])
  assert.equal(emptyState.message, 'No cited recipes were found')
  assert.equal(emptyState.canRetry, true)
  assert.equal(emptyState.canShowAll, false)
})

test('photo selection and additions allow up to 50 images', () => {
  const source = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')

  assert.match(source, /const MAX_PHOTO_COUNT = 50/)
  assert.match(source, /const remaining = MAX_PHOTO_COUNT - photos\.length/)
  assert.doesNotMatch(source, /photos\.length >= 6|photos\.length < 6|6 - photos\.length/)
})

test('mobile recipe mode CSS prevents horizontal control overflow', () => {
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')

  assert.match(css, /\.recipe-scope-toggle\s*\{[^}]*grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\)/s)
  assert.match(css, /\.recipe-scope-toggle\s*\{[^}]*max-width:\s*100%/s)
  assert.match(css, /@media \(max-width: 360px\)[\s\S]*?\.recipe-scope-toggle\s*\{[^}]*grid-template-columns:\s*1fr/s)
})
