using Recipe.Api.Options;
using Recipe.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddDataProtection();
builder.Services.Configure<FoodAiOptions>(builder.Configuration.GetSection(FoodAiOptions.SectionName));
builder.Services.Configure<RecipeCatalogOptions>(builder.Configuration.GetSection(RecipeCatalogOptions.SectionName));
builder.Services.Configure<UsageControlOptions>(builder.Configuration.GetSection(UsageControlOptions.SectionName));
builder.Services.Configure<PromptAdminOptions>(builder.Configuration.GetSection(PromptAdminOptions.SectionName));
var recipeCacheMaxEntries = builder.Configuration.GetValue<int?>("RecipeCatalog:Cache:MaxEntries") ?? 500;
var scanCacheMaxEntries = builder.Configuration.GetValue<int?>("FoodAi:ScanCache:MaxEntries") ?? 500;
builder.Services.AddMemoryCache(options =>
    options.SizeLimit = Math.Clamp(recipeCacheMaxEntries + scanCacheMaxEntries, 20, 10000));
builder.Services.AddSingleton<DemoFoodAiService>();
builder.Services.AddSingleton<RecipeSafetyValidator>();
builder.Services.AddSingleton<IngredientNormalizer>();
builder.Services.AddSingleton<RecipeRankingService>();
builder.Services.AddSingleton<RecipeSearchCache>();
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
builder.Services.AddHttpClient<CommercialRecipeImageClient>(client =>
{
    client.BaseAddress = new Uri("https://commons.wikimedia.org/");
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PLATE/1.0 (commercial-license image verification)");
});
builder.Services.AddScoped<IRecipeAiService, RecipeAiService>();
builder.Services.AddScoped<IRecipeCatalogService, RecipeCatalogService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentClient", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentClient");
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
