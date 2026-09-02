const INGREDIENT_VISUALS = [
  { key: 'chicken', icon: '🍗', aliases: ['chicken', 'chicken breast', 'chicken thigh'] },
  { key: 'beef', icon: '🥩', aliases: ['beef', 'steak', 'minced beef', 'ground beef'] },
  { key: 'pork', icon: '🥓', aliases: ['pork', 'bacon', 'ham', 'prosciutto'] },
  { key: 'salmon', icon: '🐟', aliases: ['salmon', 'salmon fillet'] },
  { key: 'fish', icon: '🐟', aliases: ['fish', 'cod', 'tuna', 'haddock', 'tilapia'] },
  { key: 'shrimp', icon: '🦐', aliases: ['shrimp', 'shrimps', 'prawn', 'prawns'] },
  { key: 'egg', icon: '🥚', aliases: ['egg', 'eggs'] },
  { key: 'tomato', icon: '🍅', aliases: ['tomato', 'tomatoes', 'cherry tomato', 'cherry tomatoes'] },
  { key: 'potato', icon: '🥔', aliases: ['potato', 'potatoes', 'sweet potato', 'sweet potatoes'] },
  { key: 'onion', icon: '🧅', aliases: ['onion', 'onions', 'red onion', 'spring onion', 'shallot', 'shallots'] },
  { key: 'garlic', icon: '🧄', aliases: ['garlic', 'garlic clove', 'garlic cloves'] },
  { key: 'bell pepper', icon: '🫑', aliases: ['bell pepper', 'bell peppers', 'capsicum', 'capsicums', 'sweet pepper', 'sweet peppers', 'red pepper', 'green pepper', 'yellow pepper'] },
  { key: 'carrot', icon: '🥕', aliases: ['carrot', 'carrots'] },
  { key: 'broccoli', icon: '🥦', aliases: ['broccoli'] },
  { key: 'spinach', icon: '🥬', aliases: ['spinach', 'baby spinach'] },
  { key: 'mushroom', icon: '🍄', aliases: ['mushroom', 'mushrooms'] },
  { key: 'avocado', icon: '🥑', aliases: ['avocado', 'avocados'] },
  { key: 'lemon', icon: '🍋', aliases: ['lemon', 'lemons'] },
  { key: 'lime', icon: '🍋‍🟩', aliases: ['lime', 'limes'] },
  { key: 'bread', icon: '🍞', aliases: ['bread', 'toast', 'toasted bread', 'baguette', 'bruschetta', 'breadcrumbs'] },
  { key: 'rice', icon: '🍚', aliases: ['rice', 'risotto'] },
  { key: 'pasta', icon: '🍝', aliases: ['pasta', 'spaghetti', 'linguine', 'penne', 'noodles'] },
  { key: 'cheese', icon: '🧀', aliases: ['cheese', 'cheddar', 'mozzarella', 'parmesan', 'feta'] },
]

const PANTRY_STAPLES = [
  'salt',
  'black pepper',
  'olive oil',
  'vegetable oil',
  'water',
  'seasoning',
]

function normalize(value) {
  return String(value || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
}

function phraseIndex(text, phrase) {
  const paddedText = ` ${normalize(text)} `
  const paddedPhrase = ` ${normalize(phrase)} `
  return paddedText.indexOf(paddedPhrase)
}

function visualsInText(text) {
  return INGREDIENT_VISUALS
    .map((visual) => {
      const positions = visual.aliases
        .map((alias) => phraseIndex(text, alias))
        .filter((position) => position >= 0)
      return positions.length ? { ...visual, position: Math.min(...positions) } : null
    })
    .filter(Boolean)
    .sort((left, right) => left.position - right.position)
}

function ingredientName(item) {
  return typeof item === 'string' ? item : item?.name
}

function isPantryStaple(value) {
  const normalized = normalize(value)
  return PANTRY_STAPLES.some((staple) => normalized === staple || normalized.startsWith(`${staple} `))
}

function hash(value) {
  let result = 2166136261
  for (const character of value) {
    result ^= character.charCodeAt(0)
    result = Math.imul(result, 16777619)
  }
  return result >>> 0
}

export function getRecipeArtwork(recipe) {
  const selected = []
  const usedKeys = new Set()
  const addVisual = (visual, label = visual.key) => {
    if (selected.length >= 3 || usedKeys.has(visual.key)) return
    usedKeys.add(visual.key)
    selected.push({ key: visual.key, icon: visual.icon, label })
  }

  visualsInText(recipe?.title).forEach((visual) => addVisual(visual))

  const ingredientNames = Array.isArray(recipe?.ingredients)
    ? recipe.ingredients.map(ingredientName).filter(Boolean)
    : []
  ingredientNames.forEach((name) => {
    const visual = visualsInText(name)[0]
    if (visual) addVisual(visual, name)
  })

  ingredientNames
    .filter((name) => !isPantryStaple(name))
    .forEach((name, index) => {
      if (selected.length >= 3) return
      const genericKey = `generic-${normalize(name) || index}`
      addVisual({ key: genericKey, icon: '🍲' }, name)
    })

  if (selected.length === 0) {
    selected.push({ key: 'generic-recipe', icon: '🍲', label: 'Recipe' })
  }

  const category = [recipe?.cuisine, ...(Array.isArray(recipe?.tags) ? recipe.tags : [])]
    .filter(Boolean)
    .join(' ')
  const seed = `${recipe?.title || ''}|${selected.map((item) => item.key).join('|')}|${category}`

  return {
    ingredients: selected,
    theme: hash(seed) % 6,
  }
}

export { INGREDIENT_VISUALS }
