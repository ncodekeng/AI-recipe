import { useEffect, useState } from 'react'
import { getRecipeArtwork } from './recipeArtwork.js'
import { shouldUseRemoteRecipeImage } from './recipePhotos.js'

export default function RecipeHeroImage({ recipe, showRecipePhotos = true, className = 'recipe-art', children }) {
  const [imageFailed, setImageFailed] = useState(false)
  const artwork = getRecipeArtwork(recipe)
  const useRealImage = shouldUseRemoteRecipeImage(recipe, showRecipePhotos, imageFailed)

  useEffect(() => {
    setImageFailed(false)
  }, [recipe.imageUrl, showRecipePhotos])

  return (
    <div
      className={`${className} ${useRealImage ? 'has-image' : 'has-fallback'}`}
      data-art-theme={artwork.theme}
    >
      {useRealImage ? (
        <img
          src={recipe.imageUrl}
          alt={recipe.title}
          loading="lazy"
          referrerPolicy="no-referrer"
          onError={() => setImageFailed(true)}
        />
      ) : (
        <div className="recipe-art-fallback" aria-hidden="true">
          {artwork.ingredients.map((ingredient) => (
            <span className="recipe-art-icon" key={ingredient.key}>{ingredient.icon}</span>
          ))}
        </div>
      )}
      {children}
    </div>
  )
}
