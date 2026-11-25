using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

/// <summary>
/// Finds candidate recipe pages through Serper's Google Search API.
///
/// It replaces the discovery half of AzureGroundedRecipeClient, not the whole thing. Asking a model to
/// find pages cost up to eight calls of roughly 6,400 input tokens each and most of the 25-to-122 second
/// search time; a search API answers the same question in a round trip. What the model was genuinely
/// good at -- reading a page that has no structured data -- stays, as a fallback.
///
/// The queries come from RecipeSearchPlanner and run concurrently, because they are independent and the
/// slowest one should set the pace rather than the sum of all of them. One query failing loses that
/// query's results and nothing else: a thinner batch beats no batch.
///
/// Two endpoints, asked at the same time and merged: `search` for the web index and `images` for the image
/// index. They are different indexes rather than two views of one, so each finds pages the other does not.
/// An image result is read for its `link` -- the page the photograph was published on, which is where the
/// recipe is -- and never for the photograph, which belongs to whoever took it. DiscoveryMode selects one
/// or both, because which is worth its credits is a measurement rather than an opinion.
///
/// The key is read from configuration on the server and never leaves it.
/// </summary>
public sealed class SerperDiscoveryProvider : IRecipeDiscoveryProvider
{
    private readonly HttpClient _httpClient;
    private readonly GoogleSearchOptions _options;
    private readonly ILogger<SerperDiscoveryProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SerperDiscoveryProvider(
        HttpClient httpClient,
        IOptions<RecipeCatalogOptions> options,
        ILogger<SerperDiscoveryProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.GoogleSearch;
        _logger = logger;
    }

    public string Name => "GoogleSearch";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<RecipeCandidate>> DiscoverAsync(
        IReadOnlyList<string> queries,
        int resultsPerQuery,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Google recipe discovery needs a Serper API key. PLATE will not invent a replacement recipe.");
        }

        if (queries.Count == 0)
        {
            return [];
        }

        var perQuery = Math.Clamp(resultsPerQuery, 1, 20);
        var imagesPerQuery = Math.Clamp(_options.ImageResultsPerQuery, 1, 20);

        // One gate across both endpoints, not one each: the limit exists to bound how hard PLATE leans on
        // Serper at once, and two gates of four would quietly make it eight.
        var gate = new SemaphoreSlim(Math.Clamp(_options.MaxConcurrentQueries, 1, 8));

        // A deadline for the whole discovery step, not per query, so a slow provider cannot stretch the
        // search past the point a customer would wait.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 2, 30)));

        // Both endpoints are asked at the same time. They are independent indexes answering the same
        // question, so running them in sequence would pay twice the latency for the same breadth.
        var webTasks = _options.UsesWebSearch
            ? queries.Select(query => RunAsync(
                "search", query, perQuery, RecipeCandidateSource.WebSearch, gate, deadline.Token)).ToList()
            : [];
        var imageTasks = _options.UsesImageSearch
            ? queries.Select(query => RunAsync(
                "images", query, imagesPerQuery, RecipeCandidateSource.ImageSearch, gate, deadline.Token)).ToList()
            : [];

        var webBatches = await Task.WhenAll(webTasks);
        var imageBatches = await Task.WhenAll(imageTasks);

        var web = webBatches.SelectMany(batch => batch).ToList();
        var images = imageBatches.SelectMany(batch => batch).ToList();

        // Merged by taking from each in turn rather than one list after the other. Whichever came second
        // would otherwise sit past MaxPagesRead and never be read at all, so the mode would be combined in
        // name only.
        var merged = Interleave(web, images);

        // Pages that look like a single recipe go first. Nothing is discarded here -- the caller decides
        // what to drop and counts it -- but a roundup or a Pinterest board should not take a read slot
        // ahead of a recipe. A stable ordering, so query order still breaks ties.
        var ordered = merged
            .OrderBy(candidate =>
                RecipeDishConcept.IsUnsuitableSource(candidate.Url) ? 2
                : RecipeDishConcept.IsListingPage(candidate.Title, candidate.Url) ? 1
                : 0)
            .ToList();

        // Deduplicated by canonical URL, keeping the first occurrence: the same page found by both
        // endpoints is one candidate, and one publisher fetch.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<RecipeCandidate>();
        foreach (var candidate in ordered)
        {
            if (CanonicalUrl(candidate.Url) is { } canonical && seen.Add(canonical))
            {
                candidates.Add(candidate);
            }
        }

        _logger.LogInformation(
            "GOOGLE DISCOVERY mode={Mode} queries={QueryCount} web={Web} images={Images} "
            + "overlap={Overlap} unique={UniqueCount}",
            _options.DiscoveryMode,
            queries.Count,
            web.Count,
            images.Count,
            web.Count + images.Count - candidates.Count,
            candidates.Count);

        return candidates;
    }

    /// <summary>Takes from each list in turn, so neither endpoint monopolises the head of the pool.</summary>
    private static List<RecipeCandidate> Interleave(
        IReadOnlyList<RecipeCandidate> first,
        IReadOnlyList<RecipeCandidate> second)
    {
        var merged = new List<RecipeCandidate>(first.Count + second.Count);
        for (var index = 0; index < Math.Max(first.Count, second.Count); index++)
        {
            if (index < first.Count)
            {
                merged.Add(first[index]);
            }

            if (index < second.Count)
            {
                merged.Add(second[index]);
            }
        }

        return merged;
    }

    private async Task<IReadOnlyList<RecipeCandidate>> RunAsync(
        string endpoint,
        string query,
        int perQuery,
        RecipeCandidateSource source,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(
                    new { q = query, num = perQuery, gl = _options.Country, hl = _options.Language },
                    options: JsonOptions),
            };
            message.Headers.Add("X-API-KEY", _options.ApiKey);

            using var response = await _httpClient.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Serper rate-limited the query {Query}; continuing with the other queries.", query);
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                // Never the body: a provider error page can echo the request, and the key is in a header.
                _logger.LogWarning(
                    "Serper returned {Status} for a discovery query; continuing with the other queries.",
                    (int)response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return source == RecipeCandidateSource.ImageSearch
                ? ReadImageResults(document.RootElement, query)
                : ReadOrganicResults(document.RootElement, query);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The discovery deadline, not the caller giving up. Other queries may still have answered.
            _logger.LogWarning("A discovery query passed the deadline and was dropped.");
            return [];
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            _logger.LogWarning(exception, "A discovery query failed; continuing with the other queries.");
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Reads Serper's `organic` array. Anything without an https link is ignored.</summary>
    private static List<RecipeCandidate> ReadOrganicResults(JsonElement root, string query)
    {
        var results = new List<RecipeCandidate>();
        if (!root.TryGetProperty("organic", out var organic) || organic.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in organic.EnumerateArray())
        {
            var link = ReadString(item, "link");
            if (link is null ||
                !Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            results.Add(new RecipeCandidate(
                link,
                ReadString(item, "title") ?? string.Empty,
                ReadString(item, "snippet"),
                query));
        }

        return results;
    }

    /// <summary>
    /// Reads Serper's `images` array.
    ///
    /// The important field is `link`, the page the photograph sits on -- that is the candidate recipe. An
    /// image search result is a picture of a dinner; the recipe is on the page it was published on, and
    /// that page is read exactly like any web-search hit. `imageUrl` is the photograph itself and is kept
    /// only so a benchmark and the logs can see what was matched: it is a third party's copyrighted image
    /// reached through a Google index, and PLATE never displays, downloads or rehosts it.
    /// </summary>
    private static List<RecipeCandidate> ReadImageResults(JsonElement root, string query)
    {
        var results = new List<RecipeCandidate>();
        if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in images.EnumerateArray())
        {
            var link = ReadString(item, "link");
            if (link is null ||
                !Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            results.Add(new RecipeCandidate(
                link,
                ReadString(item, "title") ?? string.Empty,
                null,
                query,
                RecipeCandidateSource.ImageSearch,
                ReadString(item, "source") ?? ReadString(item, "domain"),
                ReadString(item, "imageUrl"),
                ReadString(item, "thumbnailUrl")));
        }

        return results;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// One page, one entry. Tracking parameters, the fragment, a trailing slash and the host's "www."
    /// all vary without changing the page, and a duplicate costs a publisher fetch for nothing.
    /// </summary>
    public static string? CanonicalUrl(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{host.ToLowerInvariant()}{path.ToLowerInvariant()}";
    }
}
