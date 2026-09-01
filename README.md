# PLATE / Mise recipe prototype

This repository is the independently built, mobile-first implementation reference for PLATE. The current interface is branded **Mise** until the transferred Base44 project and client brand assets can be inspected. It turns kitchen photos into an editable ingredient list, applies dietary restrictions, and finds or generates recipe options through a reusable ASP.NET Core API.

## What works now

- One to six camera/gallery photos with previews, removal, signature validation, and no application-level photo storage
- Azure OpenAI multimodal ingredient recognition, quantity estimates, confidence, irrelevant-photo filtering, and frozen-meal classification
- Credential-free deterministic demo recognition for local development and presentations
- Required ingredient review with edit, add, remove, and quantity correction
- Fourteen UK allergens, custom avoided ingredients, diet, time, and serving settings
- Deterministic post-response allergen/diet validation; prompts are not the safety boundary
- Optional Edamam search for real, attributed web recipes with publisher links
- Visible owned/missing ingredient matching and source-aware recipe details
- Lightweight source bookmarks, generated-recipe saves, and input-only recent history in browser storage
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
      │   ├─ Edamam real-recipe search
      │   └─ Azure/demo generated fallback when explicitly selected
      ├─ deterministic dietary safety validator
      └─ usage, budget, and feedback controls
```

The API owns validation and provider orchestration, allowing a future Base44, iOS, or Android client to reuse the same business rules. Dapr is intentionally not enabled while this remains a single service.

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

Open `http://localhost:5173`. The API runs at `http://localhost:5050`, and Vite proxies `/api` to it. Demo mode needs no `.env` file or cloud account.

## Production container

The image compiles React and serves it from ASP.NET so the public site and API share one origin:

```powershell
docker build -t plate-recipe .
docker run --rm -p 8080:8080 plate-recipe
```

Open `http://localhost:8080`. The image is suitable for Azure Container Apps with external ingress targeting port 8080. This Windows development host currently runs Docker in Windows-container mode, so the Linux image must be built by CI, Azure, or a Docker engine switched to Linux containers.

## Connect Azure OpenAI

Create an Azure OpenAI resource and a vision-capable deployment, then set environment variables before starting the API:

```powershell
$env:FoodAi__Provider = 'AzureOpenAI'
$env:FoodAi__AzureOpenAI__Endpoint = 'https://YOUR-RESOURCE.openai.azure.com'
$env:FoodAi__AzureOpenAI__ApiKey = 'YOUR-KEY'
$env:FoodAi__AzureOpenAI__Deployment = 'YOUR-DEPLOYMENT-NAME'
dotnet run --project server/Recipe.Api
```

The integration uses Azure OpenAI's `/openai/v1/chat/completions` endpoint. Photos are sent as image inputs for recognition. Secrets must remain in environment variables, Azure Key Vault references, or local user-secrets and must never be committed.

`FoodAi__UseDemoFallback` defaults to `true` for presentations. Set it to `false` when Azure failures should be visible instead of switching to demo recognition.

## Find real recipes

The production-oriented recipe path uses [Edamam Recipe Search](https://developer.edamam.com/edamam-recipe-api). Its web-recipe results provide ingredients and an original publisher URL; PLATE links out for the copyrighted cooking method and loads Edamam's required attribution badge. Confirm the chosen commercial plan, caching rights, image rights, and attribution obligations before launch.

```powershell
$env:RecipeCatalog__Provider = 'Edamam'
$env:RecipeCatalog__Edamam__AppId = 'YOUR-APP-ID'
$env:RecipeCatalog__Edamam__AppKey = 'YOUR-APP-KEY'
dotnet run --project server/Recipe.Api
```

Real-recipe mode fails clearly if credentials are missing or the provider is unavailable. It does not silently generate recipes. An explicit `RecipeCatalog__UseGeneratedFallback=true` opt-in is available for non-production demos.

Sourced saves retain only a small local bookmark (title, publisher, and source URL); the app does not cache the third-party recipe body or image. Generated recipes can be saved in full in the current browser.

## Cost and abuse controls

Defaults are configured under `UsageControl` in `appsettings.json` and can be overridden with environment variables:

- 10 scans and 3 recipe requests per anonymous browser per UTC day
- One active AI request per browser
- 5 MB per image, 6 images, and 30 MB per request
- USD 50 estimated global daily cutoff
- `UsageControl__AiEnabled=false` emergency kill switch

These counters are intentionally in memory for the single-instance prototype. A public multi-instance launch must move quotas/idempotency to a shared durable store, use authenticated account limits, add gateway/IP bot controls, and measure actual provider token/call cost. Browser IDs alone are not an abuse-proof identity.

Feedback is written as structured application telemetry. Configure a durable production log sink (for example Azure Log Analytics/Application Insights) before relying on it for client review.

## Browser data and privacy

- Photo bytes live only for the recognition request and are not written to disk or browser history by this app.
- Azure OpenAI receives photos only when live AI mode is selected.
- Edamam receives ingredient names and selected restrictions only when real-recipe mode is selected.
- The browser stores an anonymous usage ID, preferences, source bookmarks/generated saves, and input-only history.
- Users can clear all local PLATE data from **Privacy & data**.

This implementation behavior is not a substitute for a reviewed privacy policy, retention agreement, consent copy, or provider data-processing terms.

## Verification

```powershell
dotnet build AIRecipe.slnx
cd client
npm run build
cd ..
powershell -ExecutionPolicy Bypass -File scripts/smoke-test.ps1
```

The smoke suite checks image upload, recipe generation, allergen and custom-avoid filtering, usage tracking, quota enforcement, and feedback rate limiting. Edamam requires client-owned credentials and should also be exercised in staging before release.

## API endpoints

- `GET /api/status` — AI and recipe-provider status
- `GET /api/usage` — current anonymous daily allowance
- `POST /api/ingredients/analyze` — multipart form with one to six `photos`
- `POST /api/recipes/generate` — corrected ingredients, restrictions, time, and servings
- `POST /api/feedback` — rating and optional short comment

## Known launch blockers

- Inspect and transfer the client-owned Base44 project before deciding the final UI/auth/data architecture.
- Supply and validate Azure and licensed recipe-provider credentials in staging.
- Replace in-memory anonymous quotas with account/gateway/shared-store enforcement.
- Add production authentication and server-side persistence if cross-device saves are required.
- Complete legal review for privacy, allergens, halal/kosher wording, and third-party recipe rights.
- Configure monitoring, alerts, backups, secret rotation, deployment probes, and a custom domain.

Recipe results are suggestions, not medical advice. The validator finds known text conflicts but cannot guarantee manufacturing, substitution, or cross-contamination safety. Severe-allergy users must verify every product label and consult qualified professionals.
