---
name: plate-review-pantry
description: Implement or review PLATE's editable pantry and preference step. Use for detected-ingredient corrections, quantities, servings, cooking time, allergens, avoided ingredients, halal-style or kosher-style settings, and local preference persistence.
---

# PLATE pantry review

Make the corrected pantry and current preferences the only source of truth for a recipe search.

## Inspect first

- `client/src/App.jsx`, `client/src/storage.js`, and their tests
- `server/Recipe.Api/Models/RecipeModels.cs`
- `server/Recipe.Api/Services/FoodSafetyRules.cs`, `IngredientNormalizer.cs`, and `RecipeSafetyValidator.cs`

## Workflow

1. Preserve add, edit, quantity correction, and removal for every detected ingredient.
2. Send only the latest non-empty ingredient names and quantities to the API.
3. Preserve all fourteen supported UK allergen selections, custom avoided ingredients, diet, maximum cooking time, and servings.
4. Invalidate stale recipe results whenever an ingredient or safety-relevant preference changes.
5. Persist the corrected ingredient list as browser-local Kitchen Memory alongside preferences, but never persist uploaded photo bytes. Merge a new scan by visible ingredient name and let the user remove stale items.
6. Treat model/provider filters as hints. Keep deterministic server-side allergen, diet, and avoid-list validation as the safety boundary.
7. Use “halal-style” and “kosher-style” wording unless verified certification and preparation guarantees exist.
8. Do not claim the app can guarantee safety from substitutions, labels, manufacturing, or cross-contamination.

## Verify

Test preference persistence, corrected request payloads, stale-result clearing, and deterministic conflict rejection.
