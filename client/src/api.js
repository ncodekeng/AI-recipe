const CLIENT_ID_KEY = 'plate.client-id'
let transientClientId = null

function getClientId() {
  if (transientClientId) return transientClientId

  try {
    const existing = localStorage.getItem(CLIENT_ID_KEY)
    if (existing) {
      transientClientId = existing
      return existing
    }

    transientClientId = crypto.randomUUID()
    localStorage.setItem(CLIENT_ID_KEY, transientClientId)
    return transientClientId
  } catch {
    transientClientId = crypto.randomUUID()
    return transientClientId
  }
}

async function parseResponse(response) {
  if (response.ok) {
    return response.json()
  }

  let message = 'Something went wrong. Please try again.'
  try {
    const problem = await response.json()
    message = problem.detail || problem.title || message
  } catch {
    // Keep the friendly default when the server did not return JSON.
  }

  throw new Error(message)
}

async function request(url, options = {}, timeoutMs = 90_000) {
  const controller = new AbortController()
  const abortFromCaller = () => controller.abort()
  options.signal?.addEventListener('abort', abortFromCaller, { once: true })
  const timeout = setTimeout(() => controller.abort(), timeoutMs)

  try {
    const response = await fetch(url, {
      ...options,
      signal: controller.signal,
      headers: {
        'X-Plate-Client-Id': getClientId(),
        ...options.headers,
      },
    })
    return await parseResponse(response)
  } catch (error) {
    if (error.name === 'AbortError') {
      throw new Error('The request took too long. Please retry without leaving this page.')
    }
    throw error
  } finally {
    clearTimeout(timeout)
    options.signal?.removeEventListener('abort', abortFromCaller)
  }
}

export async function getStatus(signal) {
  return request('/api/status', { signal }, 10_000)
}

export async function getUsage(signal) {
  return request('/api/usage', { signal }, 10_000)
}

export async function resetUsage() {
  return request('/api/usage/reset', { method: 'POST' }, 10_000)
}

export async function analyzePhotos(files) {
  const form = new FormData()
  files.forEach((file) => form.append('photos', file))

  return request('/api/ingredients/analyze', {
    method: 'POST',
    body: form,
  })
}

export async function generateRecipes(payload) {
  return request('/api/recipes/generate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
}

export async function createDeliverooBasket(payload) {
  return request('/api/grocery/deliveroo/basket', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }, 20_000)
}

export async function submitFeedback(payload) {
  return request('/api/feedback', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  }, 10_000)
}
