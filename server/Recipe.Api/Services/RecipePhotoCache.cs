using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class RecipePhotoCache(
    IMemoryCache cache,
    IOptions<RecipeCatalogOptions> options,
    ILogger<RecipePhotoCache> logger)
{
    private readonly CommercialImageOptions _options = options.Value.CommercialImages;

    public bool TryGet(string dishName, out CommercialRecipeImage? image)
    {
        image = null;
        if (!_options.CacheEnabled)
        {
            return false;
        }

        if (!cache.TryGetValue(BuildKey(dishName), out CachedPhoto? cached) || cached is null)
        {
            return false;
        }

        image = cached.Image;
        logger.LogInformation("Using cached commercial-image metadata for {DishName}.", dishName);
        return true;
    }

    public void Store(string dishName, CommercialRecipeImage? image)
    {
        if (!_options.CacheEnabled)
        {
            return;
        }

        cache.Set(
            BuildKey(dishName),
            new CachedPhoto(image),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(
                    Math.Clamp(_options.CacheDurationHours, 1, 168)),
                Size = 1
            });
    }

    private string BuildKey(string dishName)
    {
        var identity = JsonSerializer.Serialize(new
        {
            Version = 1,
            DishName = Regex.Replace(
                dishName.Trim().ToLowerInvariant(),
                "[^a-z0-9]+",
                " ").Trim(),
            AllowedProviders = (_options.AllowedProviders ?? [])
                .Select(item => item.Trim().ToLowerInvariant())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
            _options.AllowUnverifiedForTesting
        });
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"recipe-photo:{Convert.ToHexString(digest)}";
    }

    private sealed record CachedPhoto(CommercialRecipeImage? Image);
}
