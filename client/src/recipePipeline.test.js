import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const app = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
const hero = readFileSync(new URL('./RecipeHeroImage.jsx', import.meta.url), 'utf8')
const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')

test('review UI sends a selected main ingredient and a three-to-five result limit', () => {
  assert.match(app, /<label htmlFor="main-ingredient">Main ingredient<\/label>/)
  assert.match(app, /mainIngredient: mainIngredient \|\| validIngredients\[0\]\?\.name \|\| ''/)
  assert.match(app, /maxRecipes: Number\(maxRecipes\)/)
  assert.match(app, /\[3, 4, 5\]\.map/)
})

test('recipe UI displays the deterministic match explanation', () => {
  assert.match(app, /recipe\.matchReason && <p className="recipe-match-reason">/)
  assert.match(app, /recipe\.matchReason && <p className="recipe-match-reason modal-match-reason">/)
})

test('recipe UI displays sourced prep and cook times when available', () => {
  assert.match(app, /Prep \{recipe\.prepMinutes\} min/)
  assert.match(app, /Cook \{recipe\.cookMinutes\} min/)
  assert.match(app, /\$\{recipe\.cookingMinutes\} min total/)
})

test('recipe without an approved photo renders safe fallback artwork', () => {
  assert.match(hero, /useRealImage \? \(/)
  assert.match(hero, /recipe-art-fallback/)
  assert.match(hero, /Source photo unavailable/)
})

test('required image attribution is displayed with source and license links', () => {
  assert.match(app, /recipe\.imageAttributionRequirements/)
  assert.match(app, /href=\{recipe\.imageSourceUrl\}/)
  assert.match(app, /href=\{recipe\.imageLicenseUrl\}/)
})

test('verified publisher extraction is visible and reusable by photo refresh', () => {
  assert.match(app, /Publisher recipe data verified/)
  assert.match(app, /visibleRecipes\.map\(\(\{ id, title, sourceUrl, publisherPageVerified \}\)/)
  assert.match(app, /publisherPageVerified,/)
})

test('results and recipe details hide scrollbars without disabling scrolling', () => {
  assert.match(app, /classList\.toggle\('results-scrollbar-hidden', appScreen === 'results'\)/)
  assert.match(css, /html\.results-scrollbar-hidden,[\s\S]*?scrollbar-width:\s*none;/)
  assert.match(css, /\.recipe-modal::\-webkit-scrollbar[\s\S]*?display:\s*none;/)
  assert.match(css, /\.recipe-modal\s*\{[\s\S]*?overflow-y:\s*auto;/)
})
