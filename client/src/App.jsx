import { useEffect, useMemo, useRef, useState } from 'react'
import { analyzePhotos, generateRecipes, getStatus } from './api.js'

const ALLERGENS = [
  'Peanuts',
  'Tree nuts',
  'Milk',
  'Eggs',
  'Wheat',
  'Soy',
  'Fish',
  'Shellfish',
  'Sesame',
]

const DIETARY_OPTIONS = ['Anything', 'Vegetarian', 'Vegan', 'Pescatarian', 'Gluten-free']

const RECIPE_EMOJI = {
  coral: ['🍅', '🌿', '🍳'],
  saffron: ['🥕', '🫑', '✨'],
  sage: ['🥬', '🍋', '🥣'],
}

function Icon({ name, size = 20, strokeWidth = 1.8 }) {
  const paths = {
    camera: <><path d="M14.5 5 13 3H7L5.5 5H3a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2.5Z"/><circle cx="10" cy="12" r="3.25"/></>,
    upload: <><path d="M10 14V3"/><path d="m6 7 4-4 4 4"/><path d="M4 11H2.5A1.5 1.5 0 0 0 1 12.5v4A1.5 1.5 0 0 0 2.5 18h15a1.5 1.5 0 0 0 1.5-1.5v-4a1.5 1.5 0 0 0-1.5-1.5H16"/></>,
    sparkles: <><path d="m10 2 1.1 3.1L14 6.5l-2.9 1.4L10 11 8.9 7.9 6 6.5l2.9-1.4L10 2Z"/><path d="m16 11 .8 2.2L19 14l-2.2.8L16 17l-.8-2.2L13 14l2.2-.8L16 11Z"/><path d="m4 12 .7 1.8 1.8.7-1.8.7L4 17l-.7-1.8-1.8-.7 1.8-.7L4 12Z"/></>,
    check: <path d="m4 10 4 4 8-8"/>,
    close: <><path d="m4 4 12 12"/><path d="M16 4 4 16"/></>,
    plus: <><path d="M10 3v14"/><path d="M3 10h14"/></>,
    trash: <><path d="M3 5h14"/><path d="M8 5V3h4v2"/><path d="m5 5 1 13h8l1-13"/><path d="M8 9v5M12 9v5"/></>,
    clock: <><circle cx="10" cy="10" r="8"/><path d="M10 5v5l3 2"/></>,
    users: <><path d="M6 10a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z"/><path d="M1 17c.4-3.2 2-5 5-5s4.6 1.8 5 5"/><path d="M14 10a2.5 2.5 0 1 0 0-5"/><path d="M13 12c3 0 4.7 1.7 5 4"/></>,
    arrow: <><path d="M3 10h14"/><path d="m12 5 5 5-5 5"/></>,
    chevron: <path d="m5 8 5 5 5-5"/>,
    shield: <><path d="M10 2 3.5 4.5v5.2c0 4.1 2.7 6.9 6.5 8.3 3.8-1.4 6.5-4.2 6.5-8.3V4.5L10 2Z"/><path d="m7 10 2 2 4-4"/></>,
    edit: <><path d="m13.5 3.5 3 3L7 16H4v-3l9.5-9.5Z"/><path d="m11.5 5.5 3 3"/></>,
    image: <><rect x="2" y="3" width="16" height="14" rx="2"/><circle cx="7" cy="8" r="1.5"/><path d="m3 15 4-4 3 3 2-2 5 5"/></>,
    leaf: <><path d="M17.5 2.5C10 3 4.5 6.5 4.5 12c0 3 2.2 5 5 5 5.5 0 8-6 8-14.5Z"/><path d="M3 18c2-5 5.5-8.5 10.5-11.5"/></>,
  }

  return (
    <svg
      aria-hidden="true"
      width={size}
      height={size}
      viewBox="0 0 20 20"
      fill="none"
      stroke="currentColor"
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {paths[name]}
    </svg>
  )
}

function Stepper({ currentStep }) {
  const steps = [
    ['1', 'Show your kitchen'],
    ['2', 'Review ingredients'],
    ['3', 'Choose a recipe'],
  ]

  return (
    <ol className="stepper" aria-label="Recipe creation progress">
      {steps.map(([number, label], index) => {
        const step = index + 1
        const complete = step < currentStep
        const active = step === currentStep
        return (
          <li className={active ? 'active' : complete ? 'complete' : ''} key={number}>
            <span className="step-number">{complete ? <Icon name="check" size={15} strokeWidth={2.4} /> : number}</span>
            <span>{label}</span>
          </li>
        )
      })}
    </ol>
  )
}

function PhotoUploader({ photos, onFiles, onRemove, busy }) {
  const [isDragging, setIsDragging] = useState(false)
  const inputRef = useRef(null)

  function selectFiles(fileList) {
    const selected = Array.from(fileList || []).filter((file) => file.type.startsWith('image/'))
    if (selected.length) onFiles(selected)
  }

  return (
    <div>
      <div
        className={`drop-zone ${isDragging ? 'dragging' : ''}`}
        onDragEnter={(event) => { event.preventDefault(); setIsDragging(true) }}
        onDragOver={(event) => event.preventDefault()}
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget)) setIsDragging(false)
        }}
        onDrop={(event) => {
          event.preventDefault()
          setIsDragging(false)
          selectFiles(event.dataTransfer.files)
        }}
      >
        <input
          ref={inputRef}
          className="sr-only"
          type="file"
          accept="image/*"
          capture="environment"
          multiple
          disabled={busy || photos.length >= 6}
          onChange={(event) => {
            selectFiles(event.target.files)
            event.target.value = ''
          }}
        />
        <button
          className="drop-zone-button"
          type="button"
          onClick={() => inputRef.current?.click()}
          disabled={busy || photos.length >= 6}
        >
          <span className="upload-icon"><Icon name="camera" size={28} /></span>
          <strong>Take a photo or upload</strong>
          <span>Fridge, cupboard or countertop</span>
          <span className="soft-pill"><Icon name="upload" size={14} /> Choose photos</span>
        </button>
      </div>

      {photos.length > 0 && (
        <div className="photo-list" aria-label="Selected kitchen photos">
          {photos.map((photo, index) => (
            <figure className="photo-card" key={photo.id}>
              <img src={photo.url} alt={`Kitchen upload ${index + 1}`} />
              <figcaption>Photo {index + 1}</figcaption>
              <button
                type="button"
                aria-label={`Remove photo ${index + 1}`}
                onClick={() => onRemove(photo.id)}
                disabled={busy}
              >
                <Icon name="close" size={14} strokeWidth={2.4} />
              </button>
            </figure>
          ))}
          {photos.length < 6 && (
            <button className="add-photo" type="button" onClick={() => inputRef.current?.click()} disabled={busy}>
              <Icon name="plus" size={22} />
              <span>Add another</span>
            </button>
          )}
        </div>
      )}
      <p className="upload-hint"><Icon name="shield" size={15} /> Your photos are used only to identify food and are not stored by this prototype.</p>
    </div>
  )
}

function IngredientEditor({ ingredients, onChange, onRemove, onAdd }) {
  return (
    <div className="ingredient-editor">
      <div className="ingredient-list">
        {ingredients.map((ingredient) => (
          <div className="ingredient-row" key={ingredient.id}>
            <span className="confidence-dot" data-confidence={ingredient.confidence < 75 ? 'low' : 'high'} />
            <div className="ingredient-fields">
              <label>
                <span className="sr-only">Ingredient name</span>
                <input
                  value={ingredient.name}
                  onChange={(event) => onChange(ingredient.id, 'name', event.target.value)}
                />
              </label>
              <label>
                <span className="sr-only">Estimated quantity</span>
                <input
                  className="quantity-input"
                  value={ingredient.quantity}
                  onChange={(event) => onChange(ingredient.id, 'quantity', event.target.value)}
                />
              </label>
            </div>
            {ingredient.confidence > 0 && <span className="confidence">{ingredient.confidence}% sure</span>}
            <button className="icon-button" type="button" onClick={() => onRemove(ingredient.id)} aria-label={`Remove ${ingredient.name}`}>
              <Icon name="trash" size={17} />
            </button>
          </div>
        ))}
      </div>
      <button className="text-button" type="button" onClick={onAdd}>
        <Icon name="plus" size={17} strokeWidth={2.2} /> Add an ingredient
      </button>
    </div>
  )
}

function AllergenPicker({ selected, onToggle }) {
  return (
    <div className="allergen-picker">
      {ALLERGENS.map((allergen) => {
        const active = selected.includes(allergen)
        return (
          <button
            key={allergen}
            className={active ? 'selected' : ''}
            type="button"
            aria-pressed={active}
            onClick={() => onToggle(allergen)}
          >
            {active && <Icon name="check" size={14} strokeWidth={2.5} />}
            {allergen}
          </button>
        )
      })}
    </div>
  )
}

function RecipeCard({ recipe, onOpen }) {
  const emoji = RECIPE_EMOJI[recipe.accent] || RECIPE_EMOJI.coral
  return (
    <article className="recipe-card">
      <div className={`recipe-art ${recipe.accent}`}>
        <span>{emoji[0]}</span><span>{emoji[1]}</span><span>{emoji[2]}</span>
        <div className="match-badge">{recipe.ingredientMatch}% match</div>
      </div>
      <div className="recipe-card-body">
        <div className="recipe-tags">
          {recipe.tags.slice(0, 3).map((tag) => <span key={tag}>{tag}</span>)}
        </div>
        <h3>{recipe.title}</h3>
        <p>{recipe.description}</p>
        <div className="recipe-meta">
          <span><Icon name="clock" size={16} /> {recipe.cookingMinutes} min</span>
          <span><Icon name="users" size={17} /> {recipe.servings} servings</span>
          <span>{recipe.difficulty}</span>
        </div>
        <button type="button" className="recipe-open" onClick={() => onOpen(recipe)}>
          View recipe <Icon name="arrow" size={17} />
        </button>
      </div>
    </article>
  )
}

function RecipeModal({ recipe, onClose, safetyNote }) {
  useEffect(() => {
    function closeOnEscape(event) {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', closeOnEscape)
    document.body.classList.add('modal-open')
    return () => {
      document.removeEventListener('keydown', closeOnEscape)
      document.body.classList.remove('modal-open')
    }
  }, [onClose])

  const emoji = RECIPE_EMOJI[recipe.accent] || RECIPE_EMOJI.coral
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <article className="recipe-modal" role="dialog" aria-modal="true" aria-labelledby="recipe-title">
        <button className="modal-close" type="button" onClick={onClose} aria-label="Close recipe">
          <Icon name="close" size={19} />
        </button>
        <div className={`modal-hero ${recipe.accent}`}>
          <div className="modal-emoji">{emoji.join(' ')}</div>
          <span>{recipe.cuisine}</span>
          <h2 id="recipe-title">{recipe.title}</h2>
          <div className="modal-meta">
            <span><Icon name="clock" size={17} /> {recipe.cookingMinutes} min</span>
            <span><Icon name="users" size={18} /> {recipe.servings} servings</span>
            <span>{recipe.difficulty}</span>
          </div>
        </div>
        <div className="modal-content">
          <p className="modal-description">{recipe.description}</p>
          <div className="recipe-columns">
            <section>
              <p className="eyebrow">What you'll need</p>
              <ul className="modal-ingredients">
                {recipe.ingredients.map((item, index) => (
                  <li key={`${item.name}-${index}`}><span>{item.name}</span><strong>{item.amount}</strong></li>
                ))}
              </ul>
            </section>
            <section>
              <p className="eyebrow">Method</p>
              <ol className="method-list">
                {recipe.steps.map((step, index) => (
                  <li key={index}><span>{index + 1}</span><p>{step}</p></li>
                ))}
              </ol>
            </section>
          </div>
          <div className="safety-note"><Icon name="shield" size={18} /><p>{safetyNote}</p></div>
        </div>
      </article>
    </div>
  )
}

export default function App() {
  const [photos, setPhotos] = useState([])
  const [ingredients, setIngredients] = useState([])
  const [allergens, setAllergens] = useState([])
  const [dietaryPreference, setDietaryPreference] = useState('Anything')
  const [maxCookingMinutes, setMaxCookingMinutes] = useState(45)
  const [servings, setServings] = useState(2)
  const [recipes, setRecipes] = useState([])
  const [selectedRecipe, setSelectedRecipe] = useState(null)
  const [safetyNote, setSafetyNote] = useState('')
  const [provider, setProvider] = useState('Checking…')
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')

  const reviewRef = useRef(null)
  const resultsRef = useRef(null)
  const photoUrlsRef = useRef(new Set())

  const currentStep = recipes.length > 0 ? 3 : ingredients.length > 0 ? 2 : 1
  const validIngredients = useMemo(
    () => ingredients.filter((item) => item.name.trim()),
    [ingredients],
  )

  useEffect(() => {
    const controller = new AbortController()
    getStatus(controller.signal)
      .then((status) => setProvider(status.aiProvider))
      .catch(() => setProvider('API offline'))
    return () => controller.abort()
  }, [])

  useEffect(() => () => {
    photoUrlsRef.current.forEach((url) => URL.revokeObjectURL(url))
  }, [])

  function addPhotos(files) {
    setError('')
    const remaining = 6 - photos.length
    const accepted = files.slice(0, remaining)
    if (files.length > remaining) setError('You can add up to 6 photos at a time.')
    const additions = accepted.map((file) => {
      const url = URL.createObjectURL(file)
      photoUrlsRef.current.add(url)
      return { id: crypto.randomUUID(), file, url }
    })
    setPhotos((current) => [...current, ...additions])
    setIngredients([])
    setRecipes([])
  }

  function removePhoto(id) {
    setPhotos((current) => {
      const removed = current.find((photo) => photo.id === id)
      if (removed) {
        URL.revokeObjectURL(removed.url)
        photoUrlsRef.current.delete(removed.url)
      }
      return current.filter((photo) => photo.id !== id)
    })
    setIngredients([])
    setRecipes([])
  }

  async function handleAnalyze() {
    if (!photos.length) return
    setBusy('analyzing')
    setError('')
    setNotice('')
    try {
      const result = await analyzePhotos(photos.map((photo) => photo.file))
      setIngredients(result.ingredients)
      setProvider(result.provider)
      setNotice(result.notice || '')
      setRecipes([])
      requestAnimationFrame(() => reviewRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy('')
    }
  }

  function updateIngredient(id, field, value) {
    setIngredients((current) => current.map((item) => item.id === id ? { ...item, [field]: value } : item))
    setRecipes([])
  }

  function addIngredient() {
    setIngredients((current) => [
      ...current,
      { id: crypto.randomUUID(), name: '', quantity: 'as needed', confidence: 0, sourceImage: 'Added manually' },
    ])
  }

  function toggleAllergen(allergen) {
    setAllergens((current) => current.includes(allergen)
      ? current.filter((item) => item !== allergen)
      : [...current, allergen])
    setRecipes([])
  }

  async function handleGenerate() {
    if (!validIngredients.length) return
    setBusy('generating')
    setError('')
    setNotice('')
    try {
      const result = await generateRecipes({
        ingredients: validIngredients.map(({ name, quantity }) => ({ name, quantity })),
        allergens,
        dietaryPreference,
        maxCookingMinutes: Number(maxCookingMinutes),
        servings: Number(servings),
      })
      setRecipes(result.recipes)
      setSafetyNote(result.safetyNote)
      setProvider(result.provider)
      setNotice(result.notice || '')
      requestAnimationFrame(() => resultsRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy('')
    }
  }

  return (
    <>
      <header className="site-header">
        <a className="brand" href="#top" aria-label="Mise home">
          <span className="brand-mark"><Icon name="leaf" size={23} strokeWidth={2} /></span>
          <span>mise</span>
        </a>
        <nav aria-label="Main navigation">
          <a href="#how-it-works">How it works</a>
          <span className={`provider-badge ${provider === 'Azure OpenAI' ? 'live' : ''}`}>
            <span /> {provider}
          </span>
        </nav>
      </header>

      <main id="top">
        <section className="hero">
          <div className="hero-copy">
            <p className="eyebrow"><Icon name="sparkles" size={17} /> Your kitchen, reimagined</p>
            <h1>Make something<br /><em>wonderful.</em></h1>
            <p className="hero-lead">Show us what you have. We’ll turn everyday ingredients into thoughtful recipes made for you.</p>
          </div>
          <div className="hero-note" aria-hidden="true">
            <span className="note-leaf">🌿</span>
            <p>Tonight’s little reminder</p>
            <strong>The best meal might already be in your kitchen.</strong>
            <div className="scribble">cook what you have ↗</div>
          </div>
        </section>

        <section className="creator" id="how-it-works">
          <Stepper currentStep={currentStep} />

          <div className="work-card">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Step one</p>
                <h2>What’s in your kitchen?</h2>
                <p>Add up to six clear photos. Different angles help us spot more ingredients.</p>
              </div>
              <span className="section-number">01</span>
            </div>
            <PhotoUploader photos={photos} onFiles={addPhotos} onRemove={removePhoto} busy={Boolean(busy)} />
            <div className="card-action">
              <span>{photos.length ? `${photos.length} photo${photos.length === 1 ? '' : 's'} ready` : 'Add a photo to begin'}</span>
              <button className="primary-button" type="button" disabled={!photos.length || Boolean(busy)} onClick={handleAnalyze}>
                {busy === 'analyzing' ? <><span className="spinner" /> Looking closely…</> : <>Find my ingredients <Icon name="arrow" size={18} /></>}
              </button>
            </div>
          </div>

          {error && <div className="alert error" role="alert"><span>!</span><p>{error}</p></div>}
          {notice && <div className="alert info" role="status"><Icon name="sparkles" size={19} /><p>{notice}</p></div>}

          {ingredients.length > 0 && (
            <section className="review-section" ref={reviewRef}>
              <div className="section-heading outside">
                <div>
                  <p className="eyebrow">Step two</p>
                  <h2>We found {ingredients.length} ingredients</h2>
                  <p>AI can make mistakes. Tap any name or quantity to correct it before we cook.</p>
                </div>
                <span className="section-number">02</span>
              </div>

              <div className="review-grid">
                <div className="work-card ingredient-card">
                  <div className="mini-heading">
                    <span><Icon name="edit" size={19} /></span>
                    <div><h3>Your ingredients</h3><p>Edit anything that doesn’t look right.</p></div>
                  </div>
                  <IngredientEditor
                    ingredients={ingredients}
                    onChange={updateIngredient}
                    onRemove={(id) => { setIngredients((current) => current.filter((item) => item.id !== id)); setRecipes([]) }}
                    onAdd={addIngredient}
                  />
                </div>

                <div className="preference-stack">
                  <div className="work-card compact-card">
                    <div className="mini-heading">
                      <span className="warm"><Icon name="shield" size={19} /></span>
                      <div><h3>Allergies to avoid</h3><p>Select every allergy that applies.</p></div>
                    </div>
                    <AllergenPicker selected={allergens} onToggle={toggleAllergen} />
                    {allergens.length > 0 && <p className="allergy-warning">We’ll exclude these, but always check labels for severe allergies.</p>}
                  </div>

                  <div className="work-card compact-card preferences-card">
                    <div className="field-group">
                      <label htmlFor="diet">I usually eat</label>
                      <div className="select-wrap">
                        <select id="diet" value={dietaryPreference} onChange={(event) => { setDietaryPreference(event.target.value); setRecipes([]) }}>
                          {DIETARY_OPTIONS.map((option) => <option key={option}>{option}</option>)}
                        </select>
                        <Icon name="chevron" size={16} />
                      </div>
                    </div>
                    <div className="field-pair">
                      <div className="field-group">
                        <label htmlFor="time">Max time</label>
                        <div className="select-wrap">
                          <select id="time" value={maxCookingMinutes} onChange={(event) => { setMaxCookingMinutes(event.target.value); setRecipes([]) }}>
                            {[20, 30, 45, 60, 90].map((value) => <option value={value} key={value}>{value} min</option>)}
                          </select>
                          <Icon name="chevron" size={16} />
                        </div>
                      </div>
                      <div className="field-group">
                        <label htmlFor="servings">Serves</label>
                        <div className="select-wrap">
                          <select id="servings" value={servings} onChange={(event) => { setServings(event.target.value); setRecipes([]) }}>
                            {[1, 2, 3, 4, 6].map((value) => <option value={value} key={value}>{value}</option>)}
                          </select>
                          <Icon name="chevron" size={16} />
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <div className="generate-bar">
                <div><Icon name="sparkles" size={22} /><p><strong>Everything look right?</strong><span>We’ll dream up three delicious ideas.</span></p></div>
                <button className="primary-button large" type="button" disabled={!validIngredients.length || Boolean(busy)} onClick={handleGenerate}>
                  {busy === 'generating' ? <><span className="spinner" /> Creating recipes…</> : <>Create my recipes <Icon name="arrow" size={19} /></>}
                </button>
              </div>
            </section>
          )}

          {recipes.length > 0 && (
            <section className="results-section" ref={resultsRef}>
              <div className="results-heading">
                <div>
                  <p className="eyebrow">Made for your kitchen</p>
                  <h2>Three lovely possibilities</h2>
                  <p>Built around what you have, your preferences, and the time you want to spend.</p>
                </div>
                <span className="section-number">03</span>
              </div>
              <div className="recipe-grid">
                {recipes.map((recipe) => <RecipeCard recipe={recipe} onOpen={setSelectedRecipe} key={recipe.id} />)}
              </div>
              <div className="safety-note results-safety"><Icon name="shield" size={18} /><p>{safetyNote}</p></div>
            </section>
          )}
        </section>
      </main>

      <footer>
        <a className="brand muted" href="#top"><span className="brand-mark"><Icon name="leaf" size={19} /></span><span>mise</span></a>
        <p>Waste less. Cook more. Eat beautifully.</p>
        <span>Prototype · 2026</span>
      </footer>

      {selectedRecipe && <RecipeModal recipe={selectedRecipe} safetyNote={safetyNote} onClose={() => setSelectedRecipe(null)} />}
    </>
  )
}
