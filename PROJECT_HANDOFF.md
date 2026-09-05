# PLATE / AI Recipe Project Handoff

Last updated: 2026-09-05

This file is the portable context for continuing the project with a different Codex account. Read this file, `README.md`, and `git status` before changing anything. Do not discard existing uncommitted work.

## Project and people

- PLATE is the client's intended product: a mobile-first AI cooking application.
- Ruslan coordinates the work, reviews code, testing, architecture, and client communication.
- Andrei/Andrey is expected to own the Base44 workspace and receive the client prototype.
- The developer using this repository is responsible for implementation.
- The original locally built prototype is branded **Mise**. It is a useful implementation/reference, while **PLATE** is the client-facing product.

Never store Base44, Azure, Apple, Google, recipe-provider, or delivery-provider credentials in this repository.

## Current repository state

- Workspace: `D:\Projects\AI-recipe`
- Current implementation: React/Vite client plus ASP.NET Core .NET 10 API.
- The implementation supports credential-free scan demo mode, Azure OpenAI vision, Azure Responses web-grounded recipe search, and optional Edamam recipe search.
- Sample food images are under `sample-images/`.
- A smoke test is available at `scripts/smoke-test.ps1`.
- The React build is served by ASP.NET in the production image on port 8080. No Azure deployment has been completed.
- Dapr is intentionally disabled because the application remains a single service.
- No Azure, Edamam, Base44, Apple, Google, or delivery-provider credentials exist in the repository.

Implementation milestones:

- `6f2e3ae Build AI recipe prototype`
- `c110092 Package production container`
- `2729277 Harden AI request safety`
- `76657bb Add sourced recipe provider`
- `b5fee6c Improve guest kitchen flow`
- `d9d93ba Add recipe library feedback`

Before resuming:

```powershell
git status --short
git diff
```

Always inspect and preserve any new working-tree changes before continuing.

## Existing local application

The Mise prototype already includes:

- Up to 50 food-photo uploads with previews
- Ingredient detection with confidence and estimated quantities
- Manual ingredient edit, add, and removal
- Allergies, dietary preferences, maximum time, and servings
- Citation-verified sourced recipe suggestions and recipe details
- Show-all recipe discovery by default, using compatible subsets from large Kitchen Memory lists
- Missing ingredient highlighting and publisher attribution
- Deterministic server-side allergen/diet validation after every provider response
- Azure OpenAI boundary with demo fallback
- Per-browser quotas, a development-only reset action, concurrency control, an estimated global daily budget, and kill switch
- Locally persisted corrected Kitchen Memory, preferences, recipe saves/bookmarks, and recent search/result history
- Per-browser seven-day Azure scan-result caching keyed by photo content without retaining photo bytes
- Privacy/data controls, feedback submission, timeouts, retry/empty/error states
- Irrelevant-photo reporting and experimental frozen-meal classification in Azure mode
- Mobile-first React UI
- Reusable ASP.NET API for future web/native clients

Important endpoints:

- `GET /api/status`
- `GET /api/usage`
- `POST /api/ingredients/analyze`
- `POST /api/recipes/generate`
- `POST /api/feedback`

Local development:

```powershell
dotnet run --project server/Recipe.Api
```

```powershell
cd client
npm install
npm run dev
```

Verification:

```powershell
dotnet build AIRecipe.slnx
cd client
npm run build
cd ..
powershell -ExecutionPolicy Bypass -File scripts/smoke-test.ps1
```

## Base44 status

Public prototype: <https://aura-kitchen-flow.base44.app/>

The public app is named **PLATE** and is a Base44-hosted installable PWA. It redirects unauthenticated visitors to `/login`, so public inspection cannot verify its internal code, entities, prompts, functions, or complete user flow.

The client's former partner said they created a fresh copy named `PLATE - ANDREI MVP`. Base44's documented transfer workflow supports their explanation:

1. Andrei creates or uses his Base44 workspace.
2. Andrei adds the sender's exact email as **Editor**, not Owner.
3. The current app owner moves the copy into Andrei's workspace.
4. Moving workspaces does not itself change app ownership.
5. The sender must separately make Andrei the app owner.
6. Andrei accepts the ownership-transfer email.
7. Review and replace/disconnect integrations and secrets.
8. Remove the sender from the app/workspace after verification.
9. Export or back up the code and data where Base44 permits it.

The developer does not need to be invited until the move and ownership transfer are complete. Afterward, Andrei can add the developer as an Editor/app collaborator.

Relevant Base44 documentation:

- <https://docs.base44.com/documentation/using-your-workspaces/managing-your-workspace-apps>
- <https://docs.base44.com/documentation/account-and-billing/about-workspaces>

## Base44 implementation strategy

Do not decide to discard either codebase before inspecting the transferred project.

The likely architecture is hybrid:

- Base44 can provide the PLATE UI, authentication, app data, and PWA shell.
- ASP.NET can own AI orchestration, deterministic safety validation, usage limits, provider integrations, and other reusable business logic.
- Azure OpenAI can provide multimodal ingredient recognition and structured recipe assistance.
- A future native client can reuse the ASP.NET API.

Base44 also has built-in AI capabilities, including multimodal `InvokeLLM` calls and structured JSON schemas. Compare those with the ASP.NET/Azure implementation after access is available. The final architecture must consider exportability, cost, safety enforcement, and vendor lock-in.

## What was observed in PLATE

Screenshot review found a visually strong premium dark/gold interface, but the public/screenshotted experience initially communicated recipe discovery more clearly than the core AI workflow.

Positive elements:

- Strong premium visual identity
- Prominent scan call to action
- Attractive recipe cards
- Familiar bottom navigation
- Saved/library concepts
- Multiple-photo scanning and manual ingredient entry appeared in a later scan screenshot

Important problems:

- An uploaded photo entered a `Scanning` state but no ingredient results or error became visible.
- Ingredient identification was therefore only partial, not proven working.
- Ingredient review/correction, allergies, generation, and full cooking flow were not demonstrated in the screenshot set.
- The scan UI showed `0 ingredients detected` while processing, which looked like failure.
- Some prototype content appeared duplicated or incomplete.
- `Saved` versus `Library` naming was inconsistent.
- Fixed navigation appeared to cover content.
- UK and US currencies/market cues were mixed.
- Premium, Kitchen DNA, connected stores, and speculative savings statistics distracted from the unfinished core flow.

Recommended scan copy:

- During processing: `1 photo - Scanning...`
- Progressive result: `1 photo - 4 ingredients found`
- Completion: `1 photo - 7 ingredients detected`

Do not polish peripheral screens before the scan-to-recipe flow works end to end.

## Product objective and required V1 flow

The MVP should prove one journey exceptionally well:

```text
Upload photos
  -> Detect ingredients
  -> Review and correct
  -> Allergies, dietary preferences, and servings
  -> Find citation-verified recipes
  -> Validate restrictions
  -> Show recipe options and missing ingredients
  -> Open cooking instructions
  -> Save
```

The user must never go directly from uncertain image recognition to recipes without reviewing the ingredients.

### P0 launch scope

- Done locally: mobile-friendly web UI; multiple photos; previews/removal/type and size validation.
- Done locally: credential-free demo and high-detail per-photo Azure multimodal recognition with bounded parallel calls, deterministic cross-photo merging, partial-photo failure reporting, confidence, quantity, manual edit/add/remove, empty-result guidance, ignored-photo reporting, and experimental frozen-meal classification.
- Done locally: structured allergens, custom avoid list, diet, servings, and maximum time.
- Done locally: Azure web-grounded recipe search with citation enforcement, optional Edamam provider boundary, no invented fallback, deterministic safety validation, missing ingredient UI, source links, and conditional Edamam attribution.
- Done locally: persistent browser Kitchen Memory, sourced bookmarks, recent search/result history, basic repeat-result diversification, feedback API/UI, timeouts and failure states.
- Done locally: private seven-day Azure scan-result caching; identical photos from the same browser skip Azure and do not consume another scan allowance while the single server process remains alive.
- Done locally for a single instance: daily limits, one active request, estimated budget cutoff, kill switch, and usage display.
- Done locally: clear prototype data-handling copy and browser-data deletion.
- Still required for public MVP: Base44/auth decision, cross-device account persistence, shared durable quota/idempotency store, bot/gateway controls, actual cost telemetry, durable feedback/log sink, staging provider verification, and reviewed privacy/legal copy.
- Still provider-dependent: canonical in-app instructions require licensed content. Production photography is shown only when Wikimedia Commons metadata passes the commercial-license allowlist; otherwise PLATE uses built-in artwork. The local Development profile can show an orange-labelled `UnverifiedTestOnly` image for visual testing, but the backend refuses that fallback outside Development. Azure can show a clearly labelled AI cooking guide, while the publisher link remains canonical.

### P1 candidates

- Google and Apple authentication
- Wine suggestions
- Coordinated starter/main/dessert menu
- Available-equipment filters
- More detailed administration and analytics UI
- Provider handoff for missing-ingredient shopping

### Defer until after product validation

- Automatic Deliveroo/Uber Eats/Just Eat/Ocado baskets
- Brand integrations
- Habit learning and complex personalization
- Proactive notifications
- General AI assistant
- Native iOS and Android applications
- Apple Watch/voice cooking features
- Frozen-meal detection
- Verified halal/kosher certification

## Difficulty corrections

Do not estimate work only from the simplicity of the visible UI.

- Connecting a real AI API: Medium; making it safe and reliable is Hard.
- Online recipes with attribution: Medium, because licensing/API terms matter.
- Allergy-setting UI: Easy; reliable allergen enforcement is Hard.
- Halal/kosher preference UI: Easy/Medium; verified compliance is Hard.
- Automatic weight/volume detection from an image: Hard and unreliable; user correction is Easy.
- Irrelevant-photo detection: Medium.
- Frozen-meal detection: Medium and nonessential.
- Google/Apple authentication: Easy/Medium.
- Mobile-friendly production web app: Medium.
- Automatic provider baskets and checkout: Hard and partnership-dependent.

## AI safety requirements

Prompts are not a sufficient safety boundary.

Required flow:

```text
Structured user restrictions
  -> AI/recipe result
  -> Deterministic ingredient validation
  -> Approve, block, or regenerate
```

Implementation principles:

- Store allergies as structured data.
- Send restrictions from the backend with every request.
- Require structured JSON output.
- Normalize ingredient names and maintain allergen synonyms/derivatives.
- Inspect sauces, stocks, garnishes, seasonings, and substitutions.
- Return safe recipes individually rather than failing an entire batch unnecessarily.
- Allow at most one automatic regeneration to control cost.
- Fail closed when a conflict cannot be resolved.
- Treat free-text restrictions as best effort unless mapped to the supported taxonomy.
- Do not claim `allergen safe`, `halal certified`, or `kosher certified` without verifiable data.
- Prefer wording such as: `No known conflicts were found from the listed ingredients. Verify product labels and cross-contamination warnings.`
- Maintain adversarial tests for milk derivatives, nuts in pesto, soy/wheat in soy sauce, alcohol in sauces, pork/gelatin, stocks, oils, and unsafe substitutions.
- Treat text inside uploaded images as untrusted data and test prompt injection.

Azure AI Content Safety may help with general harmful content and prompt attacks, but it does not replace a domain-specific allergen validator.

## Free-tier cost and abuse controls

- Keep all AI credentials on the backend.
- Use configurable per-user daily allowances; an initial example was three generations per day.
- Limit photo count, image size, tokens, and output length.
- Rate-limit by account plus supporting IP/session signals.
- Allow one active generation per user/session.
- Use idempotency to prevent repeated-button charges.
- Use short-lived, per-user caching only; do not share private image results across users.
- Require verification or CAPTCHA when behaviour appears automated.
- Log token usage, latency, failure rate, and estimated cost.
- Add an application-level daily free-AI budget and emergency kill switch.

Azure budget alerts are notifications, not a guaranteed hard spending ceiling, so the application must enforce its own cutoff.

## Recipe-content decision

The latest requirement is a ChatGPT-like Azure flow that searches for real recipes online, rather than inventing recipes, and shows a publisher link under each result.

The local implementation now defaults to **Azure Responses API web search**:

- Set `RecipeCatalog__Provider=AzureWebSearch`; it reuses the configured Azure OpenAI endpoint, key, and deployment.
- Azure `web_search` is required on every recipe request, and a recipe is accepted only when its exact HTTPS source URL appears in Azure's actual returned sources/citations.
- The model structures source metadata and may add a rough wine pairing and a separately labelled AI cooking guide, but it is forbidden to invent recipes, URLs, ingredients, quantities, or claim that its guide is the publisher's method. The UI does not present an AI-written recipe summary. Halal-style results suppress wine pairing.
- Full third-party cooking instructions are not copied; the user opens the original publisher.
- Azure web search does not supply a dependable licensed image field, so its image URLs and license claims are never trusted. A separate Wikimedia Commons lookup accepts only matching CC0/Public Domain/CC BY/CC BY-SA bitmap files with complete required attribution metadata; any uncertainty produces a null image and built-in artwork.
- Missing configuration, citations, safe results, or provider availability fail visibly without a generated fallback.

The local implementation also includes a protected `/#admin` prompt studio:

- It edits separate ingredient-recognition and recipe-recommendation guidance without a redeploy.
- A server-side admin key protects `GET`, `PUT`, and reset operations under `/api/admin/prompts`; the key is held only in the open browser screen.
- Mandatory citation, anti-fabrication, JSON, prompt-injection, allergen, and dietary controls remain locked in code and are not editable.
- Prompt settings persist to a configurable JSON file. The current single-instance Azure example uses `/home/data/plate-prompt-settings.json`; production must provide a long random secret and persistent writable storage before enabling the feature.
- Scan and recipe cache keys include the active prompt revision so edits take effect on the next request.
- The tracked development profile enables the editor with the local-only key `plate-local-prompts`. Do not reuse that key publicly.

Edamam remains an explicitly selectable optional adapter. If a licensed Edamam response includes `instructionLines`, PLATE marks and displays them as provider directions; standard web-recipe plans normally require users to open the publisher link. Display images still pass through the separate Commons commercial-license verification. Both paths require real staging tests and a commercial/legal review of search, content, image, directions, caching, and attribution terms.

This is not automatically an Easy feature. Do not scrape and republish arbitrary recipe text or photography. Use one of:

- A licensed recipe API
- Client/chef-owned recipes
- Content explicitly licensed for commercial reuse
- A source-link/search experience that does not reproduce protected content

Before public launch, confirm:

- Recipe provider and commercial licence
- Whether full instructions may be stored/displayed
- Required attribution format
- Image rights
- Rate limits and cost
- Allergen/nutrition data quality
- Whether PLATE links out or offers an in-app cooking view

AI can assist with ingredient recognition, ranking, mapping, and substitutions, while the canonical recipe remains sourced and attributed.

## Missing-ingredient delivery research

Officially published APIs do not provide a general way for PLATE to fill a consumer's basket across Deliveroo, Uber Eats, Just Eat, and Ocado.

- Deliveroo Partner/Retail APIs are merchant-side. Signature provides couriers for orders already processed by a merchant.
- Uber Eats Marketplace APIs are merchant-side and require approval. Uber Direct only delivers an order already sold/prepared by a merchant.
- Just Eat JET Connect is for merchant/POS integration. JET Go provides last-mile delivery for an existing order.
- No official public Ocado consumer cart API was found; Ocado Smart Platform is an enterprise retailer product.

Recommended MVP:

1. Build an editable missing-ingredient shopping list.
2. Let the user choose a provider.
3. Copy/share the formatted list and open the provider.
4. Keep checkout, address, payment, and delivery support on the provider's platform.

Automatic product matching, basket creation, and checkout require a retailer/platform commercial partnership. Do not reverse-engineer private consumer APIs.

Suggested future .NET boundary:

```text
Base44/React
  -> ASP.NET Shopping API
      -> Ocado handoff adapter
      -> Deliveroo handoff adapter
      -> Uber Eats handoff adapter
      -> Just Eat handoff adapter
```

## Competitor reference: CheffEye

App Store: <https://apps.apple.com/us/app/cheffeye-ai-recipe-pantry/id6502579584>

CheffEye is a close competitor. Public materials show:

- Pantry photos plus manual ingredients
- Editable detected ingredient lists and quick-add chips
- Three recipe suggestions
- Diet, allergen, skill, cuisine, and time preferences
- Recipe details, voice/timer cooking mode, save/offline, and Apple Watch support
- Social recipe imports
- Free credits, subscriptions, and credit packs
- Credit cost shown before an AI action

Useful patterns for PLATE:

- Show the cost/credit impact before generation.
- Provide a strong ingredient-correction step.
- Return a small set of focused recipes.
- Offer a simple large-text cooking mode.
- Give value before a paywall.

Do not copy its entire feature surface for V1. PLATE can differentiate through chef-reviewed quality, clearer ingredient matching, transparent sources, and stronger dietary safety. Public CheffEye marketing also appeared to make strong allergen/nutrition claims without explaining deterministic validation; PLATE should use more defensible language.

## Open decisions before implementation

- Exact Base44 project contents after transfer
- Base44-only versus hybrid Base44 + ASP.NET architecture
- Azure OpenAI deployment/model and monthly budget
- Licensed recipe source and rights
- Launch market: UK, US, or both
- Supported allergen taxonomy
- Exact meaning of halal/kosher preference support
- Number of photos, recipes, free generations, and automatic retries
- Image-retention period
- Account requirement and guest persistence
- Whether the multi-course item means a coordinated menu or emailing recipes
- Whether provider handoff is sufficient for V1

## Immediate next actions

1. Complete the Base44 workspace move and ownership transfer.
2. Rotate/disconnect inherited secrets and remove former collaborators after verification.
3. Inventory Base44 routes, entities, functions, prompts, integrations, authentication, and plan/credit usage.
4. Run the entire PLATE flow and reproduce the scan that never returned results.
5. Compare Base44 implementation with the local React/.NET prototype.
6. Choose the final V1 architecture.
7. Lock measurable acceptance criteria, launch market, supported dietary taxonomy, and commercial scope.
8. Obtain a staging Azure OpenAI deployment/region supporting image input and Responses web search; verify real responses, citations, cost, latency, data flow, and rights. Verify Edamam only if that optional provider is selected.
9. Move anonymous in-memory quotas/idempotency to a shared durable store and add account/gateway bot enforcement before opening public AI endpoints.
10. Decide whether Base44 owns auth/data/PWA UI while ASP.NET owns AI/safety/provider logic; migrate the proven local vertical slice accordingly.
11. Configure durable logs/Application Insights, alerts, secrets, health probes, custom domain, and CI/CD.
12. Complete security, privacy, allergen, halal/kosher wording, and recipe-rights review before public launch.

## Development and Git preferences

- Preserve unrelated and pre-existing user changes.
- Use `apply_patch` for manual file edits.
- Verify changes with proportionate builds/tests.
- Do not commit or push unless explicitly requested.
- When a commit is requested, use a one-line subject under seven words.
- Never add a `Co-authored-by` trailer.
- Never commit credentials, `.env` secrets, or copied production tokens.

## Resume instruction for a new Codex account

Use this prompt:

> Open `PROJECT_HANDOFF.md` and `README.md` in `D:\Projects\AI-recipe`, inspect `git status` and the current Base44 access situation, preserve all existing changes, then continue from the Immediate next actions. Ask only for information that cannot be discovered safely from the workspace or Base44 project.
