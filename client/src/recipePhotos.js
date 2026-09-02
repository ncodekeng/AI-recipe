export function hasValidRecipePhoto(recipe) {
  if (!recipe?.imageUrl) return false

  try {
    return new URL(recipe.imageUrl).protocol === 'https:'
  } catch {
    return false
  }
}

export function shouldUseRemoteRecipeImage(recipe, showRecipePhotos, imageFailed = false) {
  return Boolean(showRecipePhotos) && !imageFailed && hasValidRecipePhoto(recipe)
}
