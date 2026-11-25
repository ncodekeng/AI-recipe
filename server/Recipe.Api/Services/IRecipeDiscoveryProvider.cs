namespace Recipe.Api.Services;

/// <summary>
/// One candidate page found during discovery. Deliberately thin: a URL, what the search engine called
/// it, and which query turned it up.
///
/// Discovery used to return finished recipes, because the only way to find a page was to ask a model to
/// find one and write it up in the same breath. That cost up to eight model calls of roughly 6,400 input
/// tokens before a single publisher page was read, and it is the reason a search took 25 to 122 seconds.
/// Separating "which pages exist" from "what is on them" lets the first step be cheap and the second use
/// PublisherRecipePageClient, which already reads schema.org JSON-LD and costs nothing per page.
/// </summary>
/// <param name="Url">The publisher page, exactly as the provider returned it. Never invented.</param>
/// <param name="Title">The provider's title for the page, used for dish-concept work before fetching.</param>
/// <param name="Snippet">The provider's extract, where it gives one.</param>
/// <param name="Query">The query that found it, kept for attribution and for diagnosing a thin result.</param>
/// <param name="Source">Which Serper endpoint found it, so a benchmark can tell the two apart.</param>
/// <param name="PublisherName">What the search engine called the site. Never used as the recipe's source.</param>
/// <param name="ImageUrl">
/// The photograph the image search pointed at. Carried for diagnostics and ranking ONLY: it is somebody
/// else's copyrighted photograph, reached through a Google index, and PLATE neither displays, downloads nor
/// rehosts it. Pictures come from CommercialRecipeImageClient, which verifies a commercial licence, or from
/// the publisher's own schema.org image under the existing prototype flag. A test holds this.
/// </param>
/// <param name="ThumbnailUrl">Google's thumbnail of the above, under exactly the same restriction.</param>
public sealed record RecipeCandidate(
    string Url,
    string Title,
    string? Snippet,
    string Query,
    RecipeCandidateSource Source = RecipeCandidateSource.WebSearch,
    string? PublisherName = null,
    string? ImageUrl = null,
    string? ThumbnailUrl = null);

/// <summary>Which Serper endpoint produced a candidate.</summary>
public enum RecipeCandidateSource
{
    WebSearch,
    ImageSearch,
}

/// <summary>
/// Finds candidate recipe pages. Implementations do no extraction and make no claim about what a page
/// contains beyond what the search engine said.
/// </summary>
public interface IRecipeDiscoveryProvider
{
    /// <summary>Configuration name: GoogleSearch, AzureWebSearch.</summary>
    string Name { get; }

    /// <summary>False when credentials are missing, so the caller can fall back rather than fail.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Runs the planned queries and returns the pages they found, deduplicated by canonical URL across
    /// the whole set. Queries run concurrently; one failing must not lose the others.
    /// </summary>
    Task<IReadOnlyList<RecipeCandidate>> DiscoverAsync(
        IReadOnlyList<string> queries,
        int resultsPerQuery,
        CancellationToken cancellationToken);
}
