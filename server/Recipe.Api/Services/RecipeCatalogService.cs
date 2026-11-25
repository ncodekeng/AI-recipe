using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeCatalogService(
    AzureGroundedRecipeClient azureWebSearch,
    GoogleRecipeSearchClient googleSearch,
    EdamamRecipeClient edamam,
    PublisherRecipePageClient publisherPages,
    CommercialRecipeImageClient commercialImages,
    CookingGuideWriter cookingGuides,
    RecipeSafetyValidator safetyValidator,
    RecipeRankingService ranking,
    IngredientNormalizer normalizer,
    RecipeSearchCache cache,
    IOptions<RecipeCatalogOptions> options,
    ILogger<RecipeCatalogService> logger) : IRecipeCatalogService
{
    private readonly RecipeCatalogOptions _options = options.Value;

    public async Task<RecipeGenerationResponse> FindRecipesAsync(
        GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        var (effectiveRequest, _) = ApplyDietaryRestrictions(request);
        var provider = _options.Provider.Trim();
        var useAzureWebSearch = provider.Equals("AzureWebSearch", StringComparison.OrdinalIgnoreCase);
        var useGoogleSearch = provider.Equals("GoogleSearch", StringComparison.OrdinalIgnoreCase);
        var useEdamam = provider.Equals("Edamam", StringComparison.OrdinalIgnoreCase);

        if (!useAzureWebSearch && !useGoogleSearch && !useEdamam)
        {
            throw new RecipeCatalogException(
                $"Unknown recipe provider '{provider}'. Use AzureWebSearch, GoogleSearch or Edamam.");
        }

        if (useAzureWebSearch && !azureWebSearch.IsConfigured)
        {
            throw new RecipeCatalogException(
                "Azure web-grounded recipe search requires an Azure OpenAI endpoint, API key, and deployment. PLATE will not invent a replacement recipe.");
        }

        if (useGoogleSearch && !googleSearch.IsConfigured)
        {
            throw new RecipeCatalogException(
                "Google recipe discovery requires a Serper API key. PLATE will not invent a replacement recipe.");
        }

        if (useEdamam && !_options.Edamam.IsConfigured)
        {
            throw new RecipeCatalogException(
                "Real recipe search requires Edamam credentials. PLATE will not invent a replacement recipe.");
        }

        if (cache.TryGet(effectiveRequest, out var cachedResponse) && cachedResponse is not null)
        {
            var cachedWithPhotos = await ApplyCommercialImagesAsync(
                cachedResponse,
                effectiveRequest.ShowPhotos,
                cancellationToken);
            return cachedWithPhotos;
        }

        try
        {
            // Stage timings, because a 122-second search has to be attributed before it can be fixed.
            // One line per request so a benchmark run can be read straight out of the log, and cheap
            // enough to leave on: a handful of Stopwatch reads against calls measured in seconds.
            var total = System.Diagnostics.Stopwatch.StartNew();
            var stage = System.Diagnostics.Stopwatch.StartNew();

            // GoogleSearch has already read every page it returns, so it needs no enrichment pass below:
            // the publisher data is where its recipes came from in the first place, and asking for it
            // again would fetch all of those pages a second time.
            var response = useAzureWebSearch
                ? await azureWebSearch.FindRecipesAsync(effectiveRequest, cancellationToken)
                : useGoogleSearch
                    ? await googleSearch.FindRecipesAsync(effectiveRequest, cancellationToken)
                    : await edamam.FindRecipesAsync(effectiveRequest, cancellationToken);
            var discoveryMs = stage.ElapsedMilliseconds;

            stage.Restart();
            var safeResponse = safetyValidator.Validate(response, effectiveRequest);
            var safetyMs = stage.ElapsedMilliseconds;

            stage.Restart();
            var enrichedResponse = useAzureWebSearch
                ? await publisherPages.EnrichAsync(safeResponse, effectiveRequest, cancellationToken)
                : safeResponse;
            var enrichmentMs = stage.ElapsedMilliseconds;

            stage.Restart();
            var verifiedResponse = safetyValidator.Validate(enrichedResponse, effectiveRequest);
            var rankedResponse = RankAndLimit(verifiedResponse, effectiveRequest);
            var validateAndRankMs = stage.ElapsedMilliseconds;

            stage.Restart();
            // After ranking, so nothing is paid for a recipe the customer never sees, and before the
            // cache is stored, so a cached search is never served back without a method.
            var guidedResponse = await cookingGuides.AddCookingGuidesAsync(rankedResponse, cancellationToken);
            cache.Store(effectiveRequest, guidedResponse);
            var guidesMs = stage.ElapsedMilliseconds;

            stage.Restart();
            var result = await ApplyCommercialImagesAsync(
                guidedResponse,
                effectiveRequest.ShowPhotos,
                cancellationToken);
            var imagesMs = stage.ElapsedMilliseconds;

            logger.LogInformation(
                "SEARCH TIMING total={TotalMs}ms discovery={DiscoveryMs}ms safety={SafetyMs}ms "
                + "enrichment={EnrichmentMs}ms validateRank={ValidateRankMs}ms guides={GuidesMs}ms images={ImagesMs}ms "
                + "recipes={RecipeCount} photos={ShowPhotos}",
                total.ElapsedMilliseconds,
                discoveryMs,
                safetyMs,
                enrichmentMs,
                validateAndRankMs,
                guidesMs,
                imagesMs,
                result.Recipes.Count,
                effectiveRequest.ShowPhotos);
            return result;
        }
        catch (RecipeSafetyException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "{Provider} recipe search failed.", provider);
            throw new RecipeCatalogException(
                "Real recipe search is temporarily unavailable. No replacement recipe was invented; please try again shortly.",
                exception);
        }
    }

    private (GenerateRecipesRequest Request, IReadOnlyList<string> IgnoredIngredients)
        ApplyDietaryRestrictions(GenerateRecipesRequest request)
    {
        var allowedIngredients = new List<IngredientInput>();
        var ignoredIngredients = new List<string>();

        foreach (var ingredient in request.Ingredients.Where(item =>
                     !string.IsNullOrWhiteSpace(item.Name)))
        {
            var conflicts = FoodSafetyRules.FindConflicts(
                [ingredient.Name],
                request.Allergens,
                request.DietaryPreference,
                request.AvoidIngredients);
            if (conflicts.Count == 0)
            {
                allowedIngredients.Add(ingredient);
            }
            else
            {
                ignoredIngredients.Add(ingredient.Name.Trim());
            }
        }

        // An empty basket is only a problem when the search was supposed to have one. Inspire Me sends
        // none on purpose, so there is nothing for the restrictions to conflict with and nothing to
        // report; the preferences drive the search instead.
        if (allowedIngredients.Count == 0 && !request.DiscoverByPreference)
        {
            throw new RecipeSafetyException(
                "Every submitted ingredient conflicts with the selected dietary restrictions. Update the restrictions or add a compatible ingredient.");
        }

        var effectiveMainIngredient = request.MainIngredient.Trim();
        if (effectiveMainIngredient.Length > 0 &&
            !allowedIngredients.Any(item => normalizer.Matches(item.Name, effectiveMainIngredient)))
        {
            effectiveMainIngredient = string.Empty;
        }

        // Only the two fields this method actually changes. Everything else copies itself.
        //
        // This was written out property by property, and two were missed: CuisinePreference and
        // RecipePreferences never made it across, so AzureGroundedRecipeClient read them as empty and a
        // chosen cuisine or preference chip changed nothing about the search. Nothing failed; the
        // selection was simply discarded on the way past.
        var effectiveRequest = request with
        {
            Ingredients = allowedIngredients,
            MainIngredient = effectiveMainIngredient,
        };

        return (
            effectiveRequest,
            ignoredIngredients
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private RecipeGenerationResponse RankAndLimit(
        RecipeGenerationResponse response,
        GenerateRecipesRequest request)
    {
        var ranked = ranking
            .Rank(
                response.Recipes,
                request.Ingredients,
                request.RecentlyShownRecipeIds,
                request.OnlyUseAvailableIngredients,
                request.MainIngredient)
            .ToList();
        var recipes = ranked
            .Take(Math.Clamp(request.MaxRecipes, 3, 5))
            .ToList();

        // Ranking drops a recipe whose main ingredient it cannot find in the ingredient list, and the
        // customer simply gets three results instead of four with nothing to explain it. Named here so a
        // short batch is attributable to a concept the normalizer does not know rather than to the search.
        if (ranked.Count < response.Recipes.Count)
        {
            logger.LogInformation(
                "RANKING DROPPED kept={Kept} of={Of} main={Main} dropped={Dropped}",
                ranked.Count,
                response.Recipes.Count,
                request.MainIngredient,
                string.Join(" | ", response.Recipes
                    .Where(recipe => ranked.All(item => item.Id != recipe.Id))
                    .Select(recipe => $"{recipe.Title} [{string.Join(", ", recipe.Ingredients.Take(8).Select(ingredient => ingredient.Name))}]")));
        }

        if (request.OnlyUseAvailableIngredients && recipes.Count == 0)
        {
            return response with
            {
                Recipes = [],
                Notice = AppendNotice(
                    response.Notice,
                    "No recipes found using only what you have.")
            };
        }

        return response with { Recipes = recipes };
    }

    private async Task<RecipeGenerationResponse> ApplyCommercialImagesAsync(
        RecipeGenerationResponse response,
        bool showPhotos,
        CancellationToken cancellationToken)
    {
        var tasks = response.Recipes.Select(async recipe =>
        {
            if (!showPhotos)
            {
                return ClearImage(recipe);
            }

            var searchedImage = commercialImages.IsEnabled
                ? await commercialImages.FindAsync(recipe.Title, cancellationToken)
                : null;
            var publisherImage =
                publisherPages.CreateUnverifiedPublisherImageForPrototype(recipe) ??
                publisherPages.CreateUnverifiedPublisherImageForTesting(recipe);
            var image = searchedImage?.IsVerified == true
                ? searchedImage
                : publisherImage ?? searchedImage;
            return image is null
                ? ClearImage(recipe)
                : recipe with
                {
                    ImageUrl = image.ImageUrl,
                    ImageSourceUrl = image.SourceUrl,
                    ImageLicenseType = image.LicenseType,
                    ImageLicenseUrl = image.LicenseUrl,
                    ImageAttributionRequirements = image.AttributionRequirements,
                    ImageProvider = image.Provider,
                    ImageCreator = image.Creator,
                    ImageCommercialUseAllowed = image.CommercialUseAllowed,
                    ImageAttributionRequired = image.AttributionRequired,
                    ImageVerified = image.IsVerified,
                    ImageRightsStatus = image.IsVerified
                        ? RecipeImageRightsStatuses.VerifiedCommercial
                        : RecipeImageRightsStatuses.UnverifiedTestOnly
                };
        });

        return response with { Recipes = await Task.WhenAll(tasks) };
    }

    private static RecipeSuggestion ClearImage(RecipeSuggestion recipe) => recipe with
    {
        ImageUrl = null,
        ImageSourceUrl = null,
        ImageLicenseType = null,
        ImageLicenseUrl = null,
        ImageAttributionRequirements = null,
        ImageProvider = null,
        ImageCreator = null,
        ImageCommercialUseAllowed = false,
        ImageAttributionRequired = false,
        ImageVerified = false,
        ImageRightsStatus = RecipeImageRightsStatuses.Unavailable
    };

    private static string AppendNotice(string? current, string message) =>
        string.IsNullOrWhiteSpace(current) ? message : $"{current} {message}";

}

public sealed class RecipeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
