---
name: plate-grocery-handoff
description: Implement or review PLATE's missing-ingredient shopping and delivery handoff. Use for Deliveroo or another grocery platform, basket payloads, checkout links, missing-item UI, or partner-access fallbacks; do not invent undocumented partner endpoints.
---

# PLATE grocery handoff

Keep provider-specific shopping behavior behind the backend contract and keep every claim to the user truthful.

## Inspect first

- `client/src/groceryBasket.js`, `client/src/api.js`, and the grocery UI in `client/src/App.jsx`
- `server/Recipe.Api/Controllers/GroceryController.cs`
- `server/Recipe.Api/Models/GroceryModels.cs`
- `server/Recipe.Api/Services/IGroceryBasketService.cs` and `DeliverooBasketService.cs`

## Workflow

1. Derive the shopping payload only from the selected recipe's server-calculated `missingIngredients`.
2. Preserve amount, quantity, and unit when the provider can use them. De-duplicate normalized names and exclude ordinary pantry basics.
3. When nothing is missing, show that the user is ready and do not render a basket action.
4. Keep platform calls on the server so credentials and commercial rules never enter React.
5. Do not guess endpoint URLs, scopes, merchant identifiers, basket schemas, or checkout behavior. Implement live checkout only from approved partner documentation and credentials.
6. Until live access exists, return `basketCreated: false`, no checkout URL, a copyable shopping list, and an honest manual handoff link.
7. A grocery failure must not remove or hide the recipe.

## Verify

Test missing-only payloads, zero-missing behavior, normalization, validation failures, manual-handoff truthfulness, and any documented live provider response mapping.
