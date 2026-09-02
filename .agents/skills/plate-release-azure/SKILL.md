---
name: plate-release-azure
description: Package, verify, deploy, or hand off the PLATE web application on Azure. Use for App Service configuration, Azure OpenAI and Edamam settings, production builds, smoke checks, release commits, or public deployment troubleshooting.
---

# PLATE Azure release

Release the React client and ASP.NET Core API as one mobile-friendly public web application.

## Read first

- `README.md` and `PROJECT_HANDOFF.md`
- `scripts/package-azure.ps1`, `scripts/deploy-azure-free.ps1`, and `scripts/smoke-test.ps1`
- `azure/appsettings.production.example.json`
- `Dockerfile` only when a container deployment is explicitly requested

## Release workflow

1. Keep the default free-subscription path on Azure App Service F1. This project does not require Dapr, Kubernetes, Container Apps, or a sidecar.
2. Build React and publish ASP.NET Core together so the public UI and `/api` share one origin.
3. Keep Azure OpenAI and Edamam secrets outside Git. Use local ignored settings, App Service application settings, managed secret references, or user-secrets.
4. Confirm Azure OpenAI is used for live photo recognition and Edamam is configured for sourced recipes. Never enable invented recipe fallback.
5. Leave recipe caching disabled unless provider permission for the retained fields has been confirmed explicitly.
6. Run `dotnet test AIRecipe.slnx --configuration Release`, `npm test --prefix client`, `npm run build --prefix client`, and `scripts/package-azure.ps1` before reporting readiness.
7. Treat a green build as necessary but not sufficient: check `/api/status`, the public SPA route, a scan, provider failure messaging, source links, and the grocery handoff.
8. When asked to commit, use a single-line message under seven words and do not add co-author metadata. Push only when the user explicitly requests it.

## Report

State the deployment target, configured versus demo providers, test results, package path, commit hash, whether it was pushed, and any launch blockers or paid services.
