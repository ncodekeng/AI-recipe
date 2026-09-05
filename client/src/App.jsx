import { useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { analyzePhotos, createDeliverooBasket, findRecipePhotos, generateRecipes, getStatus, getUsage, resetUsage, submitFeedback } from './api.js'
import {
  clearLocalData,
  loadKitchenMemory,
  loadPreferences,
  mergeKitchenMemory,
  saveKitchenMemory,
  savePreferences,
} from './storage.js'
import RecipeHeroImage from './RecipeHeroImage.jsx'
import PromptAdminScreen from './PromptAdminScreen.jsx'
import { hasValidRecipePhoto } from './recipePhotos.js'
import {
  buildGroceryBasketPayload,
  canPrepareGroceryBasket,
  formatShoppingList,
  getMissingIngredients,
} from './groceryBasket.js'
import {
  addHistoryEntry,
  clearLibrary,
  getRecentlyShownRecipeIds,
  loadHistory,
  loadSavedRecipes,
  removeSavedRecipe,
  toggleSavedRecipe,
} from './library.js'
import {
  getRecipeSearchEmptyState,
  getRecipesForMode,
  INITIAL_RECIPE_SEARCH_STATE,
  RECIPE_MODES,
  recipeSearchReducer,
  usesOnlyAvailableIngredients,
} from './recipeSearchState.js'

const ALLERGENS = [
  'Peanuts',
  'Tree nuts',
  'Milk',
  'Eggs',
  'Gluten cereals',
  'Soy',
  'Fish',
  'Crustaceans',
  'Molluscs',
  'Sesame',
  'Celery',
  'Mustard',
  'Lupin',
  'Sulphites',
]

const DIETARY_OPTIONS = [
  'Anything',
  'Vegetarian',
  'Vegan',
  'Pescatarian',
  'Gluten-free',
  'Dairy-free',
  'Halal-style',
  'Kosher-style',
]

const SUPPORTED_IMAGE_TYPES = new Set(['image/jpeg', 'image/png', 'image/gif', 'image/webp'])
const MAX_PHOTO_COUNT = 50
const MAX_IMAGE_BYTES = 5 * 1024 * 1024
const COOKING_TIME_OPTIONS = [20, 30, 45, 60, 90, 120, 180, 240, 0]
const DEFAULT_PREFERENCES = {
  allergens: [],
  dietaryPreference: 'Anything',
  avoidText: '',
  maxCookingMinutes: 45,
  servings: 2,
  showRecipePhotos: true,
}
const INITIAL_PREFERENCES = { ...DEFAULT_PREFERENCES, ...loadPreferences() }
const INITIAL_KITCHEN_MEMORY = loadKitchenMemory()
const INITIAL_SAVED_RECIPES = loadSavedRecipes()
const INITIAL_HISTORY = loadHistory()
const DEFAULT_SAFETY_NOTE = 'No known conflicts were found from the listed ingredients. Always verify product labels, substitutions, and cross-contamination warnings.'

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
    external: <><path d="M11 3h6v6"/><path d="m9 11 8-8"/><path d="M16 12v4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h4"/></>,
    basket: <><path d="M3 8h14l-1 9H4L3 8Z"/><path d="m7 8 3-5 3 5"/><path d="M7 11v3M10 11v3M13 11v3"/></>,
    bookmark: <path d="M5 3h10v14l-5-3-5 3V3Z"/>,
    history: <><path d="M3 5v5h5"/><path d="M4 10a7 7 0 1 0 2-5"/><path d="M10 6v4l3 2"/></>,
    settings: <><circle cx="10" cy="10" r="3"/><path d="M10 2v2M10 16v2M2 10h2M16 10h2M4.3 4.3l1.4 1.4M14.3 14.3l1.4 1.4M15.7 4.3l-1.4 1.4M5.7 14.3l-1.4 1.4"/></>,
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

function EdamamAttribution() {
  useEffect(() => {
    if (document.querySelector('script[data-edamam-attribution]')) return

    const script = document.createElement('script')
    script.src = 'https://developer.edamam.com/attribution/badge.js'
    script.async = true
    script.dataset.edamamAttribution = 'true'
    document.head.appendChild(script)
  }, [])

  return <div className="edamam-attribution" id="edamam-badge" data-color="transparent" />
}

function PrivacyModal({ onClose, onClear }) {
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

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <article className="privacy-modal" role="dialog" aria-modal="true" aria-labelledby="privacy-title">
        <button className="modal-close" type="button" onClick={onClose} aria-label="Close privacy information">
          <Icon name="close" size={19} />
        </button>
        <p className="eyebrow"><Icon name="shield" size={17} /> Prototype data handling</p>
        <h2 id="privacy-title">Your kitchen stays yours.</h2>
        <div className="privacy-points">
          <p><strong>Photos are temporary.</strong> They are sent to the API for recognition, held in memory while the request runs, and not saved by this app.</p>
          <p><strong>Cloud processing can apply.</strong> In live mode, photos are processed by the configured Azure OpenAI resource. Recipe searches send ingredient names and selected restrictions to Azure web search (Grounding with Bing), or to Edamam when that optional provider is selected.</p>
          <p><strong>Your Kitchen Memory stays in this browser.</strong> The corrected ingredient list, preferences, anonymous usage ID, bookmarks, and recent searches are stored locally. Uploaded photo bytes are never stored.</p>
        </div>
        <button className="secondary-button danger" type="button" onClick={onClear}>Clear this browser's data</button>
      </article>
    </div>
  )
}

function LibraryModal({ savedRecipes, history, onClose, onOpenRecipe, onRemove, onRestore, onClear }) {
  const [tab, setTab] = useState(savedRecipes.length ? 'saved' : 'history')

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

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <article className="library-modal" role="dialog" aria-modal="true" aria-labelledby="library-title">
        <button className="modal-close" type="button" onClick={onClose} aria-label="Close saved recipes">
          <Icon name="close" size={19} />
        </button>
        <p className="eyebrow"><Icon name="bookmark" size={17} /> Your kitchen library</p>
        <h2 id="library-title">Saved & recent</h2>
        <div className="library-tabs" role="tablist" aria-label="Kitchen library">
          <button className={tab === 'saved' ? 'active' : ''} type="button" role="tab" aria-selected={tab === 'saved'} onClick={() => setTab('saved')}>
            Saved <span>{savedRecipes.length}</span>
          </button>
          <button className={tab === 'history' ? 'active' : ''} type="button" role="tab" aria-selected={tab === 'history'} onClick={() => setTab('history')}>
            History <span>{history.length}</span>
          </button>
        </div>

        {tab === 'saved' && (
          <div className="library-list">
            {savedRecipes.length === 0 && <div className="library-empty"><Icon name="bookmark" size={24} /><p>Recipes you save will appear here.</p></div>}
            {savedRecipes.map((recipe) => (
              <article className="library-row" key={recipe.id}>
                <div>
                  <strong>{recipe.title}</strong>
                  <span>{recipe.bookmarkOnly ? `From ${recipe.sourceName || 'original publisher'}` : `${recipe.cookingMinutes} min · ${recipe.cuisine}`}</span>
                </div>
                {recipe.bookmarkOnly
                  ? <a href={recipe.sourceUrl} target="_blank" rel="noreferrer">Open source <Icon name="external" size={14} /></a>
                  : <button type="button" onClick={() => onOpenRecipe(recipe)}>Open</button>}
                <button className="library-remove" type="button" aria-label={`Remove ${recipe.title}`} onClick={() => onRemove(recipe.id)}>
                  <Icon name="trash" size={16} />
                </button>
              </article>
            ))}
          </div>
        )}

        {tab === 'history' && (
          <div className="library-list">
            {history.length === 0 && <div className="library-empty"><Icon name="history" size={25} /><p>Your recent ingredient searches will appear here.</p></div>}
            {history.map((entry) => (
              <article className="library-row history-row" key={entry.id}>
                <div>
                  <strong>{entry.ingredients.slice(0, 3).map((item) => item.name).join(', ')}{entry.ingredients.length > 3 ? ` +${entry.ingredients.length - 3}` : ''}</strong>
                  <span>{new Date(entry.createdAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} · {entry.resultCount} result{entry.resultCount === 1 ? '' : 's'} · {entry.provider}</span>
                </div>
                <button type="button" onClick={() => onRestore(entry)}>Use again</button>
              </article>
            ))}
          </div>
        )}

        {(savedRecipes.length > 0 || history.length > 0) && (
          <button className="library-clear" type="button" onClick={onClear}>Clear saved and recent</button>
        )}
      </article>
    </div>
  )
}

function FeedbackModal({ onClose }) {
  const [rating, setRating] = useState(5)
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [sent, setSent] = useState(false)

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

  async function handleSubmit(event) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      await submitFeedback({ rating, message: message.trim() })
      setSent(true)
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <article className="feedback-modal" role="dialog" aria-modal="true" aria-labelledby="feedback-title">
        <button className="modal-close" type="button" onClick={onClose} aria-label="Close feedback">
          <Icon name="close" size={19} />
        </button>
        {sent ? (
          <div className="feedback-thanks">
            <span><Icon name="check" size={27} strokeWidth={2.3} /></span>
            <h2 id="feedback-title">Thank you.</h2>
            <p>Your feedback has been received and will help shape the next PLATE review.</p>
            <button className="primary-button" type="button" onClick={onClose}>Done</button>
          </div>
        ) : (
          <form onSubmit={handleSubmit}>
            <p className="eyebrow">Help us improve</p>
            <h2 id="feedback-title">How was your kitchen flow?</h2>
            <fieldset className="rating-picker">
              <legend>Rating</legend>
              <div>
                {[1, 2, 3, 4, 5].map((value) => (
                  <button className={rating === value ? 'selected' : ''} type="button" aria-pressed={rating === value} onClick={() => setRating(value)} key={value}>
                    {value}
                  </button>
                ))}
              </div>
            </fieldset>
            <label className="feedback-message" htmlFor="feedback-message">
              <span>What should we improve? <small>Optional</small></span>
              <textarea id="feedback-message" value={message} maxLength={800} rows={5} placeholder="Tell us what worked or where you got stuck…" onChange={(event) => setMessage(event.target.value)} />
              <small>{message.length}/800 · Please do not include personal or medical information.</small>
            </label>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button className="primary-button" type="submit" disabled={busy}>
              {busy ? <><span className="spinner" /> Sending…</> : 'Send feedback'}
            </button>
          </form>
        )}
      </article>
    </div>
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

const LOADING_COPY = {
  analyzing: {
    eyebrow: 'Scanning your kitchen',
    title: 'Looking closely at your photos',
    steps: ['Uploading photos securely', 'Identifying food and ingredients', 'Estimating quantities', 'Preparing your editable list'],
  },
  generating: {
    eyebrow: 'Searching live recipe sources',
    title: 'Finding recipes you can almost make',
    steps: ['Searching online publishers', 'Checking your dietary settings', 'Comparing every ingredient', 'Ranking your strongest matches'],
  },
}

function LoadingExperience({ mode }) {
  const [activeStep, setActiveStep] = useState(0)
  const content = LOADING_COPY[mode]

  useEffect(() => {
    setActiveStep(0)
    const timer = window.setInterval(() => {
      setActiveStep((current) => (current + 1) % content.steps.length)
    }, 1800)
    return () => window.clearInterval(timer)
  }, [content])

  return (
    <section
      className={`loading-experience ${mode}`}
      id={mode === 'generating' ? 'recipe-loading' : undefined}
      role="status"
      aria-live="polite"
    >
      <div className="loading-message">
        <div className="loading-orbit" aria-hidden="true">
          <span className="loading-orbit-ring" />
          <span className="loading-orbit-core"><Icon name={mode === 'analyzing' ? 'camera' : 'sparkles'} size={23} /></span>
        </div>
        <div>
          <p className="eyebrow">{content.eyebrow}</p>
          <h3>{content.title}</h3>
          <p className="loading-step">{content.steps[activeStep]}<span aria-hidden="true">…</span></p>
        </div>
      </div>
      <div className="loading-progress" aria-hidden="true">
        {content.steps.map((step, index) => <span className={index === activeStep ? 'active' : index < activeStep ? 'complete' : ''} key={step} />)}
      </div>
      {mode === 'generating' && (
        <div className="loading-skeleton-grid" aria-hidden="true">
          {[0, 1, 2].map((item) => (
            <div className="loading-skeleton-card" key={item}>
              <span className="skeleton-photo" />
              <div><span /><span /><span /></div>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

function PhotoUploader({ photos, onFiles, onRemove, busy }) {
  const [isDragging, setIsDragging] = useState(false)
  const cameraInputRef = useRef(null)
  const libraryInputRef = useRef(null)

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
          ref={cameraInputRef}
          className="sr-only"
          type="file"
          accept="image/*"
          capture="environment"
          disabled={busy || photos.length >= MAX_PHOTO_COUNT}
          onChange={(event) => {
            selectFiles(event.target.files)
            event.target.value = ''
          }}
        />
        <input
          ref={libraryInputRef}
          className="sr-only"
          type="file"
          accept="image/*"
          multiple
          disabled={busy || photos.length >= MAX_PHOTO_COUNT}
          onChange={(event) => {
            selectFiles(event.target.files)
            event.target.value = ''
          }}
        />
        <div className="drop-zone-content">
          <span className="upload-icon"><Icon name="camera" size={28} /></span>
          <strong>Add kitchen photos</strong>
          <span>Fridge, cupboard or countertop</span>
          <div className="photo-source-actions">
            <button type="button" onClick={() => cameraInputRef.current?.click()} disabled={busy || photos.length >= MAX_PHOTO_COUNT}>
              <Icon name="camera" size={15} /> Take photo
            </button>
            <button type="button" onClick={() => libraryInputRef.current?.click()} disabled={busy || photos.length >= MAX_PHOTO_COUNT}>
              <Icon name="upload" size={15} /> Choose photos
            </button>
          </div>
        </div>
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
          {photos.length < MAX_PHOTO_COUNT && (
            <button className="add-photo" type="button" onClick={() => libraryInputRef.current?.click()} disabled={busy}>
              <Icon name="plus" size={22} />
              <span>Choose more</span>
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
              {ingredient.kind === 'Frozen meal' && <span className="ingredient-kind">Frozen meal</span>}
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

function IngredientPreview({ label, ingredients, prefix, emptyText }) {
  return (
    <div className="match-list">
      <strong>{label}</strong>
      {ingredients.length > 0 ? (
        <ul>
          {ingredients.slice(0, 4).map((item, index) => (
            <li key={`${item.name}-${index}`}><span>{prefix}</span>{item.name}</li>
          ))}
          {ingredients.length > 4 && <li className="more-items">+ {ingredients.length - 4} more</li>}
        </ul>
      ) : <p>{emptyText}</p>}
    </div>
  )
}

function GroceryAction({ recipe, compact = false }) {
  const missingIngredients = getMissingIngredients(recipe)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState(null)
  const [error, setError] = useState('')

  if (!canPrepareGroceryBasket(recipe)) {
    return <p className="everything-ready"><Icon name="check" size={15} /> You have everything you need.</p>
  }

  async function prepareBasket() {
    setBusy(true)
    setError('')
    setResult(null)
    try {
      const response = await createDeliverooBasket(buildGroceryBasketPayload(recipe))
      if (response.basketCreated && response.checkoutUrl) {
        window.location.assign(response.checkoutUrl)
        return
      }

      let copied = false
      try {
        await navigator.clipboard.writeText(formatShoppingList(response.ingredients || missingIngredients))
        copied = true
      } catch {
        // Clipboard access is optional; the user can still open Deliveroo and view the list here.
      }
      setResult({ ...response, copied })
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={`grocery-action ${compact ? 'compact' : ''}`}>
      <button type="button" disabled={busy} onClick={prepareBasket}>
        <Icon name="basket" size={16} />
        {busy
          ? 'Preparing list…'
          : `Get ${missingIngredients.length} missing ingredient${missingIngredients.length === 1 ? '' : 's'}`}
      </button>
      {result && (
        <div className="grocery-handoff" role="status">
          <p>{result.copied ? 'Shopping list copied. ' : ''}{result.message}</p>
          <ul>{missingIngredients.map((item, index) => <li key={`${item.name}-${index}`}>{item.amount} {item.name}</li>)}</ul>
          {result.handoffUrl && <a href={result.handoffUrl} target="_blank" rel="noreferrer">Open Deliveroo <Icon name="external" size={13} /></a>}
        </div>
      )}
      {error && <p className="grocery-error" role="alert">{error}</p>}
    </div>
  )
}

function RecipeCard({ recipe, onOpen, onSave, saved, showRecipePhotos, isTopPick }) {
  const availableIngredients = Array.isArray(recipe.availableIngredients) ? recipe.availableIngredients : []
  const missingIngredients = getMissingIngredients(recipe)
  return (
    <article className={`recipe-card ${isTopPick ? 'top-pick-card' : ''}`}>
      <RecipeHeroImage recipe={recipe} showRecipePhotos={showRecipePhotos}>
        <button className={`save-button ${saved ? 'saved' : ''}`} type="button" aria-label={`${saved ? 'Remove' : 'Save'} ${recipe.title}`} onClick={() => onSave(recipe)}>
          <Icon name="bookmark" size={17} />
        </button>
        <div className="match-badge">{recipe.ingredientMatch}% match</div>
      </RecipeHeroImage>
      <PhotoAttribution recipe={recipe} showRecipePhotos={showRecipePhotos} compact />
      <div className="recipe-card-body">
        <div className="top-pick-slot">
          {isTopPick && <p className="top-pick-label"><Icon name="sparkles" size={14} /> Top pick</p>}
        </div>
        <div className="recipe-tags">
          {recipe.tags.slice(0, 3).map((tag) => <span key={tag}>{tag}</span>)}
        </div>
        <h3>{recipe.title}</h3>
        <div className="recipe-meta">
          <span><Icon name="clock" size={16} /> {recipe.cookingMinutes > 0 ? `${recipe.cookingMinutes} min` : 'See source'}</span>
          <span><Icon name="users" size={17} /> {recipe.servings} servings</span>
          <span>{recipe.difficulty}</span>
        </div>
        <div className="wine-pairing-slot">
          {recipe.winePairing && (
            <p className="wine-pairing"><strong>Rough wine pairing</strong><span>{recipe.winePairing}</span></p>
          )}
        </div>
        <div className="match-breakdown">
          <IngredientPreview label="You already have" ingredients={availableIngredients} prefix="✓" emptyText="No confirmed matches yet." />
          <IngredientPreview label="You still need" ingredients={missingIngredients} prefix="+" emptyText="Nothing else — you’re ready." />
        </div>
        <div className="recipe-card-action-slot">
          <GroceryAction recipe={recipe} compact />
        </div>
        <button type="button" className="recipe-open" onClick={() => onOpen(recipe)}>
          View recipe <Icon name="arrow" size={17} />
        </button>
        {recipe.sourceUrl && (
          <a className="recipe-source" href={recipe.sourceUrl} target="_blank" rel="noreferrer">
            <span><small>Original publisher</small><strong>View detailed recipe</strong></span>
            <Icon name="external" size={16} />
          </a>
        )}
      </div>
    </article>
  )
}

function RecipeModal({ recipe, onClose, safetyNote, onSave, saved, showRecipePhotos }) {
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

  const missingIngredients = getMissingIngredients(recipe)
  const availableIngredients = Array.isArray(recipe.availableIngredients) ? recipe.availableIngredients : []
  const directions = Array.isArray(recipe.steps) ? recipe.steps.filter((step) => typeof step === 'string' && step.trim()) : []
  const directionsKind = recipe.directionsKind || 'Unavailable'
  const hasAiGuide = directionsKind === 'AiGenerated' && directions.length > 0
  const hasProviderDirections = directionsKind === 'Provider' && directions.length > 0
  const missing = new Set(missingIngredients.map((item) => item.name.toLowerCase()))
  const isSourced = Boolean(recipe.sourceUrl)
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <article className="recipe-modal" role="dialog" aria-modal="true" aria-labelledby="recipe-title">
        <button className="modal-close" type="button" onClick={onClose} aria-label="Close recipe">
          <Icon name="close" size={19} />
        </button>
        <button className={`modal-save ${saved ? 'saved' : ''}`} type="button" onClick={() => onSave(recipe)}>
          <Icon name="bookmark" size={16} /> {saved ? 'Saved' : 'Save'}
        </button>
        <RecipeHeroImage recipe={recipe} showRecipePhotos={showRecipePhotos} className="modal-hero">
          <div className="modal-hero-content">
            <span>{recipe.ingredientMatch}% match · {recipe.cuisine}</span>
            <h2 id="recipe-title">{recipe.title}</h2>
            <div className="modal-meta">
              <span><Icon name="clock" size={17} /> {recipe.cookingMinutes > 0 ? `${recipe.cookingMinutes} min` : 'Time on source'}</span>
              <span><Icon name="users" size={18} /> {recipe.servings} servings</span>
              <span>{recipe.difficulty}</span>
            </div>
          </div>
        </RecipeHeroImage>
        <PhotoAttribution recipe={recipe} showRecipePhotos={showRecipePhotos} />
        <div className="modal-content">
          {recipe.winePairing && (
            <p className="wine-pairing modal-wine-pairing"><strong>Rough wine pairing</strong><span>{recipe.winePairing}</span></p>
          )}
          <div className="modal-match-panel">
            <IngredientPreview label="You have" ingredients={availableIngredients} prefix="✓" emptyText="No confirmed matches yet." />
            <IngredientPreview label="You need" ingredients={missingIngredients} prefix="+" emptyText="You have everything you need." />
          </div>
          <GroceryAction recipe={recipe} />
          <div className="recipe-columns">
            <section>
              <p className="recipe-section-title">Ingredients</p>
              <ul className="modal-ingredients">
                {recipe.ingredients.map((item, index) => (
                  <li className={missing.has(item.name.toLowerCase()) ? 'missing' : ''} key={`${item.name}-${index}`}>
                    <span>{item.name}{missing.has(item.name.toLowerCase()) && <small>Missing</small>}</span>
                    <strong>{item.amount}</strong>
                  </li>
                ))}
              </ul>
            </section>
            <section>
              <p className="recipe-section-title">{hasAiGuide ? 'AI cooking guide' : 'Directions'}</p>
              {directions.length > 0 ? (
                <>
                  {hasAiGuide && (
                    <div className="directions-provenance ai-guide-note">
                      <Icon name="sparkles" size={17} />
                      <p><strong>AI-generated cooking guide</strong><span>This is practical guidance, not the publisher's original method. Confirm it with the live recipe below.</span></p>
                    </div>
                  )}
                  {hasProviderDirections && (
                    <div className="directions-provenance provider-directions-note">
                      <Icon name="check" size={17} />
                      <p><strong>Provider directions</strong><span>Supplied by {recipe.sourceName || 'the recipe provider'}.</span></p>
                    </div>
                  )}
                  <ol className="method-list">
                    {directions.map((step, index) => (
                      <li key={index}><span>{index + 1}</span><p>{step}</p></li>
                    ))}
                  </ol>
                </>
              ) : isSourced ? (
                <div className="source-method">
                  <strong>Directions remain with the publisher</strong>
                  <p>This recipe provider did not license cooking directions for display inside PLATE. Use the live source link at the end for the complete method and timings.</p>
                </div>
              ) : (
                <div className="source-method"><p>Directions are unavailable for this recipe.</p></div>
              )}
            </section>
          </div>
          <div className="safety-note"><Icon name="shield" size={18} /><p>{safetyNote}</p></div>
          {isSourced && (
            <a className="modal-detail-link" href={recipe.sourceUrl} target="_blank" rel="noreferrer">
              <span><small>Original recipe · {recipe.sourceName || 'publisher'}</small><strong>View full live recipe</strong></span>
              <Icon name="external" size={19} />
            </a>
          )}
        </div>
      </article>
    </div>
  )
}

function PhotoAttribution({ recipe, showRecipePhotos, compact = false }) {
  if (!showRecipePhotos || !hasValidRecipePhoto(recipe)) return null
  const isTestOnly = recipe.imageRightsStatus === 'UnverifiedTestOnly'

  return (
    <p className={`photo-attribution ${compact ? 'compact' : ''} ${isTestOnly ? 'test-only' : ''}`}>
      <span>{recipe.imageAttributionRequirements}</span>
      <a href={recipe.imageSourceUrl} target="_blank" rel="noreferrer">Image source</a>
      {recipe.imageLicenseUrl ? (
        <a href={recipe.imageLicenseUrl} target="_blank" rel="noreferrer">{recipe.imageLicenseType}</a>
      ) : (
        <strong>{isTestOnly ? 'Unverified · testing only' : recipe.imageLicenseType}</strong>
      )}
    </p>
  )
}

export default function App() {
  const [photos, setPhotos] = useState([])
  const [ingredients, setIngredients] = useState(INITIAL_KITCHEN_MEMORY)
  const [allergens, setAllergens] = useState(() => Array.isArray(INITIAL_PREFERENCES.allergens)
    ? INITIAL_PREFERENCES.allergens.filter((item) => ALLERGENS.includes(item))
    : [])
  const [dietaryPreference, setDietaryPreference] = useState(() => DIETARY_OPTIONS.includes(INITIAL_PREFERENCES.dietaryPreference)
    ? INITIAL_PREFERENCES.dietaryPreference
    : DEFAULT_PREFERENCES.dietaryPreference)
  const [avoidText, setAvoidText] = useState(() => typeof INITIAL_PREFERENCES.avoidText === 'string'
    ? INITIAL_PREFERENCES.avoidText.slice(0, 220)
    : '')
  const [maxCookingMinutes, setMaxCookingMinutes] = useState(() => COOKING_TIME_OPTIONS.includes(Number(INITIAL_PREFERENCES.maxCookingMinutes))
    ? Number(INITIAL_PREFERENCES.maxCookingMinutes)
    : DEFAULT_PREFERENCES.maxCookingMinutes)
  const [servings, setServings] = useState(() => [1, 2, 3, 4, 6].includes(Number(INITIAL_PREFERENCES.servings))
    ? Number(INITIAL_PREFERENCES.servings)
    : DEFAULT_PREFERENCES.servings)
  const [showRecipePhotos, setShowRecipePhotos] = useState(() => typeof INITIAL_PREFERENCES.showRecipePhotos === 'boolean'
    ? INITIAL_PREFERENCES.showRecipePhotos
    : DEFAULT_PREFERENCES.showRecipePhotos)
  const [recipeSearch, dispatchRecipeSearch] = useReducer(
    recipeSearchReducer,
    INITIAL_RECIPE_SEARCH_STATE,
  )
  const { mode: recipeMode, recipes, availableOnlyRecipes, hasCompletedSearch } = recipeSearch
  const [selectedRecipe, setSelectedRecipe] = useState(null)
  const [safetyNote, setSafetyNote] = useState('')
  const [provider, setProvider] = useState('Checking…')
  const [usage, setUsage] = useState(null)
  const [notice, setNotice] = useState(() => INITIAL_KITCHEN_MEMORY.length
    ? `Loaded ${INITIAL_KITCHEN_MEMORY.length} ingredient${INITIAL_KITCHEN_MEMORY.length === 1 ? '' : 's'} from your Kitchen Memory.`
    : '')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')
  const [showPrivacy, setShowPrivacy] = useState(false)
  const [showLibrary, setShowLibrary] = useState(false)
  const [showFeedback, setShowFeedback] = useState(false)
  const [showAdmin, setShowAdmin] = useState(() => window.location.hash === '#admin')
  const [savedRecipes, setSavedRecipes] = useState(INITIAL_SAVED_RECIPES)
  const [history, setHistory] = useState(INITIAL_HISTORY)
  const [reviewStarted, setReviewStarted] = useState(false)

  const reviewRef = useRef(null)
  const resultsRef = useRef(null)
  const photoUrlsRef = useRef(new Set())

  const currentStep = hasCompletedSearch ? 3 : reviewStarted ? 2 : 1
  const validIngredients = useMemo(
    () => ingredients.filter((item) => item.name.trim()),
    [ingredients],
  )
  const visibleRecipes = useMemo(
    () => getRecipesForMode(recipes, recipeMode, availableOnlyRecipes),
    [recipes, availableOnlyRecipes, recipeMode],
  )
  const recipeSearchEmptyState = getRecipeSearchEmptyState(
    { ...recipeSearch, recipes: visibleRecipes },
    busy,
    error,
  )

  useEffect(() => {
    const controller = new AbortController()
    getStatus(controller.signal)
      .then((status) => setProvider(status.recipeProviderConfigured
        ? status.recipeProvider
        : `${status.recipeProvider} setup needed`))
      .catch(() => setProvider('API offline'))
    getUsage(controller.signal)
      .then(setUsage)
      .catch(() => {})
    return () => controller.abort()
  }, [])

  useEffect(() => () => {
    photoUrlsRef.current.forEach((url) => URL.revokeObjectURL(url))
  }, [])

  useEffect(() => {
    const syncAdminRoute = () => setShowAdmin(window.location.hash === '#admin')
    window.addEventListener('hashchange', syncAdminRoute)
    return () => window.removeEventListener('hashchange', syncAdminRoute)
  }, [])

  useEffect(() => {
    savePreferences({ allergens, dietaryPreference, avoidText, maxCookingMinutes, servings, showRecipePhotos })
  }, [allergens, dietaryPreference, avoidText, maxCookingMinutes, servings, showRecipePhotos])

  useEffect(() => {
    if (busy !== 'generating') return
    const frame = window.requestAnimationFrame(() => {
      document.getElementById('recipe-loading')?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [busy])

  function invalidateRecipeResults() {
    dispatchRecipeSearch({ type: 'invalidate' })
    setSelectedRecipe(null)
    setSafetyNote('')
    setNotice('')
  }

  function addPhotos(files) {
    setError('')
    setReviewStarted(false)
    const supported = files.filter((file) => SUPPORTED_IMAGE_TYPES.has(file.type) && file.size <= MAX_IMAGE_BYTES)
    if (supported.length !== files.length) {
      setError('Use JPEG, PNG, GIF, or WebP photos no larger than 5 MB each.')
    }
    const remaining = MAX_PHOTO_COUNT - photos.length
    const accepted = supported.slice(0, remaining)
    if (supported.length > remaining) setError(`You can add up to ${MAX_PHOTO_COUNT} photos at a time.`)
    const additions = accepted.map((file) => {
      const url = URL.createObjectURL(file)
      photoUrlsRef.current.add(url)
      return { id: crypto.randomUUID(), file, url }
    })
    setPhotos((current) => [...current, ...additions])
    invalidateRecipeResults()
  }

  function removePhoto(id) {
    setReviewStarted(false)
    setPhotos((current) => {
      const removed = current.find((photo) => photo.id === id)
      if (removed) {
        URL.revokeObjectURL(removed.url)
        photoUrlsRef.current.delete(removed.url)
      }
      return current.filter((photo) => photo.id !== id)
    })
    invalidateRecipeResults()
  }

  function updateKitchenMemory(updater) {
    setReviewStarted(true)
    invalidateRecipeResults()
    setIngredients((current) => {
      const next = typeof updater === 'function' ? updater(current) : updater
      saveKitchenMemory(next)
      return next
    })
  }

  async function handleAnalyze() {
    if (!photos.length) return
    setBusy('analyzing')
    setError('')
    setNotice('')
    try {
      const result = await analyzePhotos(photos.map((photo) => photo.file))
      if (!result.ingredients.length) {
        setIngredients([])
        setNotice(result.notice || '')
        setError(result.failedPhotos?.length
          ? 'No ingredients could be confirmed from the photos Azure completed. Retry the failed photos or scan again with brighter, closer images.'
          : 'No clear food items were found. Try a brighter, closer photo of the shelves or worktop.')
        return
      }
      const rememberedIngredients = mergeKitchenMemory(ingredients, result.ingredients)
      updateKitchenMemory(rememberedIngredients)
      setProvider(result.provider)
      const ignoredNotice = result.ignoredPhotos?.length
        ? `${result.ignoredPhotos.length} photo${result.ignoredPhotos.length === 1 ? ' was' : 's were'} ignored because no clear food was found.`
        : ''
      const failedNotice = result.failedPhotos?.length && !result.notice
        ? `${result.failedPhotos.length} photo${result.failedPhotos.length === 1 ? ' could' : 's could'} not be analysed. Retry ${result.failedPhotos.length === 1 ? 'it' : 'them'} for a more complete Kitchen Memory.`
        : ''
      const memoryNotice = `${rememberedIngredients.length} ingredient${rememberedIngredients.length === 1 ? '' : 's'} saved in Kitchen Memory.`
      setNotice([result.notice, ignoredNotice, failedNotice, memoryNotice].filter(Boolean).join(' '))
      requestAnimationFrame(() => reviewRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy('')
      getUsage().then(setUsage).catch(() => {})
    }
  }

  function updateIngredient(id, field, value) {
    updateKitchenMemory((current) => {
      const updated = current.map((item) => item.id === id ? { ...item, [field]: value } : item)
      return field === 'name' && value.trim()
        ? mergeKitchenMemory([], updated)
        : updated
    })
  }

  function addIngredient() {
    updateKitchenMemory((current) => [
      ...current,
      { id: crypto.randomUUID(), name: '', quantity: 'as needed', confidence: 0, sourceImage: 'Added manually' },
    ])
  }

  function toggleAllergen(allergen) {
    setReviewStarted(true)
    invalidateRecipeResults()
    setAllergens((current) => current.includes(allergen)
      ? current.filter((item) => item !== allergen)
      : [...current, allergen])
  }

  async function runRecipeSearch(onlyUseAvailableIngredients, preserveExistingResults) {
    if (!validIngredients.length) return
    setReviewStarted(true)
    setBusy('generating')
    if (!preserveExistingResults) {
      dispatchRecipeSearch({ type: 'searchStarted' })
      setSafetyNote('')
    }
    setSelectedRecipe(null)
    setError('')
    setNotice('')
    try {
      const request = {
        ingredients: validIngredients.map(({ name, quantity }) => ({ name, quantity })),
        allergens,
        avoidIngredients: avoidText.split(',').map((item) => item.trim()).filter(Boolean),
        dietaryPreference,
        maxCookingMinutes: Number(maxCookingMinutes),
        servings: Number(servings),
        showPhotos: showRecipePhotos,
        onlyUseAvailableIngredients,
        recentlyShownRecipeIds: getRecentlyShownRecipeIds(history),
      }
      const result = await generateRecipes(request)
      dispatchRecipeSearch({
        type: preserveExistingResults ? 'availableOnlySearchSucceeded' : 'searchSucceeded',
        recipes: result.recipes,
      })
      setSafetyNote(result.safetyNote)
      setProvider(result.provider)
      setNotice(result.notice || '')
      setHistory((current) => addHistoryEntry(current, request, result))
      requestAnimationFrame(() => resultsRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
      return true
    } catch (requestError) {
      if (!preserveExistingResults) {
        dispatchRecipeSearch({ type: 'searchFailed' })
      }
      setError(requestError.message)
      return false
    } finally {
      setBusy('')
      getUsage().then(setUsage).catch(() => {})
    }
  }

  function handleGenerate() {
    return runRecipeSearch(false, false)
  }

  function handleFindCompleteMatches() {
    return runRecipeSearch(true, true)
  }

  function handleRecipeScopeChange(nextMode) {
    if (nextMode === recipeMode || busy) return

    setSelectedRecipe(null)
    setError('')
    dispatchRecipeSearch({ type: 'modeRequested', mode: nextMode })
  }

  async function handlePhotoPreferenceChange(enabled) {
    setReviewStarted(true)
    setShowRecipePhotos(enabled)
    if (!enabled || visibleRecipes.length === 0) return

    setBusy('photos')
    setError('')
    setNotice('Finding verified recipe photos…')
    try {
      const photoResults = await findRecipePhotos(
        visibleRecipes.map(({ id, title }) => ({ id, title })),
      )
      const photosById = new Map(photoResults.map((photo) => [photo.id, photo]))
      const applyPhotos = (currentRecipes) => currentRecipes.map((recipe) => ({
        ...recipe,
        ...(photosById.get(recipe.id) || {}),
      }))
      dispatchRecipeSearch({ type: 'replaceRecipes', recipes: applyPhotos(recipes) })
      dispatchRecipeSearch({
        type: 'replaceAvailableOnlyRecipes',
        recipes: applyPhotos(availableOnlyRecipes),
      })
      setSelectedRecipe((current) => current ? applyPhotos([current])[0] : current)
      const foundCount = photoResults.filter((photo) => photo.imageUrl).length
      setNotice(foundCount > 0
        ? `Found ${foundCount} verified recipe photo${foundCount === 1 ? '' : 's'}.`
        : 'No matching commercially reusable photos were verified, so the safe artwork remains visible.')
    } catch (requestError) {
      setError(requestError.message)
      setNotice('')
    } finally {
      setBusy('')
    }
  }

  function toggleSaved(recipe) {
    setSavedRecipes((current) => toggleSavedRecipe(current, recipe))
  }

  async function handleResetUsage() {
    try {
      const nextUsage = await resetUsage()
      setUsage(nextUsage)
      setError('')
      setNotice('Your local test allowance has been reset.')
    } catch (requestError) {
      setError(requestError.message)
    }
  }

  function restoreHistory(entry) {
    const restoredIngredients = Array.isArray(entry.ingredients)
      ? entry.ingredients
          .filter((item) => item && typeof item.name === 'string' && item.name.trim())
          .map((item) => ({
            id: crypto.randomUUID(),
            name: item.name.slice(0, 100),
            quantity: typeof item.quantity === 'string' ? item.quantity.slice(0, 80) : 'as needed',
            confidence: 0,
            sourceImage: 'Restored from history',
            kind: 'Ingredient',
          }))
      : []
    if (!restoredIngredients.length) return

    photoUrlsRef.current.forEach((url) => URL.revokeObjectURL(url))
    photoUrlsRef.current.clear()
    setPhotos([])
    updateKitchenMemory(restoredIngredients)
    setAllergens(Array.isArray(entry.allergens) ? entry.allergens.filter((item) => ALLERGENS.includes(item)) : [])
    setAvoidText(Array.isArray(entry.avoidIngredients) ? entry.avoidIngredients.join(', ').slice(0, 220) : '')
    setDietaryPreference(DIETARY_OPTIONS.includes(entry.dietaryPreference) ? entry.dietaryPreference : 'Anything')
    setMaxCookingMinutes(COOKING_TIME_OPTIONS.includes(Number(entry.maxCookingMinutes)) ? Number(entry.maxCookingMinutes) : 45)
    setServings([1, 2, 3, 4, 6].includes(Number(entry.servings)) ? Number(entry.servings) : 2)
    invalidateRecipeResults()
    setShowLibrary(false)
    setError('')
    setNotice('Your previous ingredients and settings are ready to review.')
    requestAnimationFrame(() => reviewRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
  }

  function openAdmin() {
    window.location.hash = 'admin'
    setShowAdmin(true)
  }

  function closeAdmin() {
    setShowAdmin(false)
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#top`)
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
          <button className="library-button" type="button" onClick={() => setShowLibrary(true)}>
            <Icon name="bookmark" size={15} /> Saved {savedRecipes.length > 0 && <span>{savedRecipes.length}</span>}
          </button>
          <button className={`library-button ${showAdmin ? 'active' : ''}`} type="button" onClick={openAdmin}>
            <Icon name="settings" size={15} /> Admin
          </button>
          <span className={`provider-badge ${provider === 'Azure OpenAI' || provider === 'Azure Web Search' || provider === 'Edamam' ? 'live' : ''}`}>
            <span /> {provider}
          </span>
        </nav>
      </header>

      <main id="top" aria-busy={Boolean(busy)}>
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
            {busy === 'analyzing' && <LoadingExperience mode="analyzing" />}
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
                    <div><h3>Your Kitchen Memory</h3><p>Edit anything that doesn’t look right. Changes are saved on this browser.</p></div>
                  </div>
                  <IngredientEditor
                    ingredients={ingredients}
                    onChange={updateIngredient}
                    onRemove={(id) => updateKitchenMemory((current) => current.filter((item) => item.id !== id))}
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
                        <select id="diet" value={dietaryPreference} onChange={(event) => { setReviewStarted(true); setDietaryPreference(event.target.value); invalidateRecipeResults() }}>
                          {DIETARY_OPTIONS.map((option) => <option key={option}>{option}</option>)}
                        </select>
                        <Icon name="chevron" size={16} />
                      </div>
                    </div>
                    <div className="field-pair">
                      <div className="field-group">
                        <label htmlFor="time">Max time</label>
                        <div className="select-wrap">
                          <select id="time" value={maxCookingMinutes} onChange={(event) => { setReviewStarted(true); setMaxCookingMinutes(event.target.value); invalidateRecipeResults() }}>
                            {COOKING_TIME_OPTIONS.map((value) => (
                              <option value={value} key={value}>
                                {value === 0 ? 'Unlimited' : value >= 120 ? `${value / 60}h` : `${value} min`}
                              </option>
                            ))}
                          </select>
                          <Icon name="chevron" size={16} />
                        </div>
                      </div>
                      <div className="field-group">
                        <label htmlFor="servings">Serves</label>
                        <div className="select-wrap">
                          <select id="servings" value={servings} onChange={(event) => { setReviewStarted(true); setServings(event.target.value); invalidateRecipeResults() }}>
                            {[1, 2, 3, 4, 6].map((value) => <option value={value} key={value}>{value}</option>)}
                          </select>
                          <Icon name="chevron" size={16} />
                        </div>
                      </div>
                    </div>
                    <div className="field-group">
                      <label htmlFor="avoid">Other ingredients to avoid</label>
                      <input
                        id="avoid"
                        className="text-input"
                        value={avoidText}
                        placeholder="e.g. coriander, mushrooms"
                        maxLength={220}
                        onChange={(event) => { setReviewStarted(true); setAvoidText(event.target.value); invalidateRecipeResults() }}
                      />
                      <p className="field-help">Separate multiple ingredients with commas.</p>
                    </div>
                    <label className="photo-preference">
                      <span><strong>Show recipe photos</strong><small>{busy === 'photos' ? 'Finding verified photos…' : 'Search for photos with verified commercial-use license metadata.'}</small></span>
                      <input type="checkbox" checked={showRecipePhotos} disabled={Boolean(busy)} onChange={(event) => handlePhotoPreferenceChange(event.target.checked)} />
                      <i aria-hidden="true"><span /></i>
                    </label>
                  </div>
                </div>
              </div>

              <div className="generate-bar">
                <div className="generate-prompt"><Icon name="sparkles" size={22} /><p><strong>Everything look right?</strong><span>Only sourced online recipes. We never invent them.</span></p></div>
                <div className="generate-actions">
                  <button className="primary-button large" type="button" disabled={!validIngredients.length || Boolean(busy)} onClick={() => handleGenerate()}>
                    {busy === 'generating' ? <><span className="spinner" /> Finding recipes you can almost make…</> : <>Find real recipes <Icon name="arrow" size={19} /></>}
                  </button>
                  {usage && (
                  <span className="usage-note">
                      {usage.isUnlimited
                        ? 'Unlimited admin test recipe searches'
                        : `${usage.recipesRemaining} of ${usage.recipeLimit} free recipe searches left today`}
                      {usage.canReset && <button type="button" disabled={Boolean(busy)} onClick={handleResetUsage}>Reset test uses</button>}
                    </span>
                  )}
                  {error && <p className="inline-action-error">{error}</p>}
                </div>
              </div>
            </section>
          )}

          {busy === 'generating' && <LoadingExperience mode="generating" />}

          {hasCompletedSearch && (
            <section className="results-section" ref={resultsRef}>
              <div className="results-heading">
                <div>
                  <p className="eyebrow">Made for your kitchen</p>
                  <h2>{visibleRecipes.length === 0 ? 'Let’s widen the search' : visibleRecipes.length === 1 ? 'One lovely possibility' : `${visibleRecipes.length} lovely possibilities`}</h2>
                  <p>Matched to what you have, your preferences, and the time you want to spend.</p>
                </div>
                <span className="section-number">03</span>
              </div>
              <div className="recipe-scope-panel">
                <div>
                  <strong>Choose your recipe range</strong>
                  <span>{usesOnlyAvailableIngredients(recipeMode) ? 'Only recipes needing no extra non-staple ingredients.' : 'Includes inspiring recipes with a few missing ingredients.'}</span>
                </div>
                <div className="recipe-scope-toggle" role="group" aria-label="Recipe range">
                  <button
                    type="button"
                    aria-pressed={usesOnlyAvailableIngredients(recipeMode)}
                    disabled={Boolean(busy)}
                    onClick={() => handleRecipeScopeChange(RECIPE_MODES.AVAILABLE_ONLY)}
                  >
                    Cook with what I have
                  </button>
                  <button
                    type="button"
                    aria-pressed={!usesOnlyAvailableIngredients(recipeMode)}
                    disabled={Boolean(busy)}
                    onClick={() => handleRecipeScopeChange(RECIPE_MODES.ALL)}
                  >
                    Show all recipes
                  </button>
                </div>
              </div>
              {error && visibleRecipes.length > 0 && <p className="results-inline-error">{error}</p>}
              {recipeSearchEmptyState ? (
                <div className="recipe-mode-empty" role={error ? 'alert' : 'status'}>
                  <strong>{recipeSearchEmptyState.title}</strong>
                  <p>{recipeSearchEmptyState.message}</p>
                  <div>
                    {recipeSearchEmptyState.canRetry && (
                      <button
                        type="button"
                        disabled={Boolean(busy)}
                        onClick={() => usesOnlyAvailableIngredients(recipeMode)
                          ? handleFindCompleteMatches()
                          : handleGenerate()}
                      >
                        {recipeSearchEmptyState.retryLabel || 'Try again'}
                      </button>
                    )}
                    {recipeSearchEmptyState.canShowAll && (
                      <button type="button" disabled={Boolean(busy)} onClick={() => handleRecipeScopeChange(RECIPE_MODES.ALL)}>Show all recipes</button>
                    )}
                  </div>
                  {recipeSearchEmptyState.retryCostNote && <small>{recipeSearchEmptyState.retryCostNote}</small>}
                </div>
              ) : visibleRecipes.length > 0 ? (
                <div className="recipe-grid">
                  {visibleRecipes.map((recipe, index) => (
                    <RecipeCard
                      recipe={recipe}
                      onOpen={setSelectedRecipe}
                      onSave={toggleSaved}
                      saved={savedRecipes.some((item) => item.id === recipe.id)}
                      showRecipePhotos={showRecipePhotos}
                      isTopPick={index === 0}
                      key={recipe.id}
                    />
                  ))}
                </div>
              ) : null}
              {visibleRecipes.length > 0 && provider === 'Edamam' && <EdamamAttribution />}
              {visibleRecipes.length > 0 && <div className="safety-note results-safety"><Icon name="shield" size={18} /><p>{safetyNote}</p></div>}
            </section>
          )}
        </section>
      </main>

      <footer>
        <a className="brand muted" href="#top"><span className="brand-mark"><Icon name="leaf" size={19} /></span><span>mise</span></a>
        <p>Waste less. Cook more. Eat beautifully.</p>
        <button className="footer-link" type="button" onClick={() => setShowLibrary(true)}>Saved & history</button>
        <button className="footer-link" type="button" onClick={() => setShowFeedback(true)}>Feedback</button>
        <button className="footer-link" type="button" onClick={() => setShowPrivacy(true)}>Privacy & data</button>
        <span>Prototype · 2026</span>
      </footer>

      {selectedRecipe && <RecipeModal
        recipe={selectedRecipe}
        safetyNote={safetyNote || DEFAULT_SAFETY_NOTE}
        onClose={() => setSelectedRecipe(null)}
        onSave={toggleSaved}
        saved={savedRecipes.some((item) => item.id === selectedRecipe.id)}
        showRecipePhotos={showRecipePhotos}
      />}
      {showLibrary && <LibraryModal
        savedRecipes={savedRecipes}
        history={history}
        onClose={() => setShowLibrary(false)}
        onOpenRecipe={(recipe) => { setShowLibrary(false); setSelectedRecipe(recipe) }}
        onRemove={(id) => setSavedRecipes((current) => removeSavedRecipe(current, id))}
        onRestore={restoreHistory}
        onClear={() => {
          clearLibrary()
          setSavedRecipes([])
          setHistory([])
        }}
      />}
      {showFeedback && <FeedbackModal onClose={() => setShowFeedback(false)} />}
      {showPrivacy && <PrivacyModal
        onClose={() => setShowPrivacy(false)}
        onClear={() => {
          clearLocalData()
          window.location.reload()
        }}
      />}
      {showAdmin && <PromptAdminScreen
        onClose={closeAdmin}
        onAuthenticated={() => getUsage().then(setUsage).catch(() => {})}
      />}
    </>
  )
}
