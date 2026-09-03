# PLATE / Mise recipe prototype

This repository is the independently built, mobile-first implementation reference for PLATE. The current interface is branded **Mise** until the transferred Base44 project and client brand assets can be inspected. It turns kitchen photos into an editable ingredient list, applies dietary restrictions, and finds sourced online recipes through a reusable ASP.NET Core API.

## What works now

- One to six camera/gallery photos with previews, removal, signature validation, and no application-level photo storage
- Azure OpenAI multimodal ingredient recognition, quantity estimates, confidence, irrelevant-photo filtering, and frozen-meal classification
- A private per-browser seven-day Azure scan-result cache keyed by photo content, without retaining uploaded photo bytes
- Credential-free deterministic demo recognition for local development and presentations
- Required ingredient review with edit, add, remove, quantity correction, and browser-persisted Kitchen Memory
- Fourteen UK allergens, custom avoided ingredients, diet, time, and serving settings
- Deterministic post-response allergen/diet validation; prompts are not the safety boundary
- Azure Responses API web search for real, cited online recipes, with Edamam available as an optional catalogue provider
- Backend ingredient normalization, meaningful pantry-staple handling, traditional near-match-first ordering, a best complete-match second slot, and recent-result diversification
- A license-gated seven-day recipe-result cache keyed by normalized ingredients and every safety preference
- Commercial-use image lookup through Wikimedia Commons structured license metadata, with recipe-specific built-in artwork whenever no image can be verified
- A persisted Show recipe photos switch that prevents remote image requests when disabled
- Visible owned/missing ingredient matching, a primary Top Pick, and source-aware recipe details
- Animated scan progress and responsive recipe-search skeletons for slower provider requests
- An isolated Deliveroo grocery-basket contract with an honest manual handoff until partner basket access is approved
- Lightweight source bookmarks and recent search/result history in browser storage
- Feedback API and UI, request timeouts, friendly error/empty states, daily quotas, one-active-request enforcement, a global estimated budget cutoff, and an AI kill switch
- One responsive React/Vite client and ASP.NET Core .NET 10 API, packaged as a single production container

## Architecture

```text
React mobile web client
  └─ ASP.NET Core API
      ├─ ingredient analysis
      │   ├─ Azure OpenAI vision
      │   └─ deterministic demo provider
      ├─ recipe catalogue
      │   ├─ Azure Responses API with required web search and citation checks
      │   └─ optional Edamam catalogue adapter
      ├─ deterministic dietary safety validator
      └─ usage, budget, and feedback controls
```

The API owns validation and provider orchestration, allowing a future Base44, iOS, or Android client to reuse the same business rules. This is one web app and API, so it has no Dapr dependency or sidecar.

## Codex workflow skills

Shared project instructions live under `.agents/skills`, the repository-scoped location Codex discovers automatically. They preserve the important workflow rules when another developer or Codex account opens this repository:

- `$plate-scan-kitchen` — photo upload, validation, and Azure ingredient recognition
- `$plate-review-pantry` — ingredient corrections, preferences, and safety inputs
- `$plate-find-sourced-recipes` — real online recipes, matching, imagery, ranking, and licensed caching
- `$plate-grocery-handoff` — missing-only shopping lists and delivery-provider boundaries
- `$plate-release-azure` — testing, packaging, Azure configuration, and release handoff

Codex may select a matching skill automatically, or it can be invoked explicitly by name. Restart Codex if a newly added skill does not appear immediately.

## Run locally

Prerequisites: .NET SDK 10+, Node.js 20+, and npm.

In one terminal:

```powershell
dotnet run --project server/Recipe.Api
```

In another terminal:

```powershell
cd client
npm install
npm run dev
```

Open `http://localhost:5173`. The API runs at `http://localhost:5050`, and Vite proxies `/api` to it. Ingredient scanning can run in credential-free demo mode. The default recipe path requires an Azure OpenAI deployment that supports the Responses API `web_search` tool and returns a clear setup error without it.

## Production container

The image compiles React and serves it from ASP.NET so the public site and API share one origin:

```powershell
docker build -t plate-recipe .
docker run --rm -p 8080:8080 plate-recipe
```

Open `http://localhost:8080`. The image is suitable for Azure Container Apps with external ingress targeting port 8080. This Windows development host currently runs Docker in Windows-container mode, so the Linux image must be built by CI, Azure, or a Docker engine switched to Linux containers.

## Deploy on an Azure free subscription

The recommended prototype deployment is a code-based **Azure App Service Free F1** web app. It does not create Container Apps, Container Registry, Kubernetes, Log Analytics, or Dapr resources. React and ASP.NET Core are published together as one ZIP package, and Azure returns its public HTTPS address after deployment.

Prerequisites: an Azure subscription, Azure CLI, .NET SDK 10+, Node.js 20+, and npm.

```powershell
az login
powershell -ExecutionPolicy Bypass -File scripts/deploy-azure-free.ps1 -AppName YOUR-GLOBALLY-UNIQUE-APP-NAME
```

The script creates only these Azure resources:

- One resource group
- One Linux App Service plan pinned to the `F1` free SKU
- One public App Service web app

It refuses to reuse a plan that is not `F1`, which helps avoid accidentally deploying onto a paid SKU. F1 availability and quotas depend on the subscription and region; use `-Location` to choose another region if `uksouth` is unavailable.

Without a private settings file, the public deployment can demonstrate ingredient scanning but cannot return recipes. To enable Azure scanning and Azure web-grounded recipe search, copy the ignored settings template, replace every placeholder locally, and deploy again:

```powershell
Copy-Item azure/appsettings.production.example.json azure/appsettings.production.json
notepad azure/appsettings.production.json
powershell -ExecutionPolicy Bypass -File scripts/deploy-azure-free.ps1 -AppName YOUR-APP-NAME
```

`azure/appsettings.production.json` is ignored by Git. Its values are uploaded to App Service application settings and are never included in the ZIP package. The website hosting can remain within the F1 tier, but Azure model tokens, web-search tool calls, optional Edamam use, a custom domain, and any later storage or monitoring resources have their own pricing and quotas.

To create the deployable ZIP without touching Azure:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-azure.ps1
```

## Connect Azure OpenAI

Create an Azure OpenAI resource and a deployment that supports both image input and Responses API web search, then set environment variables before starting the API:

```powershell
$env:FoodAi__Provider = 'AzureOpenAI'
$env:FoodAi__AzureOpenAI__Endpoint = 'https://YOUR-RESOURCE.openai.azure.com'
$env:FoodAi__AzureOpenAI__ApiKey = 'YOUR-KEY'
$env:FoodAi__AzureOpenAI__Deployment = 'YOUR-DEPLOYMENT-NAME'
$env:RecipeCatalog__Provider = 'AzureWebSearch'
dotnet run --project server/Recipe.Api
```

Ingredient recognition uses `/openai/v1/chat/completions`. Recipe discovery uses `/openai/v1/responses` with `web_search` forced on every request. The same Azure endpoint, key, and deployment settings are used for both paths. Confirm that the selected model and Azure region support image input, Responses, and web search. Secrets must remain in environment variables, Azure Key Vault references, or local user-secrets and must never be committed.

`FoodAi__UseDemoFallback` defaults to `true` for presentations. Set it to `false` when Azure failures should be visible instead of switching to demo recognition.

## Configure AI guidance from the admin screen

PLATE now has a protected prompt editor at `/#admin`. In local development, start the API with the normal tracked launch profile and use the development key shown on the screen:

```powershell
dotnet run --project server/Recipe.Api
```

Open the React site, choose **Admin**, and enter `plate-local-prompts`. The screen edits two global guidance blocks:

- Ingredient recognition guidance for fridge, freezer, cupboard, and worktop scans
- Recipe recommendation guidance for style, practicality, matching, and ranking

The admin screen deliberately does not expose the mandatory source-citation, anti-fabrication, JSON-contract, prompt-injection, allergen, or dietary rules. Those remain enforced in code. Changes apply to future Azure requests immediately, and both caches include the active prompt revision so previously cached results cannot conceal a prompt update.

Prompt text is stored server-side as JSON at `PromptAdmin__StoragePath`; the admin key is never returned by the API or saved by the browser. The tracked local profile enables the feature only for development. For a public Azure deployment, keep it disabled until a long random secret and a writable persistent path are configured:

```powershell
$env:PromptAdmin__Enabled = 'true'
$env:PromptAdmin__ApiKey = 'REPLACE-WITH-A-LONG-RANDOM-SECRET'
$env:PromptAdmin__StoragePath = '/home/data/plate-prompt-settings.json'
```

Use `/home/data/...` for a Linux App Service with persistent storage or `D:\home\data\...` for Windows App Service. Put the admin key in an App Service slot setting or Key Vault reference, require HTTPS, and never commit it. This shared-key screen is suitable for the single-admin prototype; replace it with authenticated role-based access and a shared durable prompt store before running multiple instances or granting several client accounts access.

## Find real recipes with Azure

The default recipe path uses Azure's Responses API with the `web_search` tool. Azure searches current publisher pages and returns structured recipe metadata. PLATE accepts a result only when its exact HTTPS `sourceUrl` also appears in the search tool's returned sources or citation annotations. A URL written only by the model is rejected. Azure also writes a separate practical cooking guide, which the API marks `AiGenerated` and the UI labels as AI guidance rather than publisher instructions. The cited publisher page remains the canonical recipe and is linked at the end.

The model may provide a rough wine suggestion, but the recipe title, ingredient list, quantities, and source URL must come from one cited page. PLATE does not display an AI-written recipe summary. Halal-style searches do not request or return wine suggestions. Deterministic dietary and allergen validation still runs after Azure, because prompting is not a safety boundary. When available, valid results put the best sourced traditional dish requiring one to three missing non-staple ingredients first and the best no-missing recipe second. Remaining results are randomized, with recently displayed recipes moved later.

Azure currently rejects JSON response modes on some Responses API requests that use `web_search`. PLATE therefore leaves the response format in its default text mode, instructs the model to return one JSON object, and parses that object after the search. Each call requests no more than three recipes so the JSON remains compact. If Azure returns prose or malformed JSON, PLATE retries that batch once with a stronger JSON-only instruction and accepts harmless trailing commas. Recipes are accepted only when their source URLs match that batch's real search sources or citation annotations.

PLATE combines small batches to seek six distinct cited results and displays at most six. `RecipeCatalog__AzureWebSearch__BatchSize` defaults to `3`, `MinimumResultCount` defaults to `6`, and `MaxSearchAttempts` defaults to `2`. Later searches exclude URLs already accepted. If the first batch is unreadable, PLATE can try a fresh batch; if an additional batch fails after valid recipes were found, it keeps and displays those earlier recipes. A restrictive pantry, cooking-time limit, provider response, or safety filter can still leave fewer than six honest results; PLATE reports that instead of inventing extras or discarding valid results.

Azure web search does not provide a dependable licensed recipe-image field, so PLATE never accepts an image URL or license claim from the model. When **Show recipe photos** is enabled, the backend separately searches Wikimedia Commons for the exact ranked dish and reads the file's structured image metadata. It accepts only HTTPS bitmap images explicitly marked CC0, Public Domain, CC BY, or CC BY-SA. CC BY and CC BY-SA also require a creator and a valid Creative Commons license URL. Every accepted result returns `imageUrl`, `imageSourceUrl`, `imageLicenseType`, `imageLicenseUrl`, and `imageAttributionRequirements`; the UI displays the required credit and links. Any missing, conflicting, non-commercial, or irrelevant metadata produces `imageUrl: null` and the built-in fallback artwork.

This is deliberately conservative, but not a legal guarantee: Wikimedia says each file can have different reuse conditions and recommends independently checking the file description and non-copyright restrictions before commercial reuse.

The Commons lookup needs no API key. It is enabled by default and can be disabled with `RecipeCatalog__CommercialImages__Enabled=false`; `RecipeCatalog__CommercialImages__MaxCandidates` bounds each search. Turning **Show recipe photos** off sends `showPhotos: false`, skips the lookup, clears all remote-image fields, and uses local artwork.

For visual testing, the tracked `dotnet run` development profile sets `RecipeCatalog__CommercialImages__AllowUnverifiedForTesting=true`. When no fully verified photo is found, Development may show a relevant Commons web image with an orange **Unverified · testing only** warning. The backend requires both that flag and the Development host environment, so the fallback remains disabled in production even if the normal production example settings are used.

When a licensed recipe provider actually returns `instructionLines`, PLATE displays them as provider directions. Standard Edamam web-recipe plans do not return cooking instructions, so those results keep the method on the live publisher page; PLATE does not silently replace them with Azure guidance.

To use Edamam instead, select it explicitly and supply its client-owned credentials. Recipe source links and any licensed provider directions are preserved. Display photography still passes through PLATE's separate commercial-license verification rule:

```powershell
$env:RecipeCatalog__Provider = 'Edamam'
$env:RecipeCatalog__Edamam__AppId = 'YOUR-APP-ID'
$env:RecipeCatalog__Edamam__AppKey = 'YOUR-APP-KEY'
dotnet run --project server/Recipe.Api
```

Confirm Azure/Bing web-search terms and, when applicable, the Edamam commercial plan, caching rights, image rights, and attribution obligations before launch.

Microsoft documents that Grounding with Bing incurs separate tool-call costs and that search data can flow outside the Azure compliance and geographic boundary. Review that behavior and the applicable terms before sending production user data.

### Non-negotiable sourcing rule

- Force Azure's web-search tool for every Azure recipe request.
- Require every recipe to include a valid HTTPS publisher URL that also appears in Azure's returned search sources, or was returned directly by the selected catalogue provider.
- Never ask Azure, the demo provider, or another model to invent a recipe, URL, ingredient, or quantity. Azure may create only the clearly labelled non-canonical cooking guide described above; it must never present it as the publisher's method.
- Never display an unverified searched image in production. A missing or unverifiable license means `imageUrl: null`, except for the explicitly marked Development-only visual-test fallback.
- If credentials, web search, the selected provider, its citations, or safe results are unavailable, show the error and ask the user to retry. Never substitute a made-up recipe.

Sourced saves retain only a small local bookmark (title, publisher, and source URL). With the default cache-disabled configuration, the app does not retain the third-party recipe body or image.

### Seven-day scan and recipe caches

Successful Azure ingredient scans are cached for the same anonymous browser and photo content for up to 168 hours. A cache hit returns the detected ingredient draft without calling Azure or using another scan allowance. Only the response is retained; uploaded photo bytes are not stored. The cache is in server memory, so an App Service recycle clears it.

```powershell
$env:FoodAi__ScanCache__Enabled = 'true'
$env:FoodAi__ScanCache__DurationHours = '168'
$env:FoodAi__ScanCache__MaxEntries = '500'
```

The separate recipe cache can save repeated Azure web-search or Edamam calls. It uses a safety-aware key containing the active provider, active prompt revision, normalized ingredient names, allergens, avoided ingredients, recent result IDs, diet, cooking time, and servings. It holds up to 500 search results in server memory for 168 hours, so a process restart or Azure App Service recycle clears it.

Caching is disabled by default because provider/search contracts may restrict storage. Do not enable it merely to save calls; this implementation retains structured recipe ingredients and requires permission covering every cached field and the way PLATE serves it. After receiving and recording permission for the active provider, enable both gates:

```powershell
$env:RecipeCatalog__Cache__Enabled = 'true'
$env:RecipeCatalog__Cache__ProviderPermissionConfirmed = 'true'
$env:RecipeCatalog__Cache__DurationHours = '168'
$env:RecipeCatalog__Cache__MaxEntries = '500'
```

Setting `Enabled=true` without the permission confirmation does not store anything and writes a warning. A future multi-instance deployment needs an approved shared cache; the current in-memory cache is intentionally suitable only for the single-instance prototype.

## Deliveroo grocery handoff

The UI sends only a recipe's calculated `missingIngredients` to `POST /api/grocery/deliveroo/basket`. `IGroceryBasketService` keeps grocery-provider behavior outside React and `DeliverooBasketService` currently returns a manual shopping-list handoff to Deliveroo without claiming that a basket was created.

Deliveroo does not provide this project with an approved public consumer basket endpoint. True checkout creation therefore remains disabled. It requires a Deliveroo developer/partner account, the applicable Retail Platform or Signature agreement, production API access, the authentication credentials and scopes supplied for that agreement, and the Deliveroo product/catalog, merchant/site, location, and customer context required by their approved basket flow. Do not invent endpoint URLs or credential names before Deliveroo supplies the integration contract.

No new Deliveroo environment variables are required for the current manual handoff.

## Cost and abuse controls

Defaults are configured under `UsageControl` in `appsettings.json` and can be overridden with environment variables:

- 10 scans and 3 recipe requests per anonymous browser per UTC day
- One active AI request per browser
- 5 MB per image, 6 images, and 30 MB per request
- USD 50 estimated global daily cutoff
- `UsageControl__AiEnabled=false` emergency kill switch

The tracked local `dotnet run` profile enables **Reset test uses**, which calls `POST /api/usage/reset` for the current anonymous browser. `UsageControl__AllowTestReset` remains false by default and must remain false on a public deployment.

These counters are intentionally in memory for the single-instance prototype. A public multi-instance launch must move quotas/idempotency to a shared durable store, use authenticated account limits, add gateway/IP bot controls, and measure actual provider token/call cost. Browser IDs alone are not an abuse-proof identity.

Feedback is written as structured application telemetry. Configure a durable production log sink (for example Azure Log Analytics/Application Insights) before relying on it for client review.

## Browser data and privacy

- Photo bytes live only for recognition and hashing during the request and are not retained in the scan cache, written to disk, or added to browser history.
- Azure OpenAI receives photos only when live AI mode is selected.
- Azure web search (Grounding with Bing) receives ingredient names and selected restrictions for the default recipe path and can process them outside the Azure geographic/compliance boundary; Edamam receives them only when explicitly selected. Wikimedia Commons receives only a ranked dish title when recipe photos are enabled.
- The browser stores an anonymous usage ID, corrected Kitchen Memory, preferences, source bookmarks, and recent search/result IDs.
- Users can clear all local PLATE data from **Privacy & data**.

This implementation behavior is not a substitute for a reviewed privacy policy, retention agreement, consent copy, or provider data-processing terms.

## Verification

```powershell
dotnet build AIRecipe.slnx
dotnet test AIRecipe.slnx
cd client
npm test
npm run build
cd ..
powershell -ExecutionPolicy Bypass -File scripts/smoke-test.ps1
```

The backend tests cover forced Azure web search, citation enforcement, strict HTTPS sourcing, halal wine suppression, ingredient aliases, missing-ingredient calculation, pantry basics, exact/near-match ranking, commercial-image license and attribution rejection, image metadata serialization, and the Deliveroo handoff. Frontend tests cover recipe-specific artwork, commercial-license metadata, photo ON/OFF/failure decisions, preference persistence, missing-only basket payloads, and the zero-missing case. The credential-free smoke suite checks image upload, refusal to fabricate recipes, usage tracking, quota enforcement, grocery handoff, and feedback rate limiting. Real Azure, Wikimedia Commons, and optional Edamam responses still require staging verification.

## API endpoints

- `GET /api/status` — AI and recipe-provider status
- `GET /api/usage` — current anonymous daily allowance
- `POST /api/usage/reset` — resets the current browser's counters only when explicitly enabled for local testing
- `POST /api/ingredients/analyze` — multipart form with one to six `photos`
- `POST /api/recipes/generate` — searches sourced recipes using corrected ingredients, restrictions, time, and servings
- `POST /api/grocery/deliveroo/basket` — prepares only the selected recipe's missing ingredients for the supported grocery handoff
- `POST /api/feedback` — rating and optional short comment

## Known launch blockers

- Inspect and transfer the client-owned Base44 project before deciding the final UI/auth/data architecture.
- Supply and validate an Azure model/region with Responses web-search support, plus any optional licensed recipe-provider credentials, in staging.
- Replace in-memory anonymous quotas with account/gateway/shared-store enforcement.
- Add production authentication and server-side persistence if cross-device saves are required.
- Complete legal review for privacy, allergens, halal/kosher wording, and third-party recipe rights.
- Review every selected photo's file page and non-copyright restrictions before commercial launch; automated Commons metadata is not a legal guarantee.
- Configure monitoring, alerts, backups, secret rotation, deployment probes, and a custom domain.

Recipe results are suggestions, not medical advice. The validator finds known text conflicts but cannot guarantee manufacturing, substitution, or cross-contamination safety. Severe-allergy users must verify every product label and consult qualified professionals.
