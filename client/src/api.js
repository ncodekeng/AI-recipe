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

export async function getStatus(signal) {
  const response = await fetch('/api/status', { signal })
  return parseResponse(response)
}

export async function analyzePhotos(files) {
  const form = new FormData()
  files.forEach((file) => form.append('photos', file))

  const response = await fetch('/api/ingredients/analyze', {
    method: 'POST',
    body: form,
  })
  return parseResponse(response)
}

export async function generateRecipes(payload) {
  const response = await fetch('/api/recipes/generate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseResponse(response)
}
