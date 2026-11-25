using Recipe.Api.Options;
using Recipe.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var configuredClientOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
var allowedClientOrigins = configuredClientOrigins
    .Concat(builder.Environment.IsDevelopment()
        ? ["http://localhost:5173", "https://localhost:5173"]
        : Array.Empty<string>())
    .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || builder.Environment.IsDevelopment()))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddDataProtection();
builder.Services.Configure<FoodAiOptions>(builder.Configuration.GetSection(FoodAiOptions.SectionName));
builder.Services.Configure<RecipeCatalogOptions>(builder.Configuration.GetSection(RecipeCatalogOptions.SectionName));
builder.Services.Configure<UsageControlOptions>(builder.Configuration.GetSection(UsageControlOptions.SectionName));
builder.Services.Configure<PromptAdminOptions>(builder.Configuration.GetSection(PromptAdminOptions.SectionName));
var recipeCacheMaxEntries = builder.Configuration.GetValue<int?>("RecipeCatalog:Cache:MaxEntries") ?? 500;
var scanCacheMaxEntries = builder.Configuration.GetValue<int?>("FoodAi:ScanCache:MaxEntries") ?? 500;
var photoCacheMaxEntries = builder.Configuration.GetValue<int?>("RecipeCatalog:CommercialImages:CacheMaxEntries") ?? 500;
var publisherCacheMaxEntries = builder.Configuration.GetValue<int?>("RecipeCatalog:PublisherExtraction:CacheMaxEntries") ?? 500;
builder.Services.AddMemoryCache(options =>
    options.SizeLimit = Math.Clamp(
        recipeCacheMaxEntries + scanCacheMaxEntries + photoCacheMaxEntries + publisherCacheMaxEntries,
        20,
        10000));
builder.Services.AddSingleton<DemoFoodAiService>();
builder.Services.AddSingleton<RecipeSafetyValidator>();
builder.Services.AddSingleton<IngredientNormalizer>();
builder.Services.AddSingleton<RecipeRankingService>();
builder.Services.AddSingleton<RecipeSearchCache>();
builder.Services.AddSingleton<RecipePhotoCache>();
builder.Services.AddSingleton<IngredientScanCache>();
builder.Services.AddSingleton<IGroceryBasketService, DeliverooBasketService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PromptConfigurationStore>();
builder.Services.AddSingleton<IAiPromptProvider>(services =>
    services.GetRequiredService<PromptConfigurationStore>());
builder.Services.AddSingleton<AdminSessionService>();
builder.Services.AddSingleton<AiUsageGuard>();
builder.Services.AddSingleton<FeedbackService>();
builder.Services.AddHttpClient<AzureOpenAiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(75);
});
builder.Services.AddHttpClient<EdamamRecipeClient>(client =>
{
    client.BaseAddress = new Uri("https://api.edamam.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<AzureGroundedRecipeClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});
// Writing the cooking guide: one call, no web_search tool, so it is a fraction of the old search call.
// The timeout is its own deadline rather than the 90 seconds the grounded client needs, because a slow
// guide is dropped and the recipes are still returned without one.
builder.Services.AddHttpClient<CookingGuideWriter>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(40);
});
// Asking a model what to search for, which is under investigation rather than in use. A short timeout
// because the deterministic plan is already in hand: waiting longer costs the customer more than better
// queries could be worth.
builder.Services.AddHttpClient<LlmQueryPlanner>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
// Discovery through Serper. A short timeout on purpose: this step exists to be fast, and a slow search
// provider should be dropped and worked around rather than allowed to stretch the whole request. The
// per-query deadline inside the provider is shorter still.
builder.Services.AddHttpClient<SerperDiscoveryProvider>(client =>
{
    var endpoint = builder.Configuration["RecipeCatalog:GoogleSearch:Endpoint"];
    client.BaseAddress = new Uri(
        string.IsNullOrWhiteSpace(endpoint) ? "https://google.serper.dev/" : endpoint);
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<PublisherRecipePageClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PLATE/1.0 (publisher recipe metadata verification)");
}).ConfigurePrimaryHttpMessageHandler(SafePublisherHttpMessageHandler.Create);
builder.Services.AddHttpClient<CommercialRecipeImageClient>(client =>
{
    client.BaseAddress = new Uri("https://commons.wikimedia.org/");
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PLATE/1.0 (commercial-license image verification)");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});
// The seam is the interface, so the pipeline can be tested against a stub and a second search engine can
// be added without touching it. SerperDiscoveryProvider is registered above with its own HttpClient.
builder.Services.AddScoped<IRecipeDiscoveryProvider>(services =>
    services.GetRequiredService<SerperDiscoveryProvider>());
builder.Services.AddScoped<GoogleRecipeSearchClient>();
builder.Services.AddScoped<IRecipeAiService, RecipeAiService>();
builder.Services.AddScoped<IRecipeCatalogService, RecipeCatalogService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("PlateClient", policy =>
    {
        if (allowedClientOrigins.Length > 0)
        {
            policy.WithOrigins(allowedClientOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

var app = builder.Build();

app.UseExceptionHandler();
if (allowedClientOrigins.Length > 0)
{
    app.UseCors("PlateClient");
}

app.MapControllers();

if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
