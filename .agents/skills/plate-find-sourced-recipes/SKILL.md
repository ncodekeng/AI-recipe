---
name: plate-find-sourced-recipes
description: Implement, debug, or review PLATE's real online recipe search and recommendation results. Use for Azure Responses web search, Edamam integration, citation enforcement, ingredient matching, ranking, provider photos, recipe details, or recipe-result caching; never invent recipes.
---

# PLATE sourced recipe search

Return useful recipes that exist online and remain traceable to their original publisher.

## Inspect first

- `server/Recipe.Api/Services/AzureGroundedRecipeClient.cs`, `EdamamRecipeClient.cs`, `RecipeCatalogService.cs`, `RecipeRankingService.cs`, `RecipeSafetyValidator.cs`, and `RecipeSearchCache.cs`
- `server/Recipe.Api/Models/RecipeModels.cs` and `Options/RecipeCatalogOptions.cs`
- `client/src/App.jsx`, `RecipeHeroImage.jsx`, `recipePhotos.js`, and `recipeArtwork.js`
- `README.md` before changing provider or cache behavior

## Non-negotiable rules

1. Use Azure Responses API with forced `web_search` by default, or the explicitly configured Edamam catalogue. Azure may structure source metadata and add a presentation summary, but must not create or complete the canonical recipe.
2. For Azure, accept a recipe only when its exact valid HTTPS publisher URL appears in the response's actual web-search sources or citation annotations. Never trust a URL merely because the model wrote it in JSON. For Edamam, require its valid HTTPS source URL. When credentials, search, citations, the provider, or safe results are unavailable, return a clear error without substitution.
3. Preserve publisher attribution and link to the source for the full method.
4. Apply deterministic allergen, diet, and avoid-list validation after the provider response.
5. Normalize common ingredient variants, exclude ordinary pantry basics from missing counts, and calculate available/missing structured ingredients. If any result needs a purchase, the Top Pick must have the fewest positive missing-item count; prefer an equally close result that was not recently shown.
6. Use a provider's HTTPS image only when it supplies one with suitable rights. Azure web search does not guarantee a licensed image field, so use recipe-derived fallback artwork. Do not mount any remote image when photo display is disabled.
7. Make the first ranked result the Top Pick and keep available and missing ingredients visible on cards and details.

## Cache boundary

Cache keys must include the active provider, normalized ingredients, allergens, avoided ingredients, recent result IDs, diet, time, and servings. Keep full-result caching disabled unless the active provider/search contract explicitly permits every retained field and serving model. `ProviderPermissionConfirmed` is mandatory; never bypass it to improve performance.

## Azure contract

Read [references/azure-web-search.md](references/azure-web-search.md) before changing the Azure request, response parser, or citation validation.

## Verify

Test forced web search, rejection of uncited model URLs, provider mapping, safety filtering, halal wine suppression, normalized matching, ranking, photo fallback behavior, cache isolation between providers and safety settings, and provider failure paths.
