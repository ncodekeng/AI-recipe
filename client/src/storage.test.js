import test from 'node:test'
import assert from 'node:assert/strict'
import {
  loadKitchenMemory,
  loadPreferences,
  mergeKitchenMemory,
  saveKitchenMemory,
  savePreferences,
} from './storage.js'

function installLocalStorage() {
  const values = new Map()
  globalThis.localStorage = {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key),
  }
}

test('photo preference persists through the existing preference store', () => {
  installLocalStorage()

  savePreferences({ showRecipePhotos: false })

  assert.equal(loadPreferences().showRecipePhotos, false)
  delete globalThis.localStorage
})

test('corrected ingredients persist in Kitchen Memory', () => {
  installLocalStorage()

  saveKitchenMemory([{ id: 'one', name: '  Lamb  ', quantity: '500 g', confidence: 88 }])

  assert.deepEqual(loadKitchenMemory().map(({ name, quantity }) => ({ name, quantity })), [
    { name: 'Lamb', quantity: '500 g' },
  ])
  delete globalThis.localStorage
})

test('new scans merge into Kitchen Memory without duplicate names', () => {
  installLocalStorage()

  const merged = mergeKitchenMemory(
    [{ id: 'old-lamb', name: 'Lamb', quantity: '400 g' }],
    [
      { id: 'new-lamb', name: 'lamb', quantity: '500 g' },
      { id: 'wine', name: 'Red wine', quantity: '1 bottle' },
    ],
  )

  assert.equal(merged.length, 2)
  assert.equal(merged[0].quantity, '500 g')
  assert.equal(merged[1].name, 'Red wine')
  delete globalThis.localStorage
})

test('semantic duplicates merge while meaningful colour variants remain separate', () => {
  installLocalStorage()

  const merged = mergeKitchenMemory(
    [
      { id: 'brown-eggs', name: 'Brown eggs', quantity: '6' },
      { id: 'red-pepper', name: 'Red bell pepper', quantity: '1' },
      { id: 'fresh-spinach', name: 'Fresh spinach', quantity: '1 bag' },
      { id: 'sliced-bread', name: 'Sliced bread', quantity: '5 slices' },
      { id: 'small-mustard', name: 'Dijon Mustard (small jar)', quantity: '1 jar' },
    ],
    [
      { id: 'eggs', name: 'Eggs', quantity: '8' },
      { id: 'yellow-pepper', name: 'Yellow bell pepper', quantity: '2' },
      { id: 'spinach', name: 'Spinach', quantity: '2 bags' },
      { id: 'packaged-bread', name: 'Packaged sliced bread', quantity: '1 pack' },
      { id: 'large-mustard', name: 'Dijon Mustard (larger jar)', quantity: '1 jar' },
    ],
  )

  assert.deepEqual(merged.map(({ name, quantity }) => ({ name, quantity })), [
    { name: 'Eggs', quantity: '8' },
    { name: 'Red bell pepper', quantity: '1' },
    { name: 'Spinach', quantity: '2 bags' },
    { name: 'Packaged sliced bread', quantity: '1 pack' },
    { name: 'Dijon Mustard (larger jar)', quantity: '1 jar' },
    { name: 'Yellow bell pepper', quantity: '2' },
  ])
  delete globalThis.localStorage
})

test('loading Kitchen Memory removes previously saved semantic duplicates', () => {
  installLocalStorage()
  globalThis.localStorage.setItem('plate.kitchen-memory.v1', JSON.stringify([
    { id: 'old-eggs', name: 'Brown eggs', quantity: '6' },
    { id: 'new-eggs', name: 'eggs', quantity: '8' },
  ]))

  const loaded = loadKitchenMemory()

  assert.equal(loaded.length, 1)
  assert.equal(loaded[0].name, 'eggs')
  assert.equal(loaded[0].quantity, '8')
  delete globalThis.localStorage
})

test('Kitchen Memory keeps up to 100 ingredients', () => {
  installLocalStorage()

  const ingredients = Array.from({ length: 101 }, (_, index) => ({
    id: `ingredient-${index + 1}`,
    name: `Ingredient ${index + 1}`,
    quantity: '1',
  }))

  const saved = saveKitchenMemory(ingredients)

  assert.equal(saved.length, 100)
  assert.equal(loadKitchenMemory().length, 100)
  assert.equal(saved.at(-1).name, 'Ingredient 100')
  delete globalThis.localStorage
})
