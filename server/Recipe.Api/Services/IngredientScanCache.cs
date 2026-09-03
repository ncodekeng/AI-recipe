using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class IngredientScanCache(
    IMemoryCache cache,
    IOptions<FoodAiOptions> options,
    IAiPromptProvider prompts,
    ILogger<IngredientScanCache> logger)
{
    private readonly IngredientScanCacheOptions _options = options.Value.ScanCache;
    private readonly string _providerIdentity = string.Join(
        '|',
        options.Value.Provider.Trim().ToLowerInvariant(),
        options.Value.AzureOpenAI.Deployment.Trim().ToLowerInvariant());

    public bool TryGet(
        string clientKey,
        IReadOnlyList<UploadedPhoto> photos,
        out IngredientAnalysisResponse? response)
    {
        response = null;
        if (!_options.Enabled)
        {
            return false;
        }

        var hit = cache.TryGetValue(BuildKey(clientKey, photos), out response);
        if (hit)
        {
            logger.LogInformation("Using a cached ingredient scan result.");
        }

        return hit;
    }

    public void Store(
        string clientKey,
        IReadOnlyList<UploadedPhoto> photos,
        IngredientAnalysisResponse response)
    {
        if (!_options.Enabled ||
            !response.Provider.Equals("Azure OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        cache.Set(
            BuildKey(clientKey, photos),
            response,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Clamp(_options.DurationHours, 1, 168)),
                Size = 1
            });
    }

    private string BuildKey(string clientKey, IReadOnlyList<UploadedPhoto> photos)
    {
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(incrementalHash, "plate-scan-v1");
        Append(incrementalHash, clientKey);
        Append(incrementalHash, _providerIdentity);
        Append(incrementalHash, prompts.Current.Revision);
        foreach (var photo in photos)
        {
            Append(incrementalHash, photo.FileName.Trim().ToLowerInvariant());
            Append(incrementalHash, photo.ContentType.ToLowerInvariant());
            incrementalHash.AppendData(SHA256.HashData(photo.Content));
        }

        return $"ingredient-scan:{Convert.ToHexString(incrementalHash.GetHashAndReset())}";
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
