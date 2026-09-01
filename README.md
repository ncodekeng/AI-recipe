# Mise recipe prototype

Mise is a mobile-first web prototype that turns photos of a fridge, cupboard, or countertop into editable ingredient suggestions and personalized recipes. It is designed to be demo-ready without cloud credentials, while keeping the AI boundary ready for Azure OpenAI.

## What is included

- Multiple image upload with camera support, previews, removal, size/type validation, and no server-side photo storage
- AI ingredient detection with confidence scores, estimated quantities, and full manual correction
- Allergy, dietary preference, cooking-time, and serving controls
- Three responsive recipe suggestions with match scores and full recipe detail views
- Explicit allergy notices and server-side allergen filtering in demo mode
- A polished responsive React interface and a reusable ASP.NET Core API
- Two AI modes: deterministic local demo mode and configurable Azure OpenAI multimodal mode
- Automatic fallback to demo mode if an Azure request fails during a presentation

## Architecture

```text
React client
  └─ HTTP / multipart API
      └─ ASP.NET Core controllers
          └─ IRecipeAiService
              ├─ Azure OpenAI (vision + structured recipe generation)
              └─ Local demo engine (credential-free fallback)
```

The API owns validation, safety constraints, and AI orchestration. A future native app can call the same endpoints without duplicating business logic.

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

Open `http://localhost:5173`. The API runs at `http://localhost:5050`, and Vite proxies `/api` requests to it.

Demo mode is enabled by default. Upload any food image to exercise the complete flow; descriptive filenames such as `tomatoes-and-spinach.jpg` are also understood by the deterministic demo recognizer.

## Run the production container

The production image compiles React and serves it from the ASP.NET application, so the public site and API share one origin:

```powershell
docker build -t mise-recipe .
docker run --rm -p 8080:8080 mise-recipe
```

Open `http://localhost:8080`. The same image can be deployed directly to Azure Container Apps with external ingress targeting port 8080; Dapr is intentionally disabled while the application remains a single service.

## Connect Azure OpenAI

Create an Azure OpenAI resource, deploy a vision-capable chat model, then set these environment variables before starting the API:

```powershell
$env:FoodAi__Provider = 'AzureOpenAI'
$env:FoodAi__AzureOpenAI__Endpoint = 'https://YOUR-RESOURCE.openai.azure.com'
$env:FoodAi__AzureOpenAI__ApiKey = 'YOUR-KEY'
$env:FoodAi__AzureOpenAI__Deployment = 'YOUR-DEPLOYMENT-NAME'
dotnet run --project server/Recipe.Api
```

Secrets should remain in environment variables or user-secrets and must not be committed. To make Azure failures fail the request instead of falling back during a demo, set `FoodAi__UseDemoFallback` to `false`.

The integration uses Azure OpenAI's current `/openai/v1/chat/completions` endpoint. It sends photos as base64 image inputs for ingredient recognition, then makes a separate structured generation request after the user has corrected the list and selected safety constraints.

## Azure credits

Microsoft currently advertises the following options:

- [Azure free account](https://azure.microsoft.com/en-us/pricing/offers/): USD $200 in credit for a new standard account.
- [Azure for Students](https://azure.microsoft.com/en-us/free/students): USD $100 in credit, with no credit card required for eligible full-time students.
- [Microsoft for Startups](https://www.microsoft.com/en-us/startups): startup credit programs may be more appropriate once the prototype has investor backing; eligibility and amounts should be checked at signup.

Offers, model availability, and regional quotas can change. The local provider means none of these are blockers for the first prototype review.

## Verification

```powershell
dotnet build AIRecipe.slnx
cd client
npm run build
cd ..
powershell -ExecutionPolicy Bypass -File scripts/smoke-test.ps1
```

The smoke test starts the API temporarily, uploads a tiny image, generates three recipes, and checks that egg and dairy ingredients are excluded from a vegan request carrying egg and milk allergies.

## API endpoints

- `GET /api/status` — active provider and Azure configuration status
- `POST /api/ingredients/analyze` — multipart form with one to six `photos`
- `POST /api/recipes/generate` — corrected ingredients, allergens, diet, time, and servings

This remains a prototype: generated recipes are suggestions, not medical advice. Severe-allergy users must verify every product label and use qualified professional guidance.
