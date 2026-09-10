import test from 'node:test'
import assert from 'node:assert/strict'
import {
  isHeicPhoto,
  isSupportedPhotoFile,
  MAX_HEIC_SOURCE_BYTES,
  MAX_UPLOAD_IMAGE_BYTES,
  preparePhotoFile,
} from './photoFiles.js'

function photo(name, type, size = 1024) {
  return { name, type, size, lastModified: 1 }
}

test('HEIC and HEIF photos are recognized by MIME type or extension', () => {
  assert.equal(isHeicPhoto(photo('kitchen-photo', 'image/heic')), true)
  assert.equal(isHeicPhoto(photo('kitchen-photo.HEIF', '')), true)
  assert.equal(isHeicPhoto(photo('kitchen-photo.jpg', 'image/jpeg')), false)
})

test('supported photo selection includes Azure formats and HEIC', () => {
  assert.equal(isSupportedPhotoFile(photo('fridge.jpg', 'image/jpeg')), true)
  assert.equal(isSupportedPhotoFile(photo('fridge.webp', 'image/webp')), true)
  assert.equal(isSupportedPhotoFile(photo('fridge.heic', 'application/octet-stream')), true)
  assert.equal(isSupportedPhotoFile(photo('notes.txt', 'text/plain')), false)
})

test('Azure-ready photos pass through without conversion', async () => {
  const jpeg = photo('fridge.jpg', 'image/jpeg')
  const result = await preparePhotoFile(jpeg, async () => assert.fail('converter should not run'))

  assert.equal(result.file, jpeg)
  assert.equal(result.convertedFromHeic, false)
})

test('HEIC photos are converted before upload', async () => {
  const heic = photo('fridge.heic', 'image/heic')
  const jpeg = photo('fridge.jpg', 'image/jpeg', 2048)
  const result = await preparePhotoFile(heic, async (received) => {
    assert.equal(received, heic)
    return jpeg
  })

  assert.equal(result.file, jpeg)
  assert.equal(result.convertedFromHeic, true)
})

test('oversized source and converted photos are rejected', async () => {
  await assert.rejects(
    preparePhotoFile(photo('large.png', 'image/png', MAX_UPLOAD_IMAGE_BYTES + 1)),
    /no larger than 5 MB/,
  )
  await assert.rejects(
    preparePhotoFile(photo('large.heic', 'image/heic', MAX_HEIC_SOURCE_BYTES + 1)),
    /no larger than 20 MB/,
  )
  await assert.rejects(
    preparePhotoFile(
      photo('fridge.heic', 'image/heic'),
      async () => photo('fridge.jpg', 'image/jpeg', MAX_UPLOAD_IMAGE_BYTES + 1),
    ),
    /still larger than 5 MB/,
  )
})
