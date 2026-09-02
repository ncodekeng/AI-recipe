function asIngredient(item) {
  if (typeof item === 'string') {
    return { name: item, amount: '' }
  }

  return {
    name: String(item?.name || '').trim(),
    amount: String(item?.amount || '').trim(),
    quantity: Number.isFinite(Number(item?.quantity)) && Number(item.quantity) > 0
      ? Number(item.quantity)
      : null,
    unit: typeof item?.unit === 'string' && item.unit.trim() ? item.unit.trim() : null,
  }
}

export function getMissingIngredients(recipe) {
  return Array.isArray(recipe?.missingIngredients)
    ? recipe.missingIngredients.map(asIngredient).filter((item) => item.name)
    : []
}

export function buildGroceryBasketPayload(recipe) {
  return {
    recipeId: recipe?.id,
    ingredients: getMissingIngredients(recipe),
  }
}

export function canPrepareGroceryBasket(recipe) {
  return Boolean(recipe?.id) && getMissingIngredients(recipe).length > 0
}

export function formatShoppingList(ingredients) {
  return ingredients
    .map((item) => `- ${[item.amount, item.name].filter(Boolean).join(' ')}`)
    .join('\n')
}
