using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class PublisherRecipePageClient
{
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64
    };

    private static readonly Regex IngredientPrefix = new(
        "^(?:about\\s+|approximately\\s+)?(?:\\d+(?:[./]\\d+)?|[\\u00BC\\u00BD\\u00BE\\u2153\\u2154\\u215B\\u215C\\u215D\\u215E])(?:\\s*(?:-|\\u2013|to)\\s*(?:\\d+(?:[./]\\d+)?|[\\u00BC\\u00BD\\u00BE\\u2153\\u2154\\u215B\\u215C\\u215D\\u215E]))?\\s*(?:\\([^)]{1,40}\\)\\s*)?(?:(?:cups?|tablespoons?|tbsp|teaspoons?|tsp|grams?|g|kilograms?|kg|millilitres?|milliliters?|ml|litres?|liters?|l|ounces?|oz|pounds?|lb|cloves?|cans?|tins?|packs?|packages?|slices?|pieces?|pinches?|bunches?)\\b[.,]?\\s*)?(?:of\\s+)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex FirstWholeNumber = new(
        "\\b(?<number>\\d{1,5})\\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex WordPattern = new(
        "[\\p{L}\\p{N}]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MicrodataRecipeTypePattern = new(
        "\\bitemtype\\s*=\\s*(?:\"[^\"]*https?://schema\\.org/Recipe/?[^\"]*\"|'[^']*https?://schema\\.org/Recipe/?[^']*')",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MicrodataContentElementPattern = new(
        "<(?<tag>h1|h2|li|span|time|p)\\b(?<attributes>[^>]{0,4096})>(?<content>.*?)</\\k<tag>\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex MicrodataVoidElementPattern = new(
        "<(?<tag>meta|img|link|source)\\b(?<attributes>[^>]{0,4096})/?>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex HtmlAttributePattern = new(
        "(?<name>[A-Za-z_:][A-Za-z0-9_:.-]*)\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<unquoted>[^\\s>]+))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex HtmlTagPattern = new(
        "<[^>]{1,4096}>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex HoursPattern = new(
        "(?<number>\\d{1,2})\\s*(?:hours?|hrs?|hr|h)\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MinutesPattern = new(
        "(?<number>\\d{1,3})\\s*(?:minutes?|mins?|min|m)\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> TitleStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "with", "the", "for", "from", "recipe", "easy", "best", "classic",
        "homemade", "simple", "quick", "how", "make"
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly PublisherExtractionOptions _options;
    private readonly IngredientNormalizer _normalizer;
    private readonly ILogger<PublisherRecipePageClient> _logger;
    private readonly SemaphoreSlim _requestGate;
    private readonly bool _cacheEnabled;
    private readonly bool _allowUnverifiedPublisherImagesForTesting;

    public PublisherRecipePageClient(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<RecipeCatalogOptions> options,
        IHostEnvironment environment,
        IngredientNormalizer normalizer,
        ILogger<PublisherRecipePageClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value.PublisherExtraction;
        _normalizer = normalizer;
        _logger = logger;
        _requestGate = new SemaphoreSlim(Math.Clamp(_options.MaxConcurrentRequests, 1, 5));
        _cacheEnabled = _options.CacheEnabled && options.Value.Cache.ProviderPermissionConfirmed;
        _allowUnverifiedPublisherImagesForTesting =
            environment.IsDevelopment() &&
            options.Value.CommercialImages.AllowUnverifiedForTesting;

        if (_options.CacheEnabled && !_cacheEnabled)
        {
            _logger.LogWarning(
                "Publisher-page caching was requested but is disabled because provider caching permission was not confirmed.");
        }
    }

    public bool IsEnabled => _options.Enabled;

    public async Task<RecipeGenerationResponse> EnrichAsync(
        RecipeGenerationResponse response,
        GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || response.Recipes.Count == 0)
        {
            return response;
        }

        var recipes = await Task.WhenAll(response.Recipes.Select(recipe =>
            EnrichRecipeAsync(recipe, request, cancellationToken)));
        var verifiedCount = recipes.Count(recipe => recipe.PublisherPageVerified);
        if (verifiedCount == 0)
        {
            return response;
        }

        var verificationNotice = verifiedCount == 1
            ? "Verified structured recipe data on one publisher page."
            : $"Verified structured recipe data on {verifiedCount} publisher pages.";
        return response with
        {
            Recipes = recipes,
            Notice = AppendNotice(response.Notice, verificationNotice)
        };
    }

    public async Task<CommercialRecipeImage?> FindUnverifiedPublisherImageForTestingAsync(
        string? sourceUrl,
        string dishName,
        CancellationToken cancellationToken)
    {
        if (!_allowUnverifiedPublisherImagesForTesting ||
            !TryCreateSafeSourceUri(sourceUrl, out var sourceUri))
        {
            return null;
        }

        var page = await FindRecipeAsync(sourceUri, dishName, [], cancellationToken);
        return CreateUnverifiedPublisherImage(page?.ImageUrl, sourceUri);
    }

    public CommercialRecipeImage? CreateUnverifiedPublisherImageForTesting(RecipeSuggestion recipe)
    {
        if (!_allowUnverifiedPublisherImagesForTesting ||
            !recipe.PublisherPageVerified ||
            !TryCreateSafeSourceUri(recipe.SourceUrl, out var sourceUri))
        {
            return null;
        }

        return CreateUnverifiedPublisherImage(recipe.PublisherImageCandidateUrl, sourceUri);
    }

    private async Task<RecipeSuggestion> EnrichRecipeAsync(
        RecipeSuggestion recipe,
        GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        if (!recipe.SourceVerified ||
            !TryCreateSafeSourceUri(recipe.SourceUrl, out var sourceUri))
        {
            return recipe;
        }

        var page = await FindRecipeAsync(
            sourceUri,
            recipe.Title,
            recipe.Ingredients,
            cancellationToken);
        if (page is null)
        {
            return recipe;
        }

        var ingredients = MapIngredients(page.IngredientLines, recipe.Ingredients);
        if (ingredients.Count == 0 ||
            (!string.IsNullOrWhiteSpace(request.MainIngredient) &&
             !ingredients.Any(item => _normalizer.Matches(request.MainIngredient, item.Name))))
        {
            return recipe;
        }

        var prepMinutes = page.PrepMinutes ?? recipe.PrepMinutes;
        var cookMinutes = page.CookMinutes ?? recipe.CookMinutes;
        var totalMinutes = page.TotalMinutes ??
                           (prepMinutes is not null && cookMinutes is not null
                               ? prepMinutes + cookMinutes
                               : recipe.CookingMinutes);

        return recipe with
        {
            Title = Truncate(page.Title, 160),
            Description = $"Structured recipe data verified on {sourceUri.DnsSafeHost.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)}.",
            Ingredients = ingredients,
            CookingMinutes = totalMinutes is >= 0 and <= 1_440
                ? totalMinutes.Value
                : recipe.CookingMinutes,
            PrepMinutes = prepMinutes is >= 0 and <= 1_440 ? prepMinutes : recipe.PrepMinutes,
            CookMinutes = cookMinutes is >= 0 and <= 1_440 ? cookMinutes : recipe.CookMinutes,
            Servings = page.Servings is >= 1 and <= 12 ? page.Servings.Value : recipe.Servings,
            CaloriesPerServing = page.CaloriesPerServing is >= 1 and <= 5_000
                ? page.CaloriesPerServing
                : recipe.CaloriesPerServing,
            SourceTitle = Truncate(page.Title, 180),
            PublisherPageVerified = true,
            PublisherImageCandidateUrl = page.ImageUrl
        };
    }

    private async Task<PublisherRecipeData?> FindRecipeAsync(
        Uri sourceUri,
        string expectedTitle,
        IReadOnlyList<RecipeIngredient> expectedIngredients,
        CancellationToken cancellationToken)
    {
        var recipes = await GetPageRecipesAsync(sourceUri, cancellationToken);
        return recipes
            .Select(recipe => new
            {
                Recipe = recipe,
                Score = CalculateRecipeScore(recipe, expectedTitle, expectedIngredients)
            })
            .Where(item => item.Score >= 0)
            .OrderByDescending(item => item.Score)
            .Select(item => item.Recipe)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<PublisherRecipeData>> GetPageRecipesAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(sourceUri);
        if (_cacheEnabled &&
            _cache.TryGetValue(cacheKey, out CachedPublisherPage? cached) &&
            cached is not null)
        {
            return cached.Recipes;
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            if (_cacheEnabled &&
                _cache.TryGetValue(cacheKey, out cached) &&
                cached is not null)
            {
                return cached.Recipes;
            }

            var fetch = await FetchAsync(sourceUri, cancellationToken);
            if (fetch.Cacheable && _cacheEnabled)
            {
                _cache.Set(
                    cacheKey,
                    new CachedPublisherPage(fetch.Recipes),
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(
                            Math.Clamp(_options.CacheDurationHours, 1, 168)),
                        Size = 1
                    });
            }

            return fetch.Recipes;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<PublisherFetchResult> FetchAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/ld+json,application/json;q=0.9");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PublisherFetchResult(
                    response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone,
                    []);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!IsSupportedMediaType(mediaType))
            {
                return new PublisherFetchResult(true, []);
            }

            var bytes = await ReadBoundedAsync(
                response.Content,
                Math.Clamp(_options.MaxResponseBytes, 64 * 1024, 4 * 1024 * 1024),
                cancellationToken);
            if (bytes is null)
            {
                _logger.LogWarning("Publisher recipe page exceeded the configured response limit: {SourceUrl}", sourceUri);
                return new PublisherFetchResult(false, []);
            }

            var text = Decode(bytes, response.Content.Headers.ContentType?.CharSet);
            var recipes = IsJsonMediaType(mediaType)
                ? ParseJsonDocument(text, sourceUri)
                : ParseHtml(text, sourceUri);
            return new PublisherFetchResult(true, recipes);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Publisher recipe page timed out: {SourceUrl}", sourceUri);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Publisher recipe page could not be read: {SourceUrl}", sourceUri);
        }

        return new PublisherFetchResult(false, []);
    }

    private static IReadOnlyList<PublisherRecipeData> ParseHtml(string html, Uri sourceUri)
    {
        var recipes = new List<PublisherRecipeData>();
        var cursor = 0;
        for (var blockCount = 0; blockCount < 50; blockCount++)
        {
            var scriptStart = html.IndexOf("<script", cursor, StringComparison.OrdinalIgnoreCase);
            if (scriptStart < 0)
            {
                break;
            }

            var tagEnd = html.IndexOf('>', scriptStart + 7);
            if (tagEnd < 0)
            {
                break;
            }

            var scriptEnd = html.IndexOf("</script", tagEnd + 1, StringComparison.OrdinalIgnoreCase);
            if (scriptEnd < 0)
            {
                break;
            }

            var tag = html.AsSpan(scriptStart, tagEnd - scriptStart + 1);
            if (tag.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase))
            {
                var block = html[(tagEnd + 1)..scriptEnd].Trim();
                recipes.AddRange(ParseJsonDocument(block, sourceUri));
            }

            cursor = scriptEnd + 8;
        }

        var microdataRecipe = ParseMicrodataRecipe(html, sourceUri);
        if (microdataRecipe is not null)
        {
            var matchingIndex = recipes.FindIndex(recipe =>
                recipe.Title.Equals(microdataRecipe.Title, StringComparison.OrdinalIgnoreCase));
            if (matchingIndex >= 0)
            {
                var existing = recipes[matchingIndex];
                recipes[matchingIndex] = existing with
                {
                    PrepMinutes = existing.PrepMinutes ?? microdataRecipe.PrepMinutes,
                    CookMinutes = existing.CookMinutes ?? microdataRecipe.CookMinutes,
                    TotalMinutes = existing.TotalMinutes ?? microdataRecipe.TotalMinutes,
                    Servings = existing.Servings ?? microdataRecipe.Servings,
                    CaloriesPerServing = existing.CaloriesPerServing ?? microdataRecipe.CaloriesPerServing,
                    ImageUrl = existing.ImageUrl ?? microdataRecipe.ImageUrl
                };
            }
            else
            {
                recipes.Add(microdataRecipe);
            }
        }

        return recipes
            .GroupBy(recipe => $"{recipe.Title}\n{string.Join('|', recipe.IngredientLines)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(12)
            .ToList();
    }

    private static PublisherRecipeData? ParseMicrodataRecipe(string html, Uri sourceUri)
    {
        if (!MicrodataRecipeTypePattern.IsMatch(html))
        {
            return null;
        }

        var elements = MicrodataContentElementPattern.Matches(html)
            .Cast<Match>()
            .Select(match => new MicrodataElement(
                match.Groups["tag"].Value,
                match.Groups["attributes"].Value,
                CleanHtmlText(match.Groups["content"].Value)))
            .Where(element => element.Text.Length > 0)
            .ToList();
        elements.AddRange(MicrodataVoidElementPattern.Matches(html)
            .Cast<Match>()
            .Select(match => new MicrodataElement(
                match.Groups["tag"].Value,
                match.Groups["attributes"].Value,
                CleanText(ReadHtmlAttribute(match.Groups["attributes"].Value, "content") ?? string.Empty)))
            .Where(element => element.Text.Length > 0));

        var title = elements
            .Where(element => element.Tag.Equals("h1", StringComparison.OrdinalIgnoreCase) &&
                              HasItemProperty(element.Attributes, "name"))
            .Select(element => element.Text)
            .FirstOrDefault();
        var ingredients = elements
            .Where(element => HasItemProperty(element.Attributes, "recipeIngredient", "ingredients"))
            .Select(element => element.Text)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        if (string.IsNullOrWhiteSpace(title) || ingredients.Count == 0)
        {
            return null;
        }

        return new PublisherRecipeData(
            Truncate(CleanText(title), 160),
            ingredients,
            ReadMicrodataMinutes(elements, "prepTime"),
            ReadMicrodataMinutes(elements, "cookTime"),
            ReadMicrodataMinutes(elements, "totalTime"),
            ReadMicrodataWholeNumber(elements, 12, "recipeYield"),
            ReadMicrodataWholeNumber(elements, 5_000, "calories"),
            ReadMicrodataImageUrl(html, sourceUri));
    }

    private static int? ReadMicrodataMinutes(
        IReadOnlyList<MicrodataElement> elements,
        string propertyName)
    {
        var value = elements
            .Where(element => HasItemProperty(element.Attributes, propertyName))
            .Select(element => element.Text)
            .FirstOrDefault();
        return ParseDurationMinutes(value);
    }

    private static int? ReadMicrodataWholeNumber(
        IReadOnlyList<MicrodataElement> elements,
        int maximum,
        string propertyName)
    {
        var value = elements
            .Where(element => HasItemProperty(element.Attributes, propertyName))
            .Select(element => element.Text)
            .FirstOrDefault();
        var match = FirstWholeNumber.Match(value ?? string.Empty);
        return match.Success &&
               int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
               number >= 1 && number <= maximum
            ? number
            : null;
    }

    private static int? ParseDurationMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var isoMinutes = (int)Math.Round(XmlConvert.ToTimeSpan(value).TotalMinutes);
            if (isoMinutes is >= 0 and <= 1_440)
            {
                return isoMinutes;
            }
        }
        catch (FormatException)
        {
            // Older microdata publishers often use readable values such as "70 mins".
        }

        var hoursMatch = HoursPattern.Match(value);
        var minutesMatch = MinutesPattern.Match(value);
        var hours = hoursMatch.Success &&
                    int.TryParse(hoursMatch.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedHours)
            ? parsedHours
            : 0;
        var minutes = minutesMatch.Success &&
                      int.TryParse(minutesMatch.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMinutes)
            ? parsedMinutes
            : 0;
        var total = hours * 60 + minutes;
        return total is >= 1 and <= 1_440 ? total : null;
    }

    private static string? ReadMicrodataImageUrl(string html, Uri sourceUri)
    {
        foreach (Match match in MicrodataVoidElementPattern.Matches(html))
        {
            var attributes = match.Groups["attributes"].Value;
            var property = ReadHtmlAttribute(attributes, "property")?.Trim();
            if (!HasItemProperty(attributes, "image") &&
                !string.Equals(property, "og:image", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property, "twitter:image", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var attributeName in new[] { "content", "src", "href" })
            {
                var candidate = ReadHtmlAttribute(attributes, attributeName);
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    Uri.TryCreate(sourceUri, WebUtility.HtmlDecode(candidate), out var imageUri) &&
                    SafePublisherHttpMessageHandler.IsSafeHttpsUri(imageUri))
                {
                    return imageUri.ToString();
                }
            }
        }

        return null;
    }

    private static bool HasItemProperty(string attributes, params string[] propertyNames)
    {
        var value = ReadHtmlAttribute(attributes, "itemprop");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(token => propertyNames.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    private static string? ReadHtmlAttribute(string attributes, string attributeName)
    {
        foreach (Match match in HtmlAttributePattern.Matches(attributes))
        {
            if (!match.Groups["name"].Value.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var groupName in new[] { "double", "single", "unquoted" })
            {
                if (match.Groups[groupName].Success)
                {
                    return match.Groups[groupName].Value;
                }
            }
        }

        return null;
    }

    private static string CleanHtmlText(string value) =>
        CleanText(HtmlTagPattern.Replace(value, " "));

    private static IReadOnlyList<PublisherRecipeData> ParseJsonDocument(string value, Uri sourceUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var candidates = new[]
        {
            TrimScriptWrapper(value),
            WebUtility.HtmlDecode(TrimScriptWrapper(value))
        }.Distinct(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate, JsonDocumentOptions);
                return FindRecipeObjects(document.RootElement)
                    .Select(element => MapRecipeData(element, sourceUri))
                    .Where(recipe => recipe is not null)
                    .Cast<PublisherRecipeData>()
                    .Take(12)
                    .ToList();
            }
            catch (JsonException)
            {
                // Try the decoded representation before treating this block as unreadable.
            }
        }

        return [];
    }

    private static IEnumerable<JsonElement> FindRecipeObjects(JsonElement root)
    {
        var pending = new Stack<(JsonElement Element, int Depth)>();
        pending.Push((root, 0));
        var inspected = 0;
        while (pending.Count > 0 && inspected++ < 10_000)
        {
            var (element, depth) = pending.Pop();
            if (depth > 20)
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (IsRecipeType(element))
                {
                    yield return element;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        pending.Push((property.Value, depth + 1));
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        pending.Push((item, depth + 1));
                    }
                }
            }
        }
    }

    private static PublisherRecipeData? MapRecipeData(JsonElement recipe, Uri sourceUri)
    {
        var title = ReadString(recipe, "name");
        var ingredients = ReadStringArray(recipe, "recipeIngredient")
            .Select(CleanText)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        if (string.IsNullOrWhiteSpace(title) || ingredients.Count == 0)
        {
            return null;
        }

        return new PublisherRecipeData(
            CleanText(title),
            ingredients,
            ReadDurationMinutes(recipe, "prepTime"),
            ReadDurationMinutes(recipe, "cookTime"),
            ReadDurationMinutes(recipe, "totalTime"),
            ReadServings(recipe),
            ReadCalories(recipe),
            ReadImageUrl(recipe, sourceUri));
    }

    private IReadOnlyList<RecipeIngredient> MapIngredients(
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<RecipeIngredient> modelIngredients)
    {
        var mapped = new List<RecipeIngredient>();
        foreach (var line in sourceLines.Take(50))
        {
            var original = Truncate(CleanText(line), 180);
            if (original.Length == 0)
            {
                continue;
            }

            var modelMatch = modelIngredients
                .OrderByDescending(item => item.Name.Length)
                .FirstOrDefault(item => _normalizer.Matches(item.Name, original));
            var prefix = IngredientPrefix.Match(original);
            var exactAmount = prefix.Success && prefix.Length > 0
                ? prefix.Value.Trim(' ', ',', '.')
                : null;
            var name = modelMatch?.Name ?? GuessIngredientName(original, prefix);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = original;
            }

            mapped.Add(new RecipeIngredient(
                string.IsNullOrWhiteSpace(exactAmount)
                    ? modelMatch?.Amount ?? "As listed by the publisher"
                    : exactAmount,
                Truncate(name.Trim(), 120),
                OriginalText: original));
        }

        return mapped
            .GroupBy(item => item.OriginalText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private int CalculateRecipeScore(
        PublisherRecipeData recipe,
        string expectedTitle,
        IReadOnlyList<RecipeIngredient> expectedIngredients)
    {
        var expectedWords = MeaningfulWords(expectedTitle);
        var sourceWords = MeaningfulWords(recipe.Title);
        var titleMatches = expectedWords.Count(word => sourceWords.Contains(word));
        var titleThreshold = expectedWords.Count switch
        {
            0 => int.MaxValue,
            <= 2 => expectedWords.Count,
            <= 5 => (int)Math.Ceiling(expectedWords.Count * 0.5),
            _ => (int)Math.Ceiling(expectedWords.Count * 0.4)
        };
        var ingredientMatches = expectedIngredients.Count(expected =>
            recipe.IngredientLines.Any(line => _normalizer.Matches(expected.Name, line)));
        var ingredientThreshold = Math.Min(2, expectedIngredients.Count);
        if (titleMatches < titleThreshold &&
            (ingredientThreshold == 0 || ingredientMatches < ingredientThreshold))
        {
            return -1;
        }

        return titleMatches * 100 + ingredientMatches * 10 + recipe.IngredientLines.Count;
    }

    private static CommercialRecipeImage? CreateUnverifiedPublisherImage(
        string? imageUrl,
        Uri sourceUri)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) ||
            imageUrl.Length > 2_048 ||
            !Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri) ||
            !SafePublisherHttpMessageHandler.IsSafeHttpsUri(imageUri))
        {
            return null;
        }

        return new CommercialRecipeImage(
            imageUri.ToString(),
            sourceUri.ToString(),
            "Unverified publisher image",
            null,
            "Testing only -- the publisher image is real, but reuse rights were not verified. Do not use it in a public or commercial release.",
            IsVerified: false,
            Provider: "Recipe publisher",
            CommercialUseAllowed: false,
            AttributionRequired: false);
    }

    private static string GuessIngredientName(string original, Match prefix)
    {
        var remainder = prefix.Success ? original[prefix.Length..] : original;
        var comma = remainder.IndexOf(',');
        if (comma > 0)
        {
            remainder = remainder[..comma];
        }

        return CleanText(remainder).Trim(' ', '-', '\u2013', ':', ';', '.');
    }

    private static int? ReadDurationMinutes(JsonElement recipe, string propertyName)
    {
        return ParseDurationMinutes(ReadString(recipe, propertyName));
    }

    private static int? ReadServings(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeYield", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            value = value.EnumerateArray().FirstOrDefault();
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            return numeric is >= 1 and <= 12 ? numeric : null;
        }

        var match = FirstWholeNumber.Match(value.ToString());
        return match.Success &&
               int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out numeric) &&
               numeric is >= 1 and <= 12
            ? numeric
            : null;
    }

    private static int? ReadCalories(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("nutrition", out var nutrition) ||
            nutrition.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var value = ReadString(nutrition, "calories");
        var match = FirstWholeNumber.Match(value ?? string.Empty);
        return match.Success &&
               int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var calories) &&
               calories is >= 1 and <= 5_000
            ? calories
            : null;
    }

    private static string? ReadImageUrl(JsonElement recipe, Uri sourceUri)
    {
        if (!recipe.TryGetProperty("image", out var image))
        {
            return null;
        }

        var candidate = ReadUrlValue(image);
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Uri.TryCreate(sourceUri, candidate, out var imageUri) ||
            !SafePublisherHttpMessageHandler.IsSafeHttpsUri(imageUri))
        {
            return null;
        }

        return imageUri.ToString();
    }

    private static string? ReadUrlValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (ReadUrlValue(item) is { Length: > 0 } candidate)
                {
                    return candidate;
                }
            }
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "url", "contentUrl" })
            {
                if (value.TryGetProperty(propertyName, out var property) &&
                    ReadUrlValue(property) is { Length: > 0 } candidate)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return [property.GetString() ?? string.Empty];
        }
        if (property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToList();
    }

    private static bool IsRecipeType(JsonElement value)
    {
        if (!value.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString()?.Equals("Recipe", StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Array => type.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                item.GetString()?.Equals("Recipe", StringComparison.OrdinalIgnoreCase) == true),
            _ => false
        };
    }

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static HashSet<string> MeaningfulWords(string value) => WordPattern
        .Matches(value.ToLowerInvariant())
        .Select(match => match.Value)
        .Where(word => word.Length >= 3 && !TitleStopWords.Contains(word))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string TrimScriptWrapper(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
        }
        if (trimmed.EndsWith("-->", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3];
        }
        if (trimmed.StartsWith("<![CDATA[", StringComparison.Ordinal))
        {
            trimmed = trimmed[9..];
        }
        if (trimmed.EndsWith("]]>", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3];
        }
        return trimmed.Trim().TrimEnd(';');
    }

    private static bool TryCreateSafeSourceUri(string? value, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Length <= 2_048 &&
            Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            SafePublisherHttpMessageHandler.IsSafeHttpsUri(candidate))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsSupportedMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType) ||
        mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase) ||
        IsJsonMediaType(mediaType);

    private static bool IsJsonMediaType(string? mediaType) =>
        mediaType?.Equals("application/ld+json", StringComparison.OrdinalIgnoreCase) == true ||
        mediaType?.Equals("application/json", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }
            if (output.Length + read > maxBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        try
        {
            return string.IsNullOrWhiteSpace(charset)
                ? Encoding.UTF8.GetString(bytes)
                : Encoding.GetEncoding(charset.Trim('"', '\'')).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static string CleanText(string value)
    {
        var decoded = WebUtility.HtmlDecode(value);
        return Regex.Replace(decoded, "\\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
    }

    private static string BuildCacheKey(Uri sourceUri)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUri.AbsoluteUri));
        return $"publisher-recipe:v1:{Convert.ToHexString(hash)}";
    }

    private static string AppendNotice(string? current, string message) =>
        string.IsNullOrWhiteSpace(current) ? message : $"{current} {message}";

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record MicrodataElement(
        string Tag,
        string Attributes,
        string Text);

    private sealed record PublisherRecipeData(
        string Title,
        IReadOnlyList<string> IngredientLines,
        int? PrepMinutes,
        int? CookMinutes,
        int? TotalMinutes,
        int? Servings,
        int? CaloriesPerServing,
        string? ImageUrl);

    private sealed record CachedPublisherPage(IReadOnlyList<PublisherRecipeData> Recipes);

    private sealed record PublisherFetchResult(
        bool Cacheable,
        IReadOnlyList<PublisherRecipeData> Recipes);
}
