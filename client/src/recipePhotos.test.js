import test from 'node:test'
import assert from 'node:assert/strict'
import { hasValidRecipePhoto, shouldUseRemoteRecipeImage } from './recipePhotos.js'

const recipe = {
  title: 'Roast salmon with lemon',
  imageUrl: 'https://images.example.test/salmon.jpg',
}

test('photo switch on selects a valid provider image', () => {
  assert.equal(hasValidRecipePhoto(recipe), true)
  assert.equal(shouldUseRemoteRecipeImage(recipe, true), true)
})

test('photo switch off never selects the remote image', () => {
  assert.equal(shouldUseRemoteRecipeImage(recipe, false), false)
})

test('failed or unsafe remote images select fallback artwork', () => {
  assert.equal(shouldUseRemoteRecipeImage(recipe, true, true), false)
  assert.equal(shouldUseRemoteRecipeImage({ ...recipe, imageUrl: 'javascript:alert(1)' }, true), false)
})
