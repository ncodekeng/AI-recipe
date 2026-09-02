import test from 'node:test'
import assert from 'node:assert/strict'
import { loadPreferences, savePreferences } from './storage.js'

test('photo preference persists through the existing preference store', () => {
  const values = new Map()
  globalThis.localStorage = {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key),
  }

  savePreferences({ showRecipePhotos: false })

  assert.equal(loadPreferences().showRecipePhotos, false)
  delete globalThis.localStorage
})
