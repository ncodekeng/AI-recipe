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
  addRecentlyViewedRecipe,
  addHistoryEntry,
  clearLibrary,
  getRecentlyShownRecipeIds,
  loadHistory,
  loadRecentlyViewedRecipes,
  loadSavedRecipes,
  removeSavedRecipe,
  toggleSavedRecipe,
} from './library.js'
import { TRENDING_MEALS } from './homeMeals.js'
import {
  getRecipeSearchEmptyState,
  getRecipesForMode,
  INITIAL_RECIPE_SEARCH_STATE,
  RECIPE_MODES,
  recipeSearchReducer,
  usesOnlyAvailableIngredients,
} from './recipeSearchState.js'
import {
  isHeicPhoto,
  PHOTO_INPUT_ACCEPT,
  preparePhotoFile,
} from './photoFiles.js'

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

const MAX_PHOTO_COUNT = 50
const COOKING_TIME_OPTIONS = [20, 30, 45, 60, 90, 120, 180, 240, 0]
const DEFAULT_PREFERENCES = {
  allergens: [],
  dietaryPreference: 'Anything',
  avoidText: '',
  maxCookingMinutes: 45,
  servings: 2,
  mainIngredient: '',
  maxRecipes: 5,
  showRecipePhotos: true,
}
const INITIAL_PREFERENCES = { ...DEFAULT_PREFERENCES, ...loadPreferences() }
const INITIAL_KITCHEN_MEMORY = loadKitchenMemory()
const INITIAL_SAVED_RECIPES = loadSavedRecipes()
const INITIAL_HISTORY = loadHistory()
const INITIAL_RECENT_RECIPES = loadRecentlyViewedRecipes()
const DEFAULT_SAFETY_NOTE = 'No known conflicts were found from the listed ingredients. Always verify product labels, substitutions, and cross-contamination warnings.'
const APP_SCREENS = new Set(['home', 'scan', 'review', 'results'])

function getScreenFromHash() {
  const requested = window.location.hash.replace(/^#/, '')
  if (!APP_SCREENS.has(requested)) return 'home'
  if (requested === 'results') return INITIAL_KITCHEN_MEMORY.length ? 'review' : 'scan'
  return requested
}

function getGreeting() {
  const hour = new Date().getHours()
  if (hour < 12) return 'Good morning'
  if (hour < 18) return 'Good afternoon'
  return 'Good evening'
}

function Icon({ name, size = 20, strokeWidth = 1.8 }) {
  const paths = {
    home: <><path d="M2.5 9.5 10 3l7.5 6.5"/><path d="M4.5 8.5V17h11V8.5"/><path d="M8 17v-5h4v5"/></>,
    scan: <><path d="M3 7V4a1 1 0 0 1 1-1h3"/><path d="M13 3h3a1 1 0 0 1 1 1v3"/><path d="M17 13v3a1 1 0 0 1-1 1h-3"/><path d="M7 17H4a1 1 0 0 1-1-1v-3"/><path d="M5 10h10"/></>,
    message: <><path d="M3 4h14v10H8l-4 3v-3H3V4Z"/><path d="M6 8h8M6 11h5"/></>,
    camera: <><path d="M14.5 5 13 3H7L5.5 5H3a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2.5Z"/><circle cx="10" cy="12" r="3.25"/></>,
    upload: <><path d="M10 14V3"/><path d="m6 7 4-4 4 4"/><path d="M4 11H2.5A1.5 1.5 0 0 0 1 12.5v4A1.5 1.5 0 0 0 2.5 18h15a1.5 1.5 0 0 0 1.5-1.5v-4a1.5 1.5 0 0 0-1.5-1.5H16"/></>,
    sparkles: <><path d="m10 2 1.1 3.1L14 6.5l-2.9 1.4L10 11 8.9 7.9 6 6.5l2.9-1.4L10 2Z"/><path d="m16 11 .8 2.2L19 14l-2.2.8L16 17l-.8-2.2L13 14l2.2-.8L16 11Z"/><path d="m4 12 .7 1.8 1.8.7-1.8.7L4 17l-.7-1.8-1.8-.7 1.8-.7L4 12Z"/></>,
    check: <path d="m4 10 4 4 8-8"/>,
    close: <><path d="m4 4 12 12"/><path d="M16 4 4 16"/></>,
    plus: <><path d="M10 3v14"/><path d="M3 10h14"/></>,
    trash: <><path d="M3 5h14"/><path d="M8 5V3h4v2"/><path d="m5 5 1 13h8l1-13"/><path d="M8 9v5M12 9v5"/></>,
    clock: <><circle cx="10" cy="10" r="8"/><path d="M10 5v5l3 2"/></>,
    flame: <path d="M11.5 2.5c.5 3-1.5 4.1-2.5 5.7C8 6.9 7.8 5.7 8.2 4.5 5.4 6.4 3.5 9 3.5 12.2A6.5 6.5 0 0 0 16.5 12c0-4-2.2-7.1-5-9.5ZM10 17c-1.7 0-3-1.2-3-2.8 0-1.4.8-2.4 2-3.4 0 1 .3 1.8.9 2.4.9-1 1.6-1.8 1.7-3.3.9 1.1 1.4 2.5 1.4 3.7C13 15.5 11.7 17 10 17Z"/>,
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

function HomeMealCard({ meal, onViewed, variant }) {
  const [imageFailed, setImageFailed] = useState(false)
  if (!hasValidRecipePhoto(meal) || imageFailed) return null

  const cookingMinutes = Number.isFinite(meal.cookingMinutes) && meal.cookingMinutes > 0
    ? Math.round(meal.cookingMinutes)
    : null
  const caloriesPerServing = Number.isFinite(meal.caloriesPerServing) && meal.caloriesPerServing > 0
    ? Math.round(meal.caloriesPerServing)
    : null
  const sourceHost = (() => {
    try {
      return new URL(meal.sourceUrl).hostname.replace(/^www\./, '')
    } catch {
      return meal.sourceName || 'original publisher'
    }
  })()

  return (
    <article className="home-meal-card">
      <div className="home-meal-image">
        <a className="home-meal-link" href={meal.sourceUrl} target="_blank" rel="noreferrer" onClick={() => onViewed?.(meal)}>
          <img
            src={meal.displayImageUrl || meal.imageUrl}
            alt={meal.title}
            loading="lazy"
            referrerPolicy="no-referrer"
            onError={() => setImageFailed(true)}
          />
          <span className="home-meal-shade" />
        </a>
        <small className="home-meal-cuisine">{meal.cuisine || meal.sourceName}</small>
        {variant === 'trending' && meal.mood && <span className="home-meal-mood">{meal.mood}</span>}
        <a
          className="home-meal-credit"
          href={meal.imageSourceUrl}
          target="_blank"
          rel="noreferrer"
          aria-label={`Photo credit: ${meal.imageAttributionRequirements}`}
          title={meal.imageAttributionRequirements}
        >
          <span aria-hidden="true">ⓘ</span>
        </a>
      </div>
      <div className="home-meal-body">
        <a className="home-meal-title" href={meal.sourceUrl} target="_blank" rel="noreferrer" onClick={() => onViewed?.(meal)}>
          {meal.title}
        </a>
        <div className="home-meal-meta" aria-label="Recipe summary">
          <span aria-label={cookingMinutes ? `${cookingMinutes} minutes` : 'Cooking time not listed'}>
            <Icon name="clock" size={12} />
            <strong>{cookingMinutes ? `${cookingMinutes} min` : '—'}</strong>
          </span>
          <span aria-label={caloriesPerServing ? `${caloriesPerServing} calories per serving` : 'Calories not listed'}>
            <Icon name="flame" size={12} />
            <strong>{caloriesPerServing ? `${caloriesPerServing} CAL` : '—'}</strong>
          </span>
        </div>
        <a className="home-meal-cta" href={meal.sourceUrl} target="_blank" rel="noreferrer" onClick={() => onViewed?.(meal)}>
          <span>Open {sourceHost}</span>
          <Icon name="arrow" size={13} />
        </a>
      </div>
    </article>
  )
}

function HomeMealRail({ title, subtitle, meals, emptyText, onViewed, variant }) {
  const validMeals = meals.filter(hasValidRecipePhoto)
  const headingId = `home-${title.toLowerCase().replaceAll(' ', '-')}`

  return (
    <section className="home-meal-section" aria-labelledby={headingId}>
      <div className="home-section-heading">
        <div>
          <h2 id={headingId}>{title}</h2>
          <p>{subtitle}</p>
        </div>
      </div>
      {validMeals.length > 0 ? (
        <div className="home-meal-rail">
          {validMeals.map((meal) => <HomeMealCard meal={meal} key={meal.id} onViewed={onViewed} variant={variant} />)}
        </div>
      ) : <p className="home-meal-empty">{emptyText}</p>}
    </section>
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

function Stepper({ currentStep, onStepChange, canReview, canShowResults }) {
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
        const enabled = step === 1 || (step === 2 && canReview) || (step === 3 && canShowResults)
        return (
          <li className={active ? 'active' : complete ? 'complete' : ''} key={number}>
            <button type="button" disabled={!enabled} aria-current={active ? 'step' : undefined} onClick={() => onStepChange(step)}>
              <span className="step-number">{complete ? <Icon name="check" size={15} strokeWidth={2.4} /> : number}</span>
              <span>{label}</span>
            </button>
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
    const selected = Array.from(fileList || [])
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
          accept={PHOTO_INPUT_ACCEPT}
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
          accept={PHOTO_INPUT_ACCEPT}
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
      <p className="upload-hint"><Icon name="shield" size={15} /> JPEG, PNG, GIF or WebP up to 5 MB; HEIC/HEIF up to 20 MB. HEIC/HEIF is converted privately in your browser. Photos are not stored.</p>
    </div>
  )
}

function IngredientEditor({ ingredients, onChange, onRemove, onAdd, onClear }) {
  return (
    <div className="ingredient-editor">
      <div className="ingredient-editor-actions">
        <button className="text-button" type="button" onClick={onAdd}>
          <Icon name="plus" size={17} strokeWidth={2.2} /> Add an ingredient
        </button>
        <button className="text-button danger" type="button" onClick={onClear}>
          <Icon name="trash" size={17} /> Clear all
        </button>
      </div>
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
        {recipe.matchReason && <p className="recipe-match-reason">{recipe.matchReason}</p>}
        <div className="recipe-meta">
          {Number.isFinite(recipe.prepMinutes) && <span><Icon name="clock" size={16} /> Prep {recipe.prepMinutes} min</span>}
          {Number.isFinite(recipe.cookMinutes) && <span><Icon name="clock" size={16} /> Cook {recipe.cookMinutes} min</span>}
          {!Number.isFinite(recipe.prepMinutes) && !Number.isFinite(recipe.cookMinutes) && <span><Icon name="clock" size={16} /> {recipe.cookingMinutes > 0 ? `${recipe.cookingMinutes} min total` : 'Time on source'}</span>}
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
        {recipe.sourceVerified && recipe.sourceUrl && (
          <a className="recipe-source" href={recipe.sourceUrl} target="_blank" rel="noreferrer">
            <span><small>Verified publisher source</small><strong>{recipe.sourceTitle || recipe.sourceName || 'View detailed recipe'}</strong></span>
            <Icon name="external" size={16} />
          </a>
        )}
      </div>
    </article>
  )
}

function RecipeModal({ recipe, onClose, onStartCooking, safetyNote, onSave, saved, showRecipePhotos }) {
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
  const isSourced = Boolean(recipe.sourceVerified && recipe.sourceUrl)
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
              {Number.isFinite(recipe.prepMinutes) && <span><Icon name="clock" size={17} /> Prep {recipe.prepMinutes} min</span>}
              {Number.isFinite(recipe.cookMinutes) && <span><Icon name="clock" size={17} /> Cook {recipe.cookMinutes} min</span>}
              {!Number.isFinite(recipe.prepMinutes) && !Number.isFinite(recipe.cookMinutes) && <span><Icon name="clock" size={17} /> {recipe.cookingMinutes > 0 ? `${recipe.cookingMinutes} min total` : 'Time on source'}</span>}
              <span><Icon name="users" size={18} /> {recipe.servings} servings</span>
              <span>{recipe.difficulty}</span>
            </div>
          </div>
        </RecipeHeroImage>
        <PhotoAttribution recipe={recipe} showRecipePhotos={showRecipePhotos} />
        <div className="modal-content">
          {recipe.matchReason && <p className="recipe-match-reason modal-match-reason">{recipe.matchReason}</p>}
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
                  <button className="start-cooking-button" type="button" onClick={() => onStartCooking(recipe)}>
                    <Icon name="sparkles" size={18} /> Start step-by-step cooking <Icon name="arrow" size={18} />
                  </button>
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

function CookingMode({ recipe, onClose }) {
  const directions = Array.isArray(recipe.steps)
    ? recipe.steps.filter((step) => typeof step === 'string' && step.trim())
    : []
  const [stepIndex, setStepIndex] = useState(0)
  const touchStartX = useRef(null)
  const isAiGuide = recipe.directionsKind === 'AiGenerated'

  useEffect(() => {
    setStepIndex(0)
  }, [recipe.id])

  useEffect(() => {
    function handleKeyDown(event) {
      if (event.key === 'Escape') onClose()
      if (event.key === 'ArrowRight') setStepIndex((current) => Math.min(current + 1, directions.length - 1))
      if (event.key === 'ArrowLeft') setStepIndex((current) => Math.max(current - 1, 0))
    }
    document.addEventListener('keydown', handleKeyDown)
    document.body.classList.add('modal-open')
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      document.body.classList.remove('modal-open')
    }
  }, [directions.length, onClose])

  if (!directions.length) return null

  const isFirst = stepIndex === 0
  const isLast = stepIndex === directions.length - 1

  function handleTouchStart(event) {
    touchStartX.current = event.touches.length === 1 ? event.touches[0].clientX : null
  }

  function handleTouchEnd(event) {
    if (touchStartX.current === null || !event.changedTouches.length) return
    const distance = event.changedTouches[0].clientX - touchStartX.current
    touchStartX.current = null
    if (Math.abs(distance) < 48) return
    setStepIndex((current) => distance < 0
      ? Math.min(current + 1, directions.length - 1)
      : Math.max(current - 1, 0))
  }

  return (
    <section className="cooking-mode" role="dialog" aria-modal="true" aria-labelledby="cooking-mode-title" onTouchStart={handleTouchStart} onTouchEnd={handleTouchEnd}>
      <header className="cooking-mode-header">
        <div>
          <span>{isAiGuide ? 'AI cooking guide' : `Cooking with ${recipe.sourceName || 'the recipe publisher'}`}</span>
          <h2 id="cooking-mode-title">{recipe.title}</h2>
        </div>
        <button type="button" onClick={onClose} aria-label="Close cooking mode"><Icon name="close" size={21} /></button>
      </header>

      <div className="cooking-progress" aria-label={`Step ${stepIndex + 1} of ${directions.length}`}>
        <span style={{ width: `${((stepIndex + 1) / directions.length) * 100}%` }} />
      </div>

      <main className="cooking-step" aria-live="polite">
        <p className="cooking-step-count">Step {stepIndex + 1} of {directions.length}</p>
        <article key={stepIndex}>
          <span>{stepIndex + 1}</span>
          <p>{directions[stepIndex]}</p>
        </article>
        <p className="cooking-swipe-hint">Swipe left or right with one finger</p>
        {isAiGuide && <p className="cooking-source-note"><Icon name="sparkles" size={16} /> AI-generated guidance - confirm it with the original recipe.</p>}
      </main>

      <footer className="cooking-controls">
        <button className="cooking-previous" type="button" disabled={isFirst} onClick={() => setStepIndex((current) => Math.max(current - 1, 0))}>
          <Icon name="arrow" size={18} /> Previous
        </button>
        {isLast ? (
          <button className="cooking-next" type="button" onClick={onClose}>Finish cooking <Icon name="check" size={18} /></button>
        ) : (
          <button className="cooking-next" type="button" onClick={() => setStepIndex((current) => Math.min(current + 1, directions.length - 1))}>
            Next step <Icon name="arrow" size={18} />
          </button>
        )}
        {recipe.sourceUrl && <a href={recipe.sourceUrl} target="_blank" rel="noreferrer">Check original live recipe <Icon name="external" size={14} /></a>}
      </footer>
    </section>
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
  const [appScreen, setAppScreen] = useState(getScreenFromHash)
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
  const [mainIngredient, setMainIngredient] = useState(() =>
    typeof INITIAL_PREFERENCES.mainIngredient === 'string'
      ? INITIAL_PREFERENCES.mainIngredient.slice(0, 100)
      : '')
  const [maxRecipes, setMaxRecipes] = useState(() => [3, 4, 5].includes(Number(INITIAL_PREFERENCES.maxRecipes))
    ? Number(INITIAL_PREFERENCES.maxRecipes)
    : DEFAULT_PREFERENCES.maxRecipes)
  const [showRecipePhotos, setShowRecipePhotos] = useState(() => typeof INITIAL_PREFERENCES.showRecipePhotos === 'boolean'
    ? INITIAL_PREFERENCES.showRecipePhotos
    : DEFAULT_PREFERENCES.showRecipePhotos)
  const [recipeSearch, dispatchRecipeSearch] = useReducer(
    recipeSearchReducer,
    INITIAL_RECIPE_SEARCH_STATE,
  )
  const { mode: recipeMode, recipes, availableOnlyRecipes, hasCompletedSearch } = recipeSearch
  const [selectedRecipe, setSelectedRecipe] = useState(null)
  const [cookingRecipe, setCookingRecipe] = useState(null)
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
  const [recentRecipes, setRecentRecipes] = useState(INITIAL_RECENT_RECIPES)
  const [reviewStarted, setReviewStarted] = useState(false)

  const reviewRef = useRef(null)
  const resultsRef = useRef(null)
  const photoUrlsRef = useRef(new Set())

  const currentStep = appScreen === 'results' ? 3 : appScreen === 'review' ? 2 : 1
  const validIngredients = useMemo(
    () => ingredients.filter((item) => item.name.trim()),
    [ingredients],
  )

  useEffect(() => {
    const selected = validIngredients.find((item) =>
      item.name.trim().toLowerCase() === mainIngredient.trim().toLowerCase())
    if (!selected)
    {
      setMainIngredient(validIngredients[0]?.name.trim() || '')
    }
  }, [validIngredients, mainIngredient])
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
    const syncRoute = () => {
      const hash = window.location.hash.replace(/^#/, '')
      setShowAdmin(hash === 'admin')
      if (APP_SCREENS.has(hash)) {
        const nextScreen = hash === 'results' && !hasCompletedSearch
          ? ingredients.length > 0 ? 'review' : 'scan'
          : hash
        setAppScreen(nextScreen)
      }
    }
    window.addEventListener('hashchange', syncRoute)
    window.addEventListener('popstate', syncRoute)
    return () => {
      window.removeEventListener('hashchange', syncRoute)
      window.removeEventListener('popstate', syncRoute)
    }
  }, [hasCompletedSearch, ingredients.length])

  useEffect(() => {
    savePreferences({ allergens, dietaryPreference, avoidText, maxCookingMinutes, servings, mainIngredient, maxRecipes, showRecipePhotos })
  }, [allergens, dietaryPreference, avoidText, maxCookingMinutes, servings, mainIngredient, maxRecipes, showRecipePhotos])

  useEffect(() => {
    if (busy !== 'generating') return
    const frame = window.requestAnimationFrame(() => {
      document.getElementById('recipe-loading')?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [busy])

  function goToScreen(nextScreen) {
    setShowAdmin(false)
    setAppScreen(nextScreen)
    const nextHash = `#${nextScreen}`
    if (window.location.hash !== nextHash) window.history.pushState(null, '', nextHash)
    window.requestAnimationFrame(() => window.scrollTo({ top: 0, behavior: 'smooth' }))
  }

  function leaveAdminForOverlay() {
    setShowAdmin(false)
    if (window.location.hash === '#admin') {
      setAppScreen('home')
      window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#home`)
    }
  }

  function openLibraryFromNavigation() {
    leaveAdminForOverlay()
    setShowLibrary(true)
  }

  function openFeedbackFromNavigation() {
    leaveAdminForOverlay()
    setShowFeedback(true)
  }

  function handleWorkflowStepChange(step) {
    if (step === 1) goToScreen('scan')
    if (step === 2 && ingredients.length > 0) goToScreen('review')
    if (step === 3 && hasCompletedSearch) goToScreen('results')
  }

  function goBackFromWorkflow() {
    if (appScreen === 'results') goToScreen('review')
    else if (appScreen === 'review') goToScreen('scan')
    else goToScreen('home')
  }

  function invalidateRecipeResults() {
    dispatchRecipeSearch({ type: 'invalidate' })
    setSelectedRecipe(null)
    setSafetyNote('')
    setNotice('')
  }

  function openRecipe(recipe) {
    setSelectedRecipe(recipe)
    rememberViewedRecipe(recipe)
  }

  function rememberViewedRecipe(recipe) {
    if (!hasValidRecipePhoto(recipe)) return
    setRecentRecipes((current) => addRecentlyViewedRecipe(current, recipe))
  }

  async function addPhotos(files) {
    setError('')
    setReviewStarted(false)
    const remaining = MAX_PHOTO_COUNT - photos.length
    invalidateRecipeResults()
    if (remaining <= 0) {
      setError(`You can add up to ${MAX_PHOTO_COUNT} photos at a time.`)
      return
    }

    const candidates = files.slice(0, remaining)
    const needsHeicConversion = candidates.some(isHeicPhoto)
    const prepared = []
    const rejected = []
    if (needsHeicConversion) {
      setBusy('preparingPhotos')
      setNotice('Converting HEIC/HEIF photos to JPEG on this device...')
    }

    try {
      for (const candidate of candidates) {
        try {
          prepared.push(await preparePhotoFile(candidate))
        } catch (preparationError) {
          rejected.push(preparationError.message)
        }
      }

      const additions = prepared.map(({ file }) => {
        const url = URL.createObjectURL(file)
        photoUrlsRef.current.add(url)
        return { id: crypto.randomUUID(), file, url }
      })
      if (additions.length) setPhotos((current) => [...current, ...additions])

      const convertedCount = prepared.filter((item) => item.convertedFromHeic).length
      if (convertedCount) {
        setNotice(`${convertedCount} HEIC/HEIF photo${convertedCount === 1 ? ' was' : 's were'} converted to JPEG and ${convertedCount === 1 ? 'is' : 'are'} ready to scan.`)
      }
      if (rejected.length) {
        setError(`${rejected.length} photo${rejected.length === 1 ? ' was' : 's were'} skipped. ${rejected[0]}`)
      } else if (files.length > remaining) {
        setError(`Only the first ${remaining} photos were added because the limit is ${MAX_PHOTO_COUNT}.`)
      }
    } finally {
      if (needsHeicConversion) setBusy('')
    }
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
      goToScreen('review')
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

  function clearKitchenMemoryList() {
    if (!window.confirm('Clear every ingredient from Kitchen Memory?')) return
    updateKitchenMemory([])
    setNotice('Kitchen Memory cleared. Your photos and preferences were kept.')
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
    goToScreen('results')
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
        mainIngredient: mainIngredient || validIngredients[0]?.name || '',
        maxCookingMinutes: Number(maxCookingMinutes),
        servings: Number(servings),
        maxRecipes: Number(maxRecipes),
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
    setMainIngredient(typeof entry.mainIngredient === 'string' ? entry.mainIngredient.slice(0, 100) : restoredIngredients[0].name)
    setMaxCookingMinutes(COOKING_TIME_OPTIONS.includes(Number(entry.maxCookingMinutes)) ? Number(entry.maxCookingMinutes) : 45)
    setServings([1, 2, 3, 4, 6].includes(Number(entry.servings)) ? Number(entry.servings) : 2)
    setMaxRecipes([3, 4, 5].includes(Number(entry.maxRecipes)) ? Number(entry.maxRecipes) : 5)
    invalidateRecipeResults()
    setShowLibrary(false)
    setError('')
    setNotice('Your previous ingredients and settings are ready to review.')
    goToScreen('review')
  }

  function openAdmin() {
    window.location.hash = 'admin'
    setShowAdmin(true)
  }

  function closeAdmin() {
    setShowAdmin(false)
    setAppScreen('home')
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#home`)
  }

  return (
    <>
      <header className="site-header">
        <a className="brand" href="#home" aria-label="PLATE home" onClick={() => setAppScreen('home')}>
          <span className="brand-mark"><Icon name="leaf" size={23} strokeWidth={2} /></span>
          <span>PLATE</span>
        </a>
        <nav aria-label="Main navigation">
          <span className={`provider-badge ${provider === 'Azure OpenAI' || provider === 'Azure Web Search' || provider === 'Edamam' ? 'live' : ''}`}>
            <span /> {provider}
          </span>
        </nav>
      </header>

      <main id="top" aria-busy={Boolean(busy)}>
        {appScreen === 'home' && <section className="hero">
          <div className="hero-copy">
            <p className="hero-greeting">{getGreeting()}</p>
            <h1><span>What’s in your kitchen</span><br /><em>today?</em></h1>
            <p className="hero-lead">Your kitchen is thriving — let’s cook something new.</p>
          </div>
          <div className="home-actions">
            <button className="hero-note" type="button" onClick={() => goToScreen('scan')}>
              <span className="hero-scan-icon"><Icon name="scan" size={26} strokeWidth={1.7} /></span>
              <span className="hero-note-copy"><strong>AI Ingredient Scan</strong><small>Snap your fridge, get instant meals</small></span>
              <Icon name="sparkles" size={22} strokeWidth={1.6} />
            </button>
            {ingredients.length > 0 && (
              <button className="hero-note hero-memory" type="button" onClick={() => goToScreen('review')}>
                <span className="hero-scan-icon"><Icon name="edit" size={24} /></span>
                <span className="hero-note-copy"><strong>Kitchen Memory</strong><small>Review {ingredients.length} saved ingredient{ingredients.length === 1 ? '' : 's'}</small></span>
                <Icon name="arrow" size={20} />
              </button>
            )}
          </div>
          {showRecipePhotos && (
            <HomeMealRail
              title="Recently viewed"
              subtitle="Real recipes you opened before."
              meals={recentRecipes}
              emptyText="Open a recipe with a verified photo and it will appear here."
              onViewed={rememberViewedRecipe}
              variant="recent"
            />
          )}
          {showRecipePhotos && (
            <HomeMealRail
              title="Trending meals"
              subtitle="Popular ideas from real recipe publishers."
              meals={TRENDING_MEALS}
              onViewed={rememberViewedRecipe}
              variant="trending"
            />
          )}
        </section>}

        {appScreen !== 'home' && <section className="creator workflow-screen" id="how-it-works">
          <div className="workflow-toolbar">
            <button type="button" onClick={goBackFromWorkflow}><Icon name="arrow" size={16} /> Back</button>
            <span>{appScreen === 'scan' ? 'Kitchen scan' : appScreen === 'review' ? 'Kitchen Memory' : 'Recipe matches'}</span>
          </div>
          <Stepper
            currentStep={currentStep}
            canReview={ingredients.length > 0}
            canShowResults={hasCompletedSearch}
            onStepChange={handleWorkflowStepChange}
          />

          {appScreen === 'scan' && <div className="work-card screen-card">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Step one</p>
                <h2>What’s in your kitchen?</h2>
                <p>Add up to 50 clear photos. Different angles help us spot more ingredients.</p>
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
          </div>}

          {error && <div className="alert error" role="alert"><span>!</span><p>{error}</p></div>}
          {notice && <div className="alert info" role="status"><Icon name="sparkles" size={19} /><p>{notice}</p></div>}

          {appScreen === 'review' && ingredients.length > 0 && (
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
                    onClear={clearKitchenMemoryList}
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
                        <label htmlFor="main-ingredient">Main ingredient</label>
                        <div className="select-wrap">
                          <select id="main-ingredient" value={mainIngredient} onChange={(event) => { setReviewStarted(true); setMainIngredient(event.target.value); invalidateRecipeResults() }}>
                            {validIngredients.map((ingredient) => <option value={ingredient.name} key={ingredient.id}>{ingredient.name}</option>)}
                          </select>
                          <Icon name="chevron" size={16} />
                        </div>
                        <p className="field-help">Every result must use this ingredient.</p>
                      </div>
                      <div className="field-group">
                        <label htmlFor="max-recipes">Results</label>
                        <div className="select-wrap">
                          <select id="max-recipes" value={maxRecipes} onChange={(event) => { setReviewStarted(true); setMaxRecipes(Number(event.target.value)); invalidateRecipeResults() }}>
                            {[3, 4, 5].map((value) => <option value={value} key={value}>{value} recipes</option>)}
                          </select>
                          <Icon name="chevron" size={16} />
                        </div>
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

          {appScreen === 'review' && ingredients.length === 0 && (
            <section className="workflow-empty" role="status">
              <span><Icon name="scan" size={30} /></span>
              <h2>Your Kitchen Memory is empty</h2>
              <p>Scan your kitchen or add photos to create an editable ingredient list.</p>
              <button className="primary-button" type="button" onClick={() => goToScreen('scan')}>Scan my kitchen <Icon name="arrow" size={18} /></button>
            </section>
          )}

          {appScreen === 'results' && <>
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
                      onOpen={openRecipe}
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
          </>}
        </section>}
      </main>

      <nav className="bottom-nav" aria-label="PLATE navigation">
        <div className="bottom-nav-glass">
          <button className={appScreen === 'home' && !showAdmin ? 'active' : ''} type="button" onClick={() => goToScreen('home')}><Icon name="home" size={19} /><span>Home</span></button>
          <button className={appScreen !== 'home' && !showAdmin ? 'active' : ''} type="button" onClick={() => goToScreen('scan')}><Icon name="scan" size={19} /><span>Scan</span></button>
          <button className={showLibrary ? 'active' : ''} type="button" onClick={openLibraryFromNavigation}>
            <span className="bottom-nav-icon"><Icon name="bookmark" size={19} />{savedRecipes.length > 0 && <i>{savedRecipes.length}</i>}</span>
            <span>Saved</span>
          </button>
          <button className={showFeedback ? 'active' : ''} type="button" onClick={openFeedbackFromNavigation}><Icon name="message" size={19} /><span>Feedback</span></button>
          <button className={showAdmin ? 'active' : ''} type="button" onClick={openAdmin}><Icon name="settings" size={19} /><span>Admin</span></button>
        </div>
      </nav>

      {selectedRecipe && <RecipeModal
        recipe={selectedRecipe}
        safetyNote={safetyNote || DEFAULT_SAFETY_NOTE}
        onClose={() => setSelectedRecipe(null)}
        onStartCooking={(recipe) => { setSelectedRecipe(null); setCookingRecipe(recipe) }}
        onSave={toggleSaved}
        saved={savedRecipes.some((item) => item.id === selectedRecipe.id)}
        showRecipePhotos={showRecipePhotos}
      />}
      {cookingRecipe && <CookingMode recipe={cookingRecipe} onClose={() => setCookingRecipe(null)} />}
      {showLibrary && <LibraryModal
        savedRecipes={savedRecipes}
        history={history}
        onClose={() => setShowLibrary(false)}
        onOpenRecipe={(recipe) => { setShowLibrary(false); openRecipe(recipe) }}
        onRemove={(id) => setSavedRecipes((current) => removeSavedRecipe(current, id))}
        onRestore={restoreHistory}
        onClear={() => {
          clearLibrary()
          setSavedRecipes([])
          setHistory([])
          setRecentRecipes([])
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
