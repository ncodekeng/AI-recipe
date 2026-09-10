export function hasValidRecipePhoto(recipe) {
  if (!recipe?.imageUrl || !recipe?.imageSourceUrl || !recipe?.imageLicenseType || !recipe?.imageAttributionRequirements) return false

  try {
    const imageUrl = new URL(recipe.imageUrl)
    const sourceUrl = new URL(recipe.imageSourceUrl)
    const validCommonsImageAndSource = imageUrl.protocol === 'https:' &&
      ['upload.wikimedia.org', 'thumb.wikimedia.org'].includes(imageUrl.hostname) &&
      sourceUrl.protocol === 'https:' &&
      sourceUrl.hostname === 'commons.wikimedia.org'
    if (recipe.imageRightsStatus === 'UnverifiedTestOnly') {
      const isCommonsTestImage = validCommonsImageAndSource &&
        recipe.imageProvider === 'Wikimedia Commons' &&
        recipe.imageLicenseType === 'Unverified test image'
      const isPublisherTestImage = recipe.imageProvider === 'Recipe publisher' &&
        recipe.imageLicenseType === 'Unverified publisher image' &&
        recipe.publisherPageVerified === true &&
        isPublicLookingHttpsUrl(imageUrl) &&
        isPublicLookingHttpsUrl(sourceUrl) &&
        sourceUrl.href === new URL(recipe.sourceUrl).href
      return (isCommonsTestImage || isPublisherTestImage) &&
        recipe.imageVerified !== true &&
        recipe.imageCommercialUseAllowed !== true
    }
    if (recipe.imageRightsStatus !== 'VerifiedCommercial' ||
      recipe.imageVerified !== true ||
      recipe.imageCommercialUseAllowed !== true ||
      recipe.imageProvider !== 'Wikimedia Commons') return false

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

    const completeAttribution = !recipe.imageAttributionRequired || Boolean(recipe.imageCreator)

    return validCommonsImageAndSource &&
      isCommercialLicense &&
      validLicenseUrl &&
      completeAttribution
  } catch {
    return false
  }
}

function isPublicLookingHttpsUrl(url) {
  if (url.protocol !== 'https:' || url.username || url.password || (url.port && url.port !== '443')) return false
  const host = url.hostname.toLowerCase().replace(/\.$/, '')
  if (host === 'localhost' || host.endsWith('.localhost') || host.endsWith('.local') || host.endsWith('.internal')) return false
  if (/^127\./.test(host) || /^10\./.test(host) || /^192\.168\./.test(host) || /^169\.254\./.test(host)) return false
  const private172 = host.match(/^172\.(\d{1,3})\./)
  return !private172 || Number(private172[1]) < 16 || Number(private172[1]) > 31
}

export function shouldUseRemoteRecipeImage(recipe, showRecipePhotos, imageFailed = false) {
  return Boolean(showRecipePhotos) && !imageFailed && hasValidRecipePhoto(recipe)
}
