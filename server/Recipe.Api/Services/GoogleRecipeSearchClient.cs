using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

/// <summary>
/// Finds recipes by searching for pages and then reading them, in that order.
///
/// The Azure path does both in one breath: a model is asked to search the web and write the recipes out in
/// the same response. Measured over 21 calls on 24 November 2026 that cost 14.3 seconds a call and 96-98%
/// of every search, because the time goes on generating 14-26KB of recipe prose a token at a time, and
/// because the calls run one after another -- two to five of them, a quarter of which were retries for
/// unreadable JSON. It also drifts: three searches for beef brisket returned a beef burger, savoury mince,
/// stroganoff, stir fry, roast beef and a taco casserole, and no brisket at all.
///
/// Here the two halves are separated and each is done by the thing that is good at it. A search engine
/// finds pages in about a second. Publishers state their own titles, ingredients, timings and servings in
/// schema.org markup that costs nothing to parse and cannot drift, because the page either says brisket or
/// it does not. Nothing is asked to invent a URL, and no recipe is claimed without the page being read.
///
/// What is deliberately not here is the cooking method. It is the publisher's own writing, so it is never
/// copied: recipes leave here with no steps at all. CookingGuideWriter then writes PLATE's own guide for the
/// same dish, after ranking, so the method a customer reads is ours and the facts around it are the
/// publisher's. Keeping the two apart is also what lets a dish survive a failed guide: the ingredients,
/// timings and photo are already real, so the recipe is still worth returning without one.
/// </summary>
public sealed class GoogleRecipeSearchClient(
    IRecipeDiscoveryProvider discovery,
    PublisherRecipePageClient publisherPages,
    IngredientNormalizer normalizer,
    IOptions<RecipeCatalogOptions> options,
    ILogger<GoogleRecipeSearchClient> logger)
{
    private readonly GoogleSearchOptions _options = options.Value.GoogleSearch;

    /// <summary>Below this a page is a fragment, an index or an advert, not a recipe.</summary>
    private const int MinimumIngredientLines = 3;

    public bool IsConfigured => discovery.IsConfigured;

    public async Task<RecipeGenerationResponse> FindRecipesAsync(
        GenerateRecipesRequest request,
        CancellationToken cancellationToken)
    {
        var target = Math.Clamp(request.MaxRecipes, 1, 12);
        var queries = RecipeSearchPlanner.Plan(request, _options.MaxQueries);

        var candidates = await discovery.DiscoverAsync(queries, _options.ResultsPerQuery, cancellationToken);

        // Listing pages are dropped before anything is fetched. An index page titled "Beef brisket recipes"
        // was 3 of the 33 candidates in the measured run, and each one would otherwise cost a page read to
        // discover it holds no single recipe.
        var worthReading = candidates
            .Where(candidate => !RecipeDishConcept.IsListingPage(candidate.Title, candidate.Url))
            .Take(Math.Clamp(_options.MaxPagesRead, 1, 60))
            .ToList();

        if (worthReading.Count == 0)
        {
            throw new RecipeSafetyException(
                "The recipe search found no candidate pages to read. Try different ingredients or restrictions.");
        }

        var collected = await ReadUntilEnoughAsync(worthReading, request, target, cancellationToken);

        if (collected.Recipes.Count == 0)
        {
            throw new RecipeSafetyException(
                "No page found could be read as a recipe for what you have. PLATE will not invent one.");
        }

        logger.LogInformation(
            "GOOGLE PIPELINE queries={Queries} candidates={Candidates} read={Read} accepted={Accepted} "
            + "rejectedListing={RejectedListing} rejectedMainIngredient={RejectedMain} "
            + "rejectedDuplicate={RejectedDuplicate} rejectedThin={RejectedThin}",
            queries.Count,
            candidates.Count,
            collected.PagesRead,
            collected.Recipes.Count,
            candidates.Count - worthReading.Count,
            collected.RejectedMainIngredient,
            collected.RejectedDuplicate,
            collected.RejectedThin);

        return new RecipeGenerationResponse(
            collected.Recipes,
            "Google Search",
            "Ingredients and timings are read from each publisher's own recipe data, and the cooking guide is "
                + "PLATE's own. Check the source page before cooking, and treat allergy information as "
                + "unconfirmed.");
    }

    /// <summary>
    /// Reads candidate pages a wave at a time and stops as soon as there are enough distinct dishes.
    ///
    /// A wave rather than one big fan-out, because the point is to pay for as few page reads as possible:
    /// with six workers and a target of four, the common case reads six pages and stops. Firing all
    /// twenty-four at once would be no faster to the fourth result and would hit twenty publishers who
    /// were never needed.
    /// </summary>
    private async Task<Collected> ReadUntilEnoughAsync(
        IReadOnlyList<RecipeCandidate> candidates,
        GenerateRecipesRequest request,
        int target,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.PageReadTimeoutSeconds, 3, 60)));

        var waveSize = Math.Clamp(_options.MaxConcurrentPageReads, 1, 10);
        var accepted = new List<RecipeSuggestion>();
        var usedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagesRead = 0;
        var rejectedMain = 0;
        var rejectedDuplicate = 0;
        var rejectedThin = 0;

        for (var offset = 0; offset < candidates.Count && accepted.Count < target; offset += waveSize)
        {
            var wave = candidates.Skip(offset).Take(waveSize).ToList();
            IReadOnlyList<PublisherPageRecipe>[] pages;
            try
            {
                pages = await Task.WhenAll(wave.Select(candidate =>
                    publisherPages.ReadPageRecipesAsync(candidate.Url, deadline.Token)));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The page-reading deadline, not the caller giving up. Whatever was accepted still stands.
                logger.LogWarning("Reading candidate pages passed the deadline; returning what was read.");
                break;
            }

            pagesRead += wave.Count;

            for (var i = 0; i < wave.Count && accepted.Count < target; i++)
            {
                var candidate = wave[i];
                var page = BestRecipeOnPage(pages[i]);
                if (page is null)
                {
                    rejectedThin++;
                    continue;
                }

                if (!MentionsMainIngredient(page, request))
                {
                    rejectedMain++;
                    continue;
                }

                // Two dinners, not two pages about one dinner. The URL dedup in discovery cannot see this:
                // four publishers writing up the same slow-cooked brisket are four distinct URLs. One
                // recipe per publisher for the same reason -- a page of results from one site reads as a
                // thin search even when every dish on it differs.
                if (accepted.Any(existing => RecipeDishConcept.IsSameDish(existing.Title, page.Title)) ||
                    !usedDomains.Add(DomainOf(candidate.Url)))
                {
                    rejectedDuplicate++;
                    continue;
                }

                accepted.Add(BuildSuggestion(candidate, page, request));
            }
        }

        return new Collected(accepted, pagesRead, rejectedMain, rejectedDuplicate, rejectedThin);
    }

    /// <summary>
    /// A page can describe several recipes. The one with the most ingredients stated is the one the page is
    /// about; the others are usually a "you might also like" rail.
    /// </summary>
    private static PublisherPageRecipe? BestRecipeOnPage(IReadOnlyList<PublisherPageRecipe> pageRecipes) =>
        pageRecipes
            .Where(recipe =>
                !string.IsNullOrWhiteSpace(recipe.Title) &&
                recipe.IngredientLines.Count >= MinimumIngredientLines)
            .OrderByDescending(recipe => recipe.IngredientLines.Count)
            .FirstOrDefault();

    /// <summary>
    /// The relevance guarantee the model-driven search does not give. A customer who says brisket is not
    /// asking for whatever else is made of beef, so the word has to appear in what the publisher states
    /// the recipe contains.
    ///
    /// This deliberately does not use IngredientNormalizer. That exists to decide whether a pantry item
    /// covers a required one, and to do it generously -- it collapses a whole line to a canonical concept,
    /// so "1 kg beef brisket" becomes "beef" and the word that narrows the request is gone before any
    /// comparison happens. Generosity is right for "have I got this in?" and wrong for "is this the dish I
    /// asked for?": it is how a search for beef brisket returned beef mince in a taco casserole. So the
    /// words are compared as words, tolerating nothing but a plural.
    /// </summary>
    private bool MentionsMainIngredient(PublisherPageRecipe page, GenerateRecipesRequest request)
    {
        if (!_options.RequireMainIngredient)
        {
            return true;
        }

        var wanted = SignificantWords(request.MainIngredient).ToList();
        if (wanted.Count == 0)
        {
            return true;
        }

        var stated = page.IngredientLines
            .Append(page.Title)
            .SelectMany(SignificantWords)
            .ToHashSet(StringComparer.Ordinal);

        // Every word, not any of them: "beef brisket" is not satisfied by beef alone.
        return wanted.All(stated.Contains);
    }

    /// <summary>
    /// The words of a phrase, lowercased, stripped of punctuation and digits, and reduced to a rough
    /// singular. Short words are dropped because they carry no weight and match too much.
    /// </summary>
    private static IEnumerable<string> SignificantWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var letters = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            letters.Append(char.IsLetter(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return letters.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 4)
            .Select(Singular);
    }

    private static string Singular(string word) =>
        word.Length > 4 && word.EndsWith("es", StringComparison.Ordinal) ? word[..^2]
        : word.Length > 3 && word.EndsWith('s') ? word[..^1]
        : word;

    private RecipeSuggestion BuildSuggestion(
        RecipeCandidate candidate,
        PublisherPageRecipe page,
        GenerateRecipesRequest request)
    {
        var title = Truncate(page.Title.Trim(), 120);
        var totalMinutes = page.TotalMinutes ?? Sum(page.PrepMinutes, page.CookMinutes) ?? 0;
        var domain = DomainOf(candidate.Url);

        return new RecipeSuggestion(
            StableGuid(candidate.Url),
            title,
            // Factual, and not a word of the publisher's own description. Theirs is their writing.
            $"Published by {domain}.",
            totalMinutes,
            DifficultyFor(totalMinutes, page.IngredientLines.Count),
            string.IsNullOrWhiteSpace(request.CuisinePreference) ? "Not stated" : request.CuisinePreference.Trim(),
            page.Servings ?? Math.Max(1, request.Servings),
            IngredientMatch: 0,
            Tags: [],
            Ingredients: publisherPages.MapPublisherIngredients(page.IngredientLines),
            // No steps on purpose: the method is the publisher's writing, and this path does not have one
            // of its own yet. Writing it is a separate step with its own cost.
            Steps: [],
            Accent: AccentFor(title),
            SourceName: domain,
            SourceUrl: candidate.Url,
            DirectionsKind: RecipeDirectionsKinds.Unavailable,
            CaloriesPerServing: page.CaloriesPerServing,
            SourceVerified: true,
            SourceTitle: string.IsNullOrWhiteSpace(candidate.Title) ? title : Truncate(candidate.Title.Trim(), 160),
            PrepMinutes: page.PrepMinutes,
            CookMinutes: page.CookMinutes,
            // The page was fetched and read, which is the whole basis for this recipe existing.
            PublisherPageVerified: true,
            PublisherImageCandidateUrl: page.ImageUrl);
    }

    private static int? Sum(int? first, int? second) =>
        first is null && second is null ? null : (first ?? 0) + (second ?? 0);

    private static string DifficultyFor(int totalMinutes, int ingredientCount) =>
        (totalMinutes, ingredientCount) switch
        {
            ( <= 30, <= 8) => "Easy",
            ( >= 120, _) or (_, >= 16) => "Challenging",
            _ => "Medium"
        };

    private static string DomainOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : "the publisher";

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash[..16]);
    }

    private static string AccentFor(string title) => ((StableGuid(title).GetHashCode() & int.MaxValue) % 3) switch
    {
        0 => "coral",
        1 => "saffron",
        _ => "sage"
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record Collected(
        IReadOnlyList<RecipeSuggestion> Recipes,
        int PagesRead,
        int RejectedMainIngredient,
        int RejectedDuplicate,
        int RejectedThin);
}
