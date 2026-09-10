import test from 'node:test'
import assert from 'node:assert/strict'
import { hasValidRecipePhoto, shouldUseRemoteRecipeImage } from './recipePhotos.js'

const recipe = {
  title: 'Roast salmon with lemon',
  imageUrl: 'https://upload.wikimedia.org/salmon.jpg',
  imageSourceUrl: 'https://commons.wikimedia.org/wiki/File:Roast_salmon.jpg',
  imageLicenseType: 'CC BY-SA 4.0',
  imageLicenseUrl: 'https://creativecommons.org/licenses/by-sa/4.0/',
  imageAttributionRequirements: 'Credit Example Photographer; link to the source and license.',
  imageRightsStatus: 'VerifiedCommercial',
  imageProvider: 'Wikimedia Commons',
  imageCreator: 'Example Photographer',
  imageCommercialUseAllowed: true,
  imageAttributionRequired: true,
  imageVerified: true,
}

test('photo switch on selects an image with complete commercial license metadata', () => {
  assert.equal(hasValidRecipePhoto(recipe), true)
  assert.equal(shouldUseRemoteRecipeImage(recipe, true), true)
})

test('photo switch off never selects the remote image', () => {
  assert.equal(shouldUseRemoteRecipeImage(recipe, false), false)
})

test('failed or unsafe remote images select fallback artwork', () => {
  assert.equal(shouldUseRemoteRecipeImage(recipe, true, true), false)
  assert.equal(shouldUseRemoteRecipeImage({ ...recipe, imageUrl: 'javascript:alert(1)' }, true), false)
})

test('missing or noncommercial license metadata selects fallback artwork', () => {
  assert.equal(hasValidRecipePhoto({ ...recipe, imageSourceUrl: null }), false)
  assert.equal(hasValidRecipePhoto({ ...recipe, imageLicenseType: 'CC BY-NC 4.0' }), false)
  assert.equal(hasValidRecipePhoto({ ...recipe, imageLicenseUrl: null }), false)
})

test('explicit test-only status permits an unverified local test image', () => {
  const testImage = {
    ...recipe,
    imageLicenseType: 'Unverified test image',
    imageLicenseUrl: null,
    imageAttributionRequirements: 'Testing only — image rights were not verified.',
    imageRightsStatus: 'UnverifiedTestOnly',
    imageCommercialUseAllowed: false,
    imageVerified: false,
  }

  assert.equal(hasValidRecipePhoto(testImage), true)
})

test('development permits an exact publisher image marked testing-only', () => {
  const publisherImage = {
    ...recipe,
    sourceUrl: 'https://publisher.example.test/chicken-and-peppers',
    publisherPageVerified: true,
    imageUrl: 'https://cdn.publisher.example.test/chicken-and-peppers.jpg',
    imageSourceUrl: 'https://publisher.example.test/chicken-and-peppers',
    imageLicenseType: 'Unverified publisher image',
    imageLicenseUrl: null,
    imageAttributionRequirements: 'Testing only -- publisher image rights were not verified.',
    imageRightsStatus: 'UnverifiedTestOnly',
    imageProvider: 'Recipe publisher',
    imageCreator: null,
    imageCommercialUseAllowed: false,
    imageAttributionRequired: false,
    imageVerified: false,
  }

  assert.equal(hasValidRecipePhoto(publisherImage), true)
  assert.equal(hasValidRecipePhoto({ ...publisherImage, imageUrl: 'https://127.0.0.1/food.jpg' }), false)
  assert.equal(hasValidRecipePhoto({ ...publisherImage, publisherPageVerified: false }), false)
})
