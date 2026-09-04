import { useEffect, useMemo, useState } from 'react'
import { getAdminPrompts, resetAdminPrompts, updateAdminPrompts } from './api.js'

const EMPTY_PROMPTS = {
  ingredientRecognitionPrompt: '',
  recipeRecommendationPrompt: '',
}

export default function PromptAdminScreen({ onClose, onAuthenticated }) {
  const [adminKey, setAdminKey] = useState('')
  const [settings, setSettings] = useState(null)
  const [draft, setDraft] = useState(EMPTY_PROMPTS)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    document.body.classList.add('modal-open')
    return () => document.body.classList.remove('modal-open')
  }, [])

  const dirty = useMemo(() => Boolean(settings) && (
    draft.ingredientRecognitionPrompt !== settings.ingredientRecognitionPrompt ||
    draft.recipeRecommendationPrompt !== settings.recipeRecommendationPrompt
  ), [draft, settings])

  useEffect(() => {
    function closeOnEscape(event) {
      if (event.key !== 'Escape') return
      if (!dirty || window.confirm('Discard your unsaved prompt edits?')) onClose()
    }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [dirty, onClose])

  function requestClose() {
    if (!dirty || window.confirm('Discard your unsaved prompt edits?')) onClose()
  }

  function applySettings(next) {
    setSettings(next)
    setDraft({
      ingredientRecognitionPrompt: next.ingredientRecognitionPrompt,
      recipeRecommendationPrompt: next.recipeRecommendationPrompt,
    })
  }

  async function handleUnlock(event) {
    event.preventDefault()
    setBusy('unlocking')
    setError('')
    setNotice('')
    try {
      applySettings(await getAdminPrompts(adminKey))
      setAdminKey('')
      onAuthenticated?.()
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy('')
    }
  }

  async function handleSave(event) {
    event.preventDefault()
    setBusy('saving')
    setError('')
    setNotice('')
    try {
      const next = await updateAdminPrompts(draft)
      applySettings(next)
      setNotice('Prompts saved. New scans and recipe searches will use this configuration.')
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy('')
    }
  }

  async function handleReset() {
    if (!window.confirm('Restore both prompts to the built-in PLATE defaults?')) return

    setBusy('resetting')
    setError('')
    setNotice('')
    try {
      const next = await resetAdminPrompts()
      applySettings(next)
      setNotice('Built-in prompt defaults restored.')
    } catch (requestError) {
      setError(requestError.message)
    } finally {
      setBusy('')
    }
  }

  const maxLength = settings?.maxPromptLength || 8000

  return (
    <div className="admin-screen" role="region" aria-labelledby="prompt-admin-title">
      <div className="admin-shell">
        <button className="admin-close" type="button" onClick={requestClose} aria-label="Close prompt administration">×</button>
        <div className="admin-heading">
          <div>
            <p className="eyebrow">PLATE configuration</p>
            <h1 id="prompt-admin-title">AI prompt studio</h1>
            <p>Shape how PLATE recognises ingredients and recommends sourced recipes without redeploying the application.</p>
          </div>
          {settings && <span className={`admin-status ${settings.usingDefaults ? '' : 'custom'}`}>{settings.usingDefaults ? 'Built-in defaults' : 'Custom prompts active'}</span>}
        </div>

        <aside className="admin-guardrails">
          <div><strong>Safety remains locked</strong><span>These controls are enforced by code and cannot be removed from this screen.</span></div>
          <ul>
            <li>Real publisher sources and citation checks</li>
            <li>Allergen and dietary validation</li>
            <li>Prompt-injection and fabrication protections</li>
            <li>Validated JSON response contracts</li>
          </ul>
        </aside>

        {!settings ? (
          <form className="admin-unlock" onSubmit={handleUnlock}>
            <div>
              <h2>Administrator access</h2>
              <p>The key is checked once by the API. A protected admin session then enables unlimited testing.</p>
            </div>
            <label htmlFor="admin-key">Prompt-admin key</label>
            <div className="admin-key-row">
              <input
                id="admin-key"
                type="password"
                autoComplete="current-password"
                value={adminKey}
                onChange={(event) => setAdminKey(event.target.value)}
                placeholder="Enter admin key"
                required
              />
              <button className="primary-button" type="submit" disabled={busy === 'unlocking' || !adminKey}>
                {busy === 'unlocking' ? 'Checking…' : 'Open editor'}
              </button>
            </div>
            {error && <p className="admin-message error" role="alert">{error}</p>}
            <small>The local development key is documented in the project README.</small>
          </form>
        ) : (
          <form className="prompt-editor" onSubmit={handleSave}>
            <p className="admin-session-note">Administrator session active · AI test attempts are unlimited while this session remains valid.</p>
            <section className="prompt-panel">
              <div className="prompt-panel-heading">
                <div><span>01</span><h2>Ingredient recognition</h2></div>
                <small>{draft.ingredientRecognitionPrompt.length}/{maxLength}</small>
              </div>
              <p>Guides what Azure should identify, how specific names should be, and how quantities or frozen meals are handled.</p>
              <label htmlFor="ingredient-recognition-prompt">Editable guidance</label>
              <textarea
                id="ingredient-recognition-prompt"
                rows="12"
                maxLength={maxLength}
                value={draft.ingredientRecognitionPrompt}
                onChange={(event) => setDraft((current) => ({ ...current, ingredientRecognitionPrompt: event.target.value }))}
                required
              />
            </section>

            <section className="prompt-panel">
              <div className="prompt-panel-heading">
                <div><span>02</span><h2>Recipe recommendations</h2></div>
                <small>{draft.recipeRecommendationPrompt.length}/{maxLength}</small>
              </div>
              <p>Guides recipe style and ranking while the backend continues to require real, cited publisher recipes.</p>
              <label htmlFor="recipe-recommendation-prompt">Editable guidance</label>
              <textarea
                id="recipe-recommendation-prompt"
                rows="12"
                maxLength={maxLength}
                value={draft.recipeRecommendationPrompt}
                onChange={(event) => setDraft((current) => ({ ...current, recipeRecommendationPrompt: event.target.value }))}
                required
              />
            </section>

            {(error || notice) && <p className={`admin-message ${error ? 'error' : 'success'}`} role={error ? 'alert' : 'status'}>{error || notice}</p>}

            <div className="admin-actions">
              <div>
                <button className="secondary-button" type="button" onClick={handleReset} disabled={Boolean(busy)}>Restore defaults</button>
                <button className="text-button" type="button" onClick={() => applySettings(settings)} disabled={!dirty || Boolean(busy)}>Discard edits</button>
              </div>
              <div>
                <span>{settings.updatedAtUtc ? `Last saved ${new Date(settings.updatedAtUtc).toLocaleString()}` : 'Not customised yet'}</span>
                <button className="primary-button" type="submit" disabled={!dirty || Boolean(busy)}>
                  {busy === 'saving' ? 'Saving…' : 'Save prompts'}
                </button>
              </div>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
