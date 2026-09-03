using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipeSearchCache
{
    private readonly IMemoryCache _cache;
    private readonly IngredientNormalizer _normalizer;
    private readonly RecipeCacheOptions _options;
    private readonly string _provider;
    private readonly IAiPromptProvider _prompts;
    private readonly ILogger<RecipeSearchCache> _logger;

    public RecipeSearchCache(
        IMemoryCache cache,
        IngredientNormalizer normalizer,
        IOptions<RecipeCatalogOptions> options,
        IAiPromptProvider prompts,
        ILogger<RecipeSearchCache> logger)
    {
        _cache = cache;
        _normalizer = normalizer;
        _options = options.Value.Cache;
        _provider = options.Value.Provider.Trim().ToLowerInvariant();
        _prompts = prompts;
        _logger = logger;

        if (_options.Enabled && !_options.ProviderPermissionConfirmed)
        {
            _logger.LogWarning(
                "Recipe caching was requested but is disabled because provider caching permission was not confirmed.");
        }
    }

    public bool TryGet(
        GenerateRecipesRequest request,
        out RecipeGenerationResponse? response)
    {
        response = null;
        if (!_options.CanStoreProviderData)
        {
            return false;
        }

        var hit = _cache.TryGetValue(BuildKey(request), out response);
        if (hit)
        {
            _logger.LogInformation("Using a cached sourced-recipe result.");
        }

        return hit;
    }

    public void Store(
        GenerateRecipesRequest request,
        RecipeGenerationResponse response)
    {
        if (!_options.CanStoreProviderData)
        {
            return;
        }

        var duration = TimeSpan.FromHours(Math.Clamp(_options.DurationHours, 1, 168));
        _cache.Set(
            BuildKey(request),
            response,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration,
                Size = 1
            });
    }

    private string BuildKey(GenerateRecipesRequest request)
    {
        var ingredients = request.Ingredients
            .Select(item => _normalizer.Normalize(item.Name))
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var allergens = NormalizeTextSet(request.Allergens);
        var avoidIngredients = request.AvoidIngredients
            .Select(_normalizer.Normalize)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var recentlyShownRecipeIds = request.RecentlyShownRecipeIds
            .Distinct()
            .Order()
            .ToArray();
        var cacheIdentity = JsonSerializer.Serialize(new
        {
            Version = 7,
            Provider = _provider,
            PromptRevision = _prompts.Current.Revision,
            Ingredients = ingredients,
            Allergens = allergens,
            AvoidIngredients = avoidIngredients,
            RecentlyShownRecipeIds = recentlyShownRecipeIds,
            DietaryPreference = request.DietaryPreference.Trim().ToLowerInvariant(),
            request.MaxCookingMinutes,
            request.Servings,
            request.ShowPhotos
        });
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(cacheIdentity));
        return $"recipes:{Convert.ToHexString(digest)}";
    }

    private static string[] NormalizeTextSet(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim().ToLowerInvariant())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
