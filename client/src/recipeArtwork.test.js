import test from 'node:test'
import assert from 'node:assert/strict'
import { getRecipeArtwork } from './recipeArtwork.js'

function recipe(title, ingredients, extra = {}) {
  return {
    title,
    ingredients: ingredients.map((name) => ({ name })),
    cuisine: 'Modern',
    tags: ['Dinner'],
    ...extra,
  }
}

test('selects bell pepper, chicken, and spinach', () => {
  const artwork = getRecipeArtwork(recipe(
    'Stuffed Bell Peppers with Chicken and Spinach',
    ['Bell peppers', 'Chicken breast', 'Baby spinach', 'Olive oil'],
  ))

  assert.deepEqual(artwork.ingredients.map((item) => item.key), ['bell pepper', 'chicken', 'spinach'])
})

test('selects potato, spinach, and bread', () => {
  const artwork = getRecipeArtwork(recipe(
    'Potato and Spinach Sauté with Toasted Bread',
    ['Potatoes', 'Spinach', 'Bread', 'Salt'],
  ))

  assert.deepEqual(artwork.ingredients.map((item) => item.key), ['potato', 'spinach', 'bread'])
})

test('understands bruschetta as a bread visual', () => {
  const artwork = getRecipeArtwork(recipe(
    'Tomato and Bell Pepper Bruschetta',
    ['Tomatoes', 'Bell peppers', 'Baguette'],
  ))

  assert.deepEqual(artwork.ingredients.map((item) => item.key), ['tomato', 'bell pepper', 'bread'])
})

test('creates deterministic artwork and a generic unknown ingredient', () => {
  const input = recipe('Roasted Celeriac Supper', ['Celeriac', 'Olive oil'])
  const first = getRecipeArtwork(input)
  const second = getRecipeArtwork(input)

  assert.deepEqual(first, second)
  assert.equal(first.ingredients[0].key, 'generic-celeriac')
})

test('uses lamb artwork for the reference recommendation style', () => {
  const artwork = getRecipeArtwork(recipe(
    'Red-wine braised lamb shanks',
    ['Lamb shanks', 'Garlic', 'Onion', 'Carrots'],
  ))

  assert.deepEqual(artwork.ingredients.map((item) => item.key), ['lamb', 'garlic', 'onion'])
})
