FROM node:24-alpine AS client-build
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api-build
WORKDIR /src
COPY server/Recipe.Api/Recipe.Api.csproj server/Recipe.Api/
RUN dotnet restore server/Recipe.Api/Recipe.Api.csproj
COPY server/Recipe.Api/ server/Recipe.Api/
RUN dotnet publish server/Recipe.Api/Recipe.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=api-build /app/publish ./
COPY --from=client-build /src/client/dist ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Recipe.Api.dll"]
