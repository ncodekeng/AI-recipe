# PLATE / Mise recipe prototype

This repository is the independently built, mobile-first implementation reference for PLATE. The current interface is branded **Mise** until the transferred Base44 project and client brand assets can be inspected. It turns kitchen photos into an editable ingredient list, applies dietary restrictions, and finds sourced online recipes through a reusable ASP.NET Core API.

## What works now

- One to six camera/gallery photos with previews, removal, signature validation, and no application-level photo storage
- Azure OpenAI multimodal ingredient recognition, quantity estimates, confidence, irrelevant-photo filtering, and frozen-meal classification
- Credential-free deterministic demo recognition for local development and presentations
- Required ingredient review with edit, add, remove, and quantity correction
- Fourteen UK allergens, custom avoided ingredients, diet, time, and serving settings
- Deterministic post-response allergen/diet validation; prompts are not the safety boundary
- Edamam search for real, attributed web recipes with publisher links and provider imagery
- Backend ingredient normalization, meaningful pantry-staple handling, near-match scoring, and provider-aware ranking
- Recipe-specific built-in artwork derived from the title and primary ingredients, used only as a visual fallback when no remote image exists or one fails
- A persisted Show recipe photos switch that prevents remote image requests when disabled
- Visible owned/missing ingredient matching, a primary Top Pick, and source-aware recipe details
- Animated scan progress and responsive recipe-search skeletons for slower provider requests
- An isolated Deliveroo grocery-basket contract with an honest manual handoff until partner basket access is approved
- Lightweight source bookmarks and input-only recent history in browser storage
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
      │   └─ Edamam sourced online-recipe search only
      ├─ deterministic dietary safety validator
      └─ usage, budget, and feedback controls
```

The API owns validation and provider orchestration, allowing a future Base44, iOS, or Android client to reuse the same business rules. This is one web app and API, so it has no Dapr dependency or sidecar.

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

Open `http://localhost:5173`. The API runs at `http://localhost:5050`, and Vite proxies `/api` to it. Ingredient scanning can run in credential-free demo mode, but recipe search requires Edamam credentials and returns a clear setup error without them.

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

Without a private settings file, the public deployment can demonstrate ingredient scanning but cannot return recipes. To enable Azure AI scanning and sourced online recipes, copy the ignored settings template, replace every placeholder locally, and deploy again:

```powershell
Copy-Item azure/appsettings.production.example.json azure/appsettings.production.json
notepad azure/appsettings.production.json
powershell -ExecutionPolicy Bypass -File scripts/deploy-azure-free.ps1 -AppName YOUR-APP-NAME
```

`azure/appsettings.production.json` is ignored by Git. Its values are uploaded to App Service application settings and are never included in the ZIP package. The website hosting can remain within the F1 tier, but Azure OpenAI, Edamam, a custom domain, and any later storage or monitoring resources have their own pricing and quotas.

To create the deployable ZIP without touching Azure:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-azure.ps1
```

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

The primary production recipe path uses [Edamam Recipe Search](https://developer.edamam.com/edamam-recipe-api). Its web-recipe results provide ingredients, provider photography, and an original publisher URL; PLATE links out for the copyrighted cooking method and loads Edamam's required attribution badge. Results are normalized and ranked after the dietary safety check, so a useful provider-ranked recipe needing roughly one to three meaningful ingredients can beat a less useful complete match. Confirm the chosen commercial plan, caching rights, image rights, and attribution obligations before launch.

```powershell
$env:RecipeCatalog__Edamam__AppId = 'YOUR-APP-ID'
$env:RecipeCatalog__Edamam__AppKey = 'YOUR-APP-KEY'
dotnet run --project server/Recipe.Api
```

### Non-negotiable sourcing rule

- Display only recipes returned by the configured online catalogue.
- Require every recipe to include a valid HTTPS link to its original publisher.
- Never ask Azure OpenAI, the demo provider, or another language model to invent or complete a recipe.
- If credentials are missing, Edamam is unavailable, or no safe result is found, show the error and ask the user to retry. Never substitute a made-up recipe.

Sourced saves retain only a small local bookmark (title, publisher, and source URL); the app does not cache the third-party recipe body or image.

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

These counters are intentionally in memory for the single-instance prototype. A public multi-instance launch must move quotas/idempotency to a shared durable store, use authenticated account limits, add gateway/IP bot controls, and measure actual provider token/call cost. Browser IDs alone are not an abuse-proof identity.

Feedback is written as structured application telemetry. Configure a durable production log sink (for example Azure Log Analytics/Application Insights) before relying on it for client review.

## Browser data and privacy

- Photo bytes live only for the recognition request and are not written to disk or browser history by this app.
- Azure OpenAI receives photos only when live AI mode is selected.
- Edamam receives ingredient names and selected restrictions only when real-recipe mode is selected.
- The browser stores an anonymous usage ID, preferences, source bookmarks, and input-only history.
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

The backend tests cover strict HTTPS recipe sourcing, ingredient aliases, missing-ingredient calculation, pantry basics, near-match ranking, provider image mapping/serialization, and the Deliveroo handoff. Frontend tests cover recipe-specific artwork, photo ON/OFF/failure decisions, preference persistence, missing-only basket payloads, and the zero-missing case. The credential-free smoke suite checks image upload, refusal to fabricate recipes, usage tracking, quota enforcement, grocery handoff, and feedback rate limiting. Edamam requires client-owned credentials and should also be exercised in staging before release.

## API endpoints

- `GET /api/status` — AI and recipe-provider status
- `GET /api/usage` — current anonymous daily allowance
- `POST /api/ingredients/analyze` — multipart form with one to six `photos`
- `POST /api/recipes/generate` — searches sourced recipes using corrected ingredients, restrictions, time, and servings
- `POST /api/grocery/deliveroo/basket` — prepares only the selected recipe's missing ingredients for the supported grocery handoff
- `POST /api/feedback` — rating and optional short comment

## Known launch blockers

- Inspect and transfer the client-owned Base44 project before deciding the final UI/auth/data architecture.
- Supply and validate Azure and licensed recipe-provider credentials in staging.
- Replace in-memory anonymous quotas with account/gateway/shared-store enforcement.
- Add production authentication and server-side persistence if cross-device saves are required.
- Complete legal review for privacy, allergens, halal/kosher wording, and third-party recipe rights.
- Configure monitoring, alerts, backups, secret rotation, deployment probes, and a custom domain.

Recipe results are suggestions, not medical advice. The validator finds known text conflicts but cannot guarantee manufacturing, substitution, or cross-contamination safety. Severe-allergy users must verify every product label and consult qualified professionals.
