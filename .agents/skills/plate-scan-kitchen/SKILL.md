---
name: plate-scan-kitchen
description: Implement, debug, or review PLATE's kitchen-photo upload and photo-to-ingredient recognition workflow. Use for camera/gallery input, image validation, Azure vision analysis, irrelevant-photo filtering, frozen-food detection, or scan loading states; do not use for recipe catalogue work.
---

# PLATE kitchen scan

Keep this stage focused on converting temporary kitchen photos into an editable ingredient list.

## Inspect first

- `client/src/App.jsx` and `client/src/api.js`
- `server/Recipe.Api/Controllers/IngredientsController.cs`
- `server/Recipe.Api/Services/AzureOpenAiClient.cs`, `RecipeAiService.cs`, `ImageFileValidator.cs`, and `DemoFoodAiService.cs`
- `server/Recipe.Api/Models/IngredientModels.cs`

## Preserve these invariants

1. Accept one to six JPEG, PNG, GIF, or WebP images, with a maximum of 5 MB per image and signature validation on the server.
2. Keep photo bytes in memory only for the active request. Do not add photo persistence, logs containing image data, or history entries containing photos.
3. Use Azure OpenAI vision to inspect actual pixels in live mode. Demo recognition is allowed only as the explicitly configured prototype fallback.
4. Return ingredient name, estimated quantity, confidence, source image, and classification data needed for irrelevant or frozen-item handling.
5. Treat recognition as a draft. The next stage must let the user correct every ingredient and quantity.
6. Show an animated, accessible loading state for slow scans and preserve a useful error when no food is found.
7. Never generate recipes in the scan provider. Recipe discovery belongs to the sourced catalogue stage.

## Verify

Run the backend tests, frontend tests, and production frontend build. Exercise a multipart upload when the request contract changes.
