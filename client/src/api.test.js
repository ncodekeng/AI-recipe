import assert from 'node:assert/strict'
import test from 'node:test'
import { findRecipePhotos, getAdminPrompts, updateAdminPrompts } from './api.js'

test('admin key is sent only while creating the protected session', async () => {
  const originalFetch = globalThis.fetch
  const originalLocalStorage = globalThis.localStorage
  const captured = []
  globalThis.localStorage = {
    getItem: () => 'test-client-id',
    setItem: () => {},
  }
  globalThis.fetch = async (url, options) => {
    captured.push({ url, options })
    return {
      ok: true,
      json: async () => ({
        ingredientRecognitionPrompt: 'Ingredient prompt long enough for the API.',
        recipeRecommendationPrompt: 'Recipe prompt long enough for the API.',
        usingDefaults: false,
        updatedAtUtc: '2026-09-03T00:00:00Z',
        maxPromptLength: 8000,
      }),
    }
  }

  try {
    await getAdminPrompts('admin-secret')
    await updateAdminPrompts({
      ingredientRecognitionPrompt: 'Ingredient prompt long enough for the API.',
      recipeRecommendationPrompt: 'Recipe prompt long enough for the API.',
    })

    assert.equal(captured[0].url, '/api/admin/prompts')
    assert.equal(captured[0].options.headers['X-Plate-Admin-Key'], 'admin-secret')
    assert.equal(captured[0].options.credentials, 'same-origin')
    assert.equal(captured[1].options.method, 'PUT')
    assert.equal(captured[1].options.headers['X-Plate-Admin-Key'], undefined)
    assert.equal(captured[1].options.headers['Content-Type'], 'application/json')
    assert.doesNotMatch(captured[1].options.body, /admin-secret/)
  } finally {
    globalThis.fetch = originalFetch
    globalThis.localStorage = originalLocalStorage
  }
})

test('photo refresh uses the photo-only endpoint', async () => {
  const originalFetch = globalThis.fetch
  const originalLocalStorage = globalThis.localStorage
  let captured
  globalThis.localStorage = {
    getItem: () => 'test-client-id',
    setItem: () => {},
  }
  globalThis.fetch = async (url, options) => {
    captured = { url, options }
    return { ok: true, json: async () => [] }
  }

  try {
    const recipes = [{ id: '95e729fd-8d19-4e7b-9ad9-3b9b94063f90', title: 'Chicken and peppers' }]
    await findRecipePhotos(recipes)

    assert.equal(captured.url, '/api/recipes/photos')
    assert.equal(captured.options.method, 'POST')
    assert.deepEqual(JSON.parse(captured.options.body), { recipes })
  } finally {
    globalThis.fetch = originalFetch
    globalThis.localStorage = originalLocalStorage
  }
})
