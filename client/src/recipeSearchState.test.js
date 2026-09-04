import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import {
  getRecipeSearchEmptyState,
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

test('the initial recipe mode is Cook with what I have', () => {
  assert.equal(INITIAL_RECIPE_SEARCH_STATE.mode, RECIPE_MODES.AVAILABLE_ONLY)
  assert.equal(usesOnlyAvailableIngredients(INITIAL_RECIPE_SEARCH_STATE.mode), true)
})

test('Cook with what I have sets the backend available-only flag', () => {
  assert.equal(usesOnlyAvailableIngredients(RECIPE_MODES.AVAILABLE_ONLY), true)
})

test('Show all recipes clears the backend available-only flag', () => {
  assert.equal(usesOnlyAvailableIngredients(RECIPE_MODES.ALL), false)
})

test('available-only zero results show the required empty state and exit action', () => {
  const state = {
    mode: RECIPE_MODES.AVAILABLE_ONLY,
    recipes: [],
    hasCompletedSearch: true,
  }

  const emptyState = getRecipeSearchEmptyState(state, '', '')

  assert.equal(emptyState.title, 'No recipes found using only what you have.')
  assert.equal(emptyState.canShowAll, true)
  assert.equal(emptyState.canRetry, true)
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

test('switching modes updates selection and removes stale results immediately', () => {
  const state = recipeSearchReducer({
    mode: RECIPE_MODES.ALL,
    recipes: [{ id: 'near-match' }],
    hasCompletedSearch: true,
  }, { type: 'modeRequested', mode: RECIPE_MODES.AVAILABLE_ONLY })

  assert.equal(state.mode, RECIPE_MODES.AVAILABLE_ONLY)
  assert.deepEqual(state.recipes, [])
  assert.equal(state.hasCompletedSearch, true)
})

test('loading a switched mode hides both stale results and a premature empty state', () => {
  const state = recipeSearchReducer({
    mode: RECIPE_MODES.ALL,
    recipes: [{ id: 'old-result' }],
    hasCompletedSearch: true,
  }, { type: 'modeRequested', mode: RECIPE_MODES.AVAILABLE_ONLY })

  assert.deepEqual(state.recipes, [])
  assert.equal(getRecipeSearchEmptyState(state, 'generating', ''), null)
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

test('mobile recipe mode CSS prevents horizontal control overflow', () => {
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')

  assert.match(css, /\.recipe-scope-toggle\s*\{[^}]*grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\)/s)
  assert.match(css, /\.recipe-scope-toggle\s*\{[^}]*max-width:\s*100%/s)
  assert.match(css, /@media \(max-width: 360px\)[\s\S]*?\.recipe-scope-toggle\s*\{[^}]*grid-template-columns:\s*1fr/s)
})
