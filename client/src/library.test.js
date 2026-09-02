import test from 'node:test'
import assert from 'node:assert/strict'
import { addHistoryEntry, getRecentlyShownRecipeIds } from './library.js'

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
