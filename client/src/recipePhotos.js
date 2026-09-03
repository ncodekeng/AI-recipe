export function hasValidRecipePhoto(recipe) {
  if (!recipe?.imageUrl || !recipe?.imageSourceUrl || !recipe?.imageLicenseType || !recipe?.imageAttributionRequirements) return false

  try {
    const imageUrl = new URL(recipe.imageUrl)
    const sourceUrl = new URL(recipe.imageSourceUrl)
    const validImageAndSource = imageUrl.protocol === 'https:' &&
      ['upload.wikimedia.org', 'thumb.wikimedia.org'].includes(imageUrl.hostname) &&
      sourceUrl.protocol === 'https:' &&
      sourceUrl.hostname === 'commons.wikimedia.org'
    if (recipe.imageRightsStatus === 'UnverifiedTestOnly') {
      return validImageAndSource && recipe.imageLicenseType === 'Unverified test image'
    }
    if (recipe.imageRightsStatus !== 'VerifiedCommercial') return false

    const license = recipe.imageLicenseType.toUpperCase().replaceAll('-', ' ')
    const isCommercialLicense = license.startsWith('CC0') ||
      license.startsWith('PUBLIC DOMAIN') ||
      license === 'PDM' ||
      license.startsWith('PD ') ||
      (license.startsWith('CC BY') && !license.includes(' NC') && !license.includes(' ND'))
    const requiresLicenseLink = license.startsWith('CC BY')
    const validLicenseUrl = !requiresLicenseLink || (() => {
      try {
        const licenseUrl = new URL(recipe.imageLicenseUrl)
        return licenseUrl.protocol === 'https:' && licenseUrl.hostname === 'creativecommons.org'
      } catch {
        return false
      }
    })()

    return validImageAndSource &&
      isCommercialLicense &&
      validLicenseUrl
  } catch {
    return false
  }
}

export function shouldUseRemoteRecipeImage(recipe, showRecipePhotos, imageFailed = false) {
  return Boolean(showRecipePhotos) && !imageFailed && hasValidRecipePhoto(recipe)
}
