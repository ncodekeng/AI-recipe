import { useEffect, useState } from 'react'
import { getRecipeArtwork } from './recipeArtwork.js'

export default function RecipeHeroImage({ recipe, children }) {
  const [imageFailed, setImageFailed] = useState(false)
  const artwork = getRecipeArtwork(recipe)
  const useRealImage = Boolean(recipe.imageUrl) && !imageFailed

  useEffect(() => {
    setImageFailed(false)
  }, [recipe.imageUrl])

  return (
    <div
      className={`recipe-art ${useRealImage ? 'has-image' : 'has-fallback'}`}
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
