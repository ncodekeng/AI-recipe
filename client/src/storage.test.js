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
