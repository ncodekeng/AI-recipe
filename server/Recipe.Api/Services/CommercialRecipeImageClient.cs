using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class CommercialRecipeImageClient(
    HttpClient httpClient,
    IOptions<RecipeCatalogOptions> options,
    IHostEnvironment environment,
    ILogger<CommercialRecipeImageClient> logger)
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "with", "the", "for", "from", "into", "style", "recipe"
    };

    private readonly CommercialImageOptions _options = options.Value.CommercialImages;
    private readonly bool _allowUnverifiedForTesting =
        environment.IsDevelopment() && options.Value.CommercialImages.AllowUnverifiedForTesting;

    public bool IsEnabled => _options.Enabled;

    public async Task<CommercialRecipeImage?> FindAsync(
        string dishName,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(dishName))
        {
            return null;
        }

        var query = QueryHelpers.AddQueryString("w/api.php", new Dictionary<string, string?>
        {
            ["action"] = "query",
            ["format"] = "json",
            ["formatversion"] = "2",
            ["generator"] = "search",
            ["gsrsearch"] = BuildSearchQuery(dishName),
            ["gsrnamespace"] = "6",
            ["gsrsort"] = "relevance",
            ["gsrlimit"] = Math.Clamp(_options.MaxCandidates, 1, 12).ToString(),
            ["prop"] = "imageinfo",
            ["iilimit"] = "1",
            ["iiprop"] = "url|extmetadata|mime|mediatype",
            ["iiurlwidth"] = "1200"
        });

        try
        {
            using var response = await httpClient.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Wikimedia Commons image search returned status {StatusCode} for {DishName}.",
                    (int)response.StatusCode,
                    dishName);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<CommonsResponse>(cancellationToken);
            CommercialRecipeImage? unverifiedFallback = null;
            foreach (var page in payload?.Query?.Pages ?? [])
            {
                var image = page.ImageInfo.FirstOrDefault();
                if (image is null)
                {
                    continue;
                }

                if (LooksLikeDish(page.Title, dishName) && MapVerifiedImage(image) is { } verified)
                {
                    return verified;
                }

                if (_allowUnverifiedForTesting &&
                    unverifiedFallback is null &&
                    LooksPossiblyLikeDish(page.Title, dishName))
                {
                    unverifiedFallback = MapUnverifiedTestImage(image);
                }
            }

            return unverifiedFallback;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Commercial-use image lookup failed for {DishName}.", dishName);
        }

        return null;
    }

    private static CommercialRecipeImage? MapVerifiedImage(CommonsImageInfo image)
    {
        if (!string.Equals(image.MediaType, "BITMAP", StringComparison.OrdinalIgnoreCase) ||
            image.Mime is not ("image/jpeg" or "image/png" or "image/webp") ||
            ValidImageUrl(image.ThumbUrl ?? image.Url) is not { } imageUrl ||
            ValidHttpsUrl(image.DescriptionUrl, "commons.wikimedia.org") is not { } sourceUrl ||
            Metadata(image, "LicenseShortName") is not { Length: > 0 } rawLicense ||
            NormalizeAllowedLicense(rawLicense) is not { } licenseType)
        {
            return null;
        }

        var requiresAttribution = licenseType.StartsWith("CC BY", StringComparison.OrdinalIgnoreCase);
        var licenseUrl = ValidLicenseUrl(Metadata(image, "LicenseUrl"));
        if (requiresAttribution && licenseUrl is null)
        {
            return null;
        }

        var creator = CleanText(Metadata(image, "Artist"));
        if (requiresAttribution && string.IsNullOrWhiteSpace(creator))
        {
            creator = CleanText(Metadata(image, "Credit"));
        }
        if (requiresAttribution && string.IsNullOrWhiteSpace(creator))
        {
            return null;
        }

        var attribution = requiresAttribution
            ? BuildCreativeCommonsAttribution(creator!, licenseType)
            : licenseType.Equals("CC0", StringComparison.OrdinalIgnoreCase)
                ? "No attribution is required by CC0; retain the source link for provenance."
                : "No copyright attribution is required; retain the source link for provenance and review non-copyright restrictions.";

        return new CommercialRecipeImage(
            imageUrl,
            sourceUrl,
            licenseType,
            licenseUrl,
            attribution);
    }

    private static CommercialRecipeImage? MapUnverifiedTestImage(CommonsImageInfo image)
    {
        if (!string.Equals(image.MediaType, "BITMAP", StringComparison.OrdinalIgnoreCase) ||
            image.Mime is not ("image/jpeg" or "image/png" or "image/webp") ||
            ValidImageUrl(image.ThumbUrl ?? image.Url) is not { } imageUrl ||
            ValidHttpsUrl(image.DescriptionUrl, "commons.wikimedia.org") is not { } sourceUrl)
        {
            return null;
        }

        return new CommercialRecipeImage(
            imageUrl,
            sourceUrl,
            "Unverified test image",
            null,
            "Testing only — image rights were not verified. Do not use this image in a public or commercial release.",
            IsVerified: false);
    }

    private static string BuildCreativeCommonsAttribution(string creator, string licenseType)
    {
        var shareAlike = licenseType.StartsWith("CC BY-SA", StringComparison.OrdinalIgnoreCase)
            ? " License adaptations under the same terms."
            : string.Empty;
        return $"Credit {Truncate(creator, 180)}; link to the source and {licenseType}; indicate changes.{shareAlike}";
    }

    private static string? NormalizeAllowedLicense(string value)
    {
        var cleaned = CleanText(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var comparable = Regex.Replace(cleaned.ToUpperInvariant(), "[-_\\s]+", " ").Trim();
        if (comparable.Contains(" NC ", StringComparison.Ordinal) ||
            comparable.Contains(" ND ", StringComparison.Ordinal) ||
            comparable.EndsWith(" NC", StringComparison.Ordinal) ||
            comparable.EndsWith(" ND", StringComparison.Ordinal))
        {
            return null;
        }

        if (comparable == "CC0" || comparable.StartsWith("CC0 ", StringComparison.Ordinal))
        {
            return cleaned;
        }
        if (comparable == "PUBLIC DOMAIN" || comparable.StartsWith("PUBLIC DOMAIN ", StringComparison.Ordinal) ||
            comparable == "PDM" || comparable.StartsWith("PD ", StringComparison.Ordinal))
        {
            return cleaned;
        }
        if (comparable == "CC BY" || comparable.StartsWith("CC BY ", StringComparison.Ordinal))
        {
            return cleaned;
        }

        return null;
    }

    private static bool LooksLikeDish(string pageTitle, string dishName)
    {
        var dishWords = Words(dishName);
        if (dishWords.Count == 0)
        {
            return false;
        }

        var pageWords = Words(pageTitle);
        var requiredMatches = dishWords.Count <= 4
            ? dishWords.Count
            : (int)Math.Ceiling(dishWords.Count * 0.75);
        return dishWords.Count(word => pageWords.Contains(word)) >= requiredMatches;
    }

    private static bool LooksPossiblyLikeDish(string pageTitle, string dishName)
    {
        var dishWords = Words(dishName);
        var pageWords = Words(pageTitle);
        var requiredMatches = Math.Min(2, dishWords.Count);
        return requiredMatches > 0 && dishWords.Count(word => pageWords.Contains(word)) >= requiredMatches;
    }

    private static string BuildSearchQuery(string dishName)
    {
        var words = WordList(dishName);
        if (words.Count == 0)
        {
            return "food dish filetype:bitmap";
        }
        if (words.Count == 1)
        {
            return $"{words[0]} food filetype:bitmap";
        }

        var phrase = string.Join(' ', words.Take(2));
        var remaining = string.Join(' ', words.Skip(2).Take(4));
        return $"\"{phrase}\" {remaining} filetype:bitmap".Trim();
    }

    private static List<string> WordList(string value) => Regex
        .Matches(value, "[\\p{L}\\p{N}]+")
        .Select(match => match.Value.ToLowerInvariant())
        .Where(word => word.Length >= 3 && !StopWords.Contains(word))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static HashSet<string> Words(string value) =>
        WordList(value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? Metadata(CommonsImageInfo image, string key) =>
        image.ExtMetadata.FirstOrDefault(item =>
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.Value;

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var withoutMarkup = Regex.Replace(value, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutMarkup);
        return Truncate(Regex.Replace(decoded, "\\s+", " ").Trim(), 300);
    }

    private static string? ValidHttpsUrl(string? value, string requiredHost) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.DnsSafeHost.Equals(requiredHost, StringComparison.OrdinalIgnoreCase)
            ? uri.ToString()
            : null;

    private static string? ValidImageUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.DnsSafeHost.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
         uri.DnsSafeHost.Equals("thumb.wikimedia.org", StringComparison.OrdinalIgnoreCase))
            ? uri.ToString()
            : null;

    private static string? ValidLicenseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.DnsSafeHost.Equals("creativecommons.org", StringComparison.OrdinalIgnoreCase)
            ? uri.ToString()
            : null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class CommonsResponse
    {
        [JsonPropertyName("query")]
        public CommonsQuery? Query { get; init; }
    }

    private sealed class CommonsQuery
    {
        [JsonPropertyName("pages")]
        public List<CommonsPage> Pages { get; init; } = [];
    }

    private sealed class CommonsPage
    {
        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("imageinfo")]
        public List<CommonsImageInfo> ImageInfo { get; init; } = [];
    }

    private sealed class CommonsImageInfo
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("thumburl")]
        public string? ThumbUrl { get; init; }

        [JsonPropertyName("descriptionurl")]
        public string? DescriptionUrl { get; init; }

        [JsonPropertyName("mime")]
        public string? Mime { get; init; }

        [JsonPropertyName("mediatype")]
        public string? MediaType { get; init; }

        [JsonPropertyName("extmetadata")]
        public Dictionary<string, CommonsMetadataValue> ExtMetadata { get; init; } = [];
    }

    private sealed class CommonsMetadataValue
    {
        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }
}

public sealed record CommercialRecipeImage(
    string ImageUrl,
    string SourceUrl,
    string LicenseType,
    string? LicenseUrl,
    string AttributionRequirements,
    bool IsVerified = true);
