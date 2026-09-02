---
name: plate-find-sourced-recipes
description: Implement, debug, or review PLATE's real online recipe search and recommendation results. Use for Edamam integration, source enforcement, ingredient matching, ranking, provider photos, recipe details, or recipe-result caching; never use an AI model to invent recipes.
---

# PLATE sourced recipe search

Return useful recipes that exist online and remain traceable to their original publisher.

## Inspect first

- `server/Recipe.Api/Services/EdamamRecipeClient.cs`, `RecipeCatalogService.cs`, `RecipeRankingService.cs`, `RecipeSafetyValidator.cs`, and `RecipeSearchCache.cs`
- `server/Recipe.Api/Models/RecipeModels.cs` and `Options/RecipeCatalogOptions.cs`
- `client/src/App.jsx`, `RecipeHeroImage.jsx`, `recipePhotos.js`, and `recipeArtwork.js`
- `README.md` before changing provider or cache behavior

## Non-negotiable rules

1. Use the configured online recipe catalogue. Do not ask Azure OpenAI, the demo scanner, or another language model to create or complete recipes.
2. Require a valid HTTPS original-publisher URL for every result. When credentials, the provider, or safe results are unavailable, return a clear error without substitution.
3. Preserve publisher attribution and link to the source for the full method.
4. Apply deterministic allergen, diet, and avoid-list validation after the provider response.
5. Normalize common ingredient variants, exclude ordinary pantry basics from missing counts, and calculate available/missing structured ingredients. If any result needs a purchase, the Top Pick must have the fewest positive missing-item count; prefer an equally close result that was not recently shown.
6. Use the provider's HTTPS image when photo display is enabled. Do not mount the remote image when disabled; use recipe-derived fallback artwork for missing or failed images.
7. Make the first ranked result the Top Pick and keep available and missing ingredients visible on cards and details.

## Cache boundary

Cache keys must include normalized ingredients, allergens, avoided ingredients, recent result IDs, diet, time, and servings. Keep full-result caching disabled unless the active provider contract explicitly permits every retained field and serving model. `ProviderPermissionConfirmed` is mandatory; never bypass it to improve performance.

## Verify

Test source rejection, provider mapping, safety filtering, normalized matching, ranking, photo fallback behavior, cache isolation between safety settings, and the provider failure path.
