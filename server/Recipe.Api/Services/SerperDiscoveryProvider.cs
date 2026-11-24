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
        var gate = new SemaphoreSlim(Math.Clamp(_options.MaxConcurrentQueries, 1, 8));

        // A deadline for the whole discovery step, not per query, so a slow provider cannot stretch the
        // search past the point a customer would wait.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 2, 30)));

        var tasks = queries.Select(query => RunAsync(query, perQuery, gate, deadline.Token)).ToList();
        var batches = await Task.WhenAll(tasks);

        // Deduplicated across every query, keeping the first occurrence: queries are ordered most
        // promising first, so the earliest hit is the one worth keeping.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<RecipeCandidate>();
        foreach (var candidate in batches.SelectMany(batch => batch))
        {
            if (CanonicalUrl(candidate.Url) is { } canonical && seen.Add(canonical))
            {
                candidates.Add(candidate);
            }
        }

        _logger.LogInformation(
            "GOOGLE DISCOVERY queries={QueryCount} candidates={CandidateCount} unique={UniqueCount}",
            queries.Count,
            batches.Sum(batch => batch.Count),
            candidates.Count);

        return candidates;
    }

    private async Task<IReadOnlyList<RecipeCandidate>> RunAsync(
        string query,
        int perQuery,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "search")
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
            return ReadOrganicResults(document.RootElement, query);
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
