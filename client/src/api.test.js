import assert from 'node:assert/strict'
import test from 'node:test'
import { updateAdminPrompts } from './api.js'

test('prompt updates send the admin key only in the protected API header', async () => {
  const originalFetch = globalThis.fetch
  const originalLocalStorage = globalThis.localStorage
  let captured
  globalThis.localStorage = {
    getItem: () => 'test-client-id',
    setItem: () => {},
  }
  globalThis.fetch = async (url, options) => {
    captured = { url, options }
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
    await updateAdminPrompts('admin-secret', {
      ingredientRecognitionPrompt: 'Ingredient prompt long enough for the API.',
      recipeRecommendationPrompt: 'Recipe prompt long enough for the API.',
    })

    assert.equal(captured.url, '/api/admin/prompts')
    assert.equal(captured.options.method, 'PUT')
    assert.equal(captured.options.headers['X-Plate-Admin-Key'], 'admin-secret')
    assert.equal(captured.options.headers['Content-Type'], 'application/json')
    assert.doesNotMatch(captured.options.body, /admin-secret/)
  } finally {
    globalThis.fetch = originalFetch
    globalThis.localStorage = originalLocalStorage
  }
})
