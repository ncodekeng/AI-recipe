import test from 'node:test'
import assert from 'node:assert/strict'
import { addHistoryEntry, addRecentlyViewedRecipe, getRecentlyShownRecipeIds, loadRecentlyViewedRecipes } from './library.js'

test('history records recipe IDs for recommendation diversity', () => {
  const firstRecipeId = '95e729fd-8d19-4e7b-9ad9-3b9b94063f90'
  const secondRecipeId = 'd12d8212-af7c-47ac-9b45-02bfc61c6832'
  const values = new Map()
  globalThis.localStorage = {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key),
  }

  const history = addHistoryEntry([], {
    ingredients: [{ name: 'Lamb', quantity: '500 g' }],
    allergens: [],
    avoidIngredients: [],
    dietaryPreference: 'Anything',
    maxCookingMinutes: 90,
    servings: 4,
  }, {
    recipes: [{ id: firstRecipeId, title: 'Lamb stew' }, { id: secondRecipeId, title: 'Lamb roast' }],
    provider: 'Edamam',
  })

  assert.deepEqual(getRecentlyShownRecipeIds(history), [firstRecipeId, secondRecipeId])
  delete globalThis.localStorage
})

test('recently viewed recipes retain only sourced real-photo preview metadata', () => {
  const values = new Map()
  globalThis.localStorage = {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key),
  }

  addRecentlyViewedRecipe([], {
    id: 'recipe-one',
    title: 'Shakshuka',
    cuisine: 'North African',
    cookingMinutes: 25,
    caloriesPerServing: 340,
    sourceName: 'Good Food',
    sourceUrl: 'https://example.com/shakshuka',
    sourceVerified: true,
    imageUrl: 'https://upload.wikimedia.org/example.jpg',
    imageSourceUrl: 'https://commons.wikimedia.org/wiki/File:Example.jpg',
    imageLicenseType: 'CC BY 4.0',
    imageLicenseUrl: 'https://creativecommons.org/licenses/by/4.0/',
    imageAttributionRequirements: 'Photo credit',
    imageRightsStatus: 'VerifiedCommercial',
    steps: ['This must not be persisted.'],
    ingredients: [{ name: 'Eggs' }],
  })

  const [recent] = loadRecentlyViewedRecipes()
  assert.equal(recent.title, 'Shakshuka')
  assert.equal(recent.imageRightsStatus, 'VerifiedCommercial')
  assert.equal(recent.cookingMinutes, 25)
  assert.equal(recent.caloriesPerServing, 340)
  assert.equal('steps' in recent, false)
  assert.equal('ingredients' in recent, false)
  delete globalThis.localStorage
})
