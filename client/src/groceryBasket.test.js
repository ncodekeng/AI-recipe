import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildGroceryBasketPayload,
  canPrepareGroceryBasket,
  formatShoppingList,
} from './groceryBasket.js'

test('Deliveroo payload contains only missing ingredients', () => {
  const recipe = {
    id: '95e729fd-8d19-4e7b-9ad9-3b9b94063f90',
    availableIngredients: [
      { name: 'chicken' },
      { name: 'bell pepper' },
      { name: 'spinach' },
    ],
    missingIngredients: [
      { name: 'garlic', amount: '2 cloves', quantity: 2, unit: 'clove' },
      { name: 'feta', amount: '100 g', quantity: 100, unit: 'g' },
    ],
  }

  const payload = buildGroceryBasketPayload(recipe)

  assert.deepEqual(payload.ingredients.map((item) => item.name), ['garlic', 'feta'])
  assert.equal(payload.ingredients.some((item) => item.name === 'chicken'), false)
  assert.equal(canPrepareGroceryBasket(recipe), true)
  assert.match(formatShoppingList(payload.ingredients), /2 cloves garlic/)
})

test('zero missing ingredients disables the grocery action', () => {
  assert.equal(canPrepareGroceryBasket({ id: 'recipe-id', missingIngredients: [] }), false)
})
