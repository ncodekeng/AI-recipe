export const MAX_UPLOAD_IMAGE_BYTES = 5 * 1024 * 1024
export const MAX_HEIC_SOURCE_BYTES = 20 * 1024 * 1024
export const PHOTO_INPUT_ACCEPT = 'image/jpeg,image/png,image/gif,image/webp,image/heic,image/heif,.heic,.heif'

const AZURE_IMAGE_TYPES = new Set(['image/jpeg', 'image/png', 'image/gif', 'image/webp'])
const HEIC_IMAGE_TYPES = new Set([
  'image/heic',
  'image/heif',
  'image/heic-sequence',
  'image/heif-sequence',
])

export function isHeicPhoto(file) {
  const type = String(file?.type || '').toLowerCase()
  const name = String(file?.name || '').toLowerCase()
  return HEIC_IMAGE_TYPES.has(type) || /\.(heic|heif)$/.test(name)
}

export function isSupportedPhotoFile(file) {
  return AZURE_IMAGE_TYPES.has(String(file?.type || '').toLowerCase()) || isHeicPhoto(file)
}

export async function preparePhotoFile(file, convertHeic = convertHeicToJpeg) {
  if (!isSupportedPhotoFile(file)) {
    throw new Error('Use a JPEG, PNG, GIF, WebP, HEIC, or HEIF photo.')
  }

  if (!isHeicPhoto(file)) {
    if (file.size > MAX_UPLOAD_IMAGE_BYTES) {
      throw new Error('JPEG, PNG, GIF, and WebP photos must be no larger than 5 MB.')
    }
    return { file, convertedFromHeic: false }
  }

  if (file.size > MAX_HEIC_SOURCE_BYTES) {
    throw new Error('HEIC and HEIF photos must be no larger than 20 MB before conversion.')
  }

  const converted = await convertHeic(file)
  if (!converted || converted.type !== 'image/jpeg') {
    throw new Error('The HEIC photo could not be converted to JPEG.')
  }
  if (converted.size > MAX_UPLOAD_IMAGE_BYTES) {
    throw new Error('The converted HEIC photo is still larger than 5 MB.')
  }

  return { file: converted, convertedFromHeic: true }
}

async function convertHeicToJpeg(file) {
  try {
    const { heicTo } = await import('heic-to/csp')
    const bitmap = await heicTo({ blob: file, type: 'bitmap' })

    try {
      const maxDimension = 2400
      const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height))
      const width = Math.max(1, Math.round(bitmap.width * scale))
      const height = Math.max(1, Math.round(bitmap.height * scale))
      const canvas = document.createElement('canvas')
      canvas.width = width
      canvas.height = height
      const context = canvas.getContext('2d')
      if (!context) throw new Error('Canvas is unavailable.')

      context.fillStyle = '#fff'
      context.fillRect(0, 0, width, height)
      context.drawImage(bitmap, 0, 0, width, height)

      let jpegBlob = null
      for (const quality of [0.82, 0.7, 0.55, 0.42]) {
        jpegBlob = await canvasToBlob(canvas, quality)
        if (jpegBlob.size <= MAX_UPLOAD_IMAGE_BYTES) break
      }

      if (!jpegBlob) throw new Error('JPEG encoding failed.')
      const outputName = String(file.name || 'kitchen-photo.heic').replace(/\.(heic|heif)$/i, '') || 'kitchen-photo'
      return new File([jpegBlob], `${outputName}.jpg`, {
        type: 'image/jpeg',
        lastModified: file.lastModified || Date.now(),
      })
    } finally {
      bitmap.close?.()
    }
  } catch (error) {
    throw new Error('This HEIC/HEIF photo could not be prepared. Try exporting it as JPEG.', { cause: error })
  }
}

function canvasToBlob(canvas, quality) {
  return new Promise((resolve, reject) => {
    canvas.toBlob(
      (blob) => blob ? resolve(blob) : reject(new Error('JPEG encoding failed.')),
      'image/jpeg',
      quality,
    )
  })
}
