import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import test from 'node:test'
import { TRENDING_MEALS } from './homeMeals.js'
import { hasValidRecipePhoto } from './recipePhotos.js'

test('trending meals use sourced recipes and verified real photos', () => {
  assert.ok(TRENDING_MEALS.length >= 3)
  for (const meal of TRENDING_MEALS) {
    assert.equal(new URL(meal.sourceUrl).protocol, 'https:')
    assert.equal(hasValidRecipePhoto(meal), true, meal.title)
    assert.match(meal.imageAttributionRequirements, /Wikimedia Commons/)
    assert.equal(existsSync(new URL(`../public${meal.displayImageUrl}`, import.meta.url)), true, meal.title)
    assert.ok(meal.cookingMinutes > 0, meal.title)
    assert.ok(meal.caloriesPerServing > 0, meal.title)
    assert.match(meal.mood, /\S/, meal.title)
  }
})

test('home cards show source-backed nutrition and gold trending moods', () => {
  const app = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
  assert.match(app, /caloriesPerServing/)
  assert.match(app, /variant === 'trending'/)
  assert.match(css, /\.home-meal-mood\s*\{[^}]*top:\s*11px;[^}]*right:\s*11px;[^}]*color:\s*#e2bd68/s)
})

test('home cards use a full image and title, split stats, and full source link', () => {
  const app = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
  assert.match(app, /className="home-meal-image"[\s\S]*className="home-meal-title"[\s\S]*className="home-meal-meta"[\s\S]*className="home-meal-cta"/)
  assert.match(css, /\.home-meal-image\s*\{[^}]*width:\s*100%/s)
  assert.match(css, /\.home-meal-title\s*\{[^}]*width:\s*100%/s)
  assert.match(css, /\.home-meal-meta\s*\{[^}]*width:\s*100%;[^}]*grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\)/s)
  assert.match(css, /\.home-meal-cta\s*\{[^}]*width:\s*100%/s)
})

test('home cards stay compact and duplicate footer navigation is removed', () => {
  const app = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
  assert.match(css, /\.home-meal-link\s*\{[^}]*height:\s*104px/s)
  assert.match(css, /\.home-meal-body\s*\{[^}]*gap:\s*6px;[^}]*padding:\s*9px/s)
  assert.doesNotMatch(app, /Waste less\. Cook more\. Eat beautifully\./)
  assert.doesNotMatch(app, /Prototype · 2026/)
  assert.match(app, /<nav className="bottom-nav" aria-label="PLATE navigation">/)
})

test('home card stats show icons, values, and compact units', () => {
  const app = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  assert.match(app, /<Icon name="clock" size=\{12\} \/>[\s\S]*<strong>\{cookingMinutes \? `\$\{cookingMinutes\} min` : '—'\}<\/strong>/)
  assert.match(app, /<Icon name="flame" size=\{12\} \/>[\s\S]*<strong>\{caloriesPerServing \? `\$\{caloriesPerServing\} CAL` : '—'\}<\/strong>/)
  assert.doesNotMatch(app, /<small><Icon name="clock" size=\{11\} \/> Time<\/small>/)
})

test('photo attribution is an image overlay instead of text below the source link', () => {
  const app = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
  assert.match(app, /className="home-meal-image"[\s\S]*className="home-meal-credit"[\s\S]*className="home-meal-body"/)
  assert.match(app, /aria-label=\{`Photo credit: \$\{meal\.imageAttributionRequirements\}`\}/)
  assert.match(css, /\.home-meal-credit\s*\{[^}]*position:\s*absolute;[^}]*right:\s*8px;[^}]*bottom:\s*7px/s)
})

test('home content clears the fixed navigation at the end of the page', () => {
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
  assert.match(css, /\.hero\s*\{[^}]*padding-bottom:\s*calc\(112px \+ env\(safe-area-inset-bottom\)\)/s)
  assert.match(css, /\.bottom-nav\s*\{[^}]*position:\s*fixed/s)
})

test('home meal rail shows two complete cards per view', () => {
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
  assert.match(css, /\.home-meal-card\s*\{[^}]*width:\s*calc\(\(100% - 12px\) \/ 2\);[^}]*flex:\s*0 0 calc\(\(100% - 12px\) \/ 2\)/s)
})

test('home headline is constrained to two mobile lines', () => {
  const app = readFileSync(new URL('./App.jsx', import.meta.url), 'utf8')
  const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
  assert.match(app, /<h1><span>What’s in your kitchen<\/span><br \/><em>today\?<\/em><\/h1>/)
  assert.match(css, /\.hero h1 > span\s*\{[^}]*white-space:\s*nowrap/s)
})
