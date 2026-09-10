import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

test('admin remains visible in the customer navigation', () => {
  const source = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  const bottomNavigation = source.match(/<nav className="bottom-nav"[\s\S]*?<\/nav>/)?.[0] || ''
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')

  assert.match(bottomNavigation, />Admin</)
  assert.match(bottomNavigation, /onClick=\{openLibraryFromNavigation\}/)
  assert.match(bottomNavigation, /onClick=\{openFeedbackFromNavigation\}/)
  assert.match(source, /window\.location\.hash === '#admin'/)
  assert.doesNotMatch(source, /className="desktop-admin-entry"/)
  assert.match(css, /body\.admin-open \.bottom-nav\s*\{[^}]*display:\s*block;[^}]*z-index:\s*100/s)
})

test('admin prompt studio stays in a mobile-first shell at every viewport', () => {
  const source = readFileSync(new URL('./PromptAdminScreen.jsx', import.meta.url), 'utf8')
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')

  assert.match(source, /className="admin-topbar"/)
  assert.doesNotMatch(source, /className="admin-mobile-warning"/)
  assert.match(css, /\.admin-screen\s*\{[^}]*scrollbar-width:\s*none/s)
  assert.match(css, /\.admin-screen::\-webkit-scrollbar\s*\{[^}]*display:\s*none/s)
  assert.match(css, /Admin uses the same mobile-first shell[\s\S]*?\.admin-screen\s*\{[^}]*width:\s*min\(448px, 100%\)/s)
  assert.match(css, /Admin uses the same mobile-first shell[\s\S]*?\.prompt-editor\s*\{[^}]*grid-template-columns:\s*1fr/s)
})
