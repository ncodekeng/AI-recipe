using Recipe.Api.Options;
using Recipe.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.Configure<FoodAiOptions>(builder.Configuration.GetSection(FoodAiOptions.SectionName));
builder.Services.Configure<RecipeCatalogOptions>(builder.Configuration.GetSection(RecipeCatalogOptions.SectionName));
builder.Services.Configure<UsageControlOptions>(builder.Configuration.GetSection(UsageControlOptions.SectionName));
var recipeCacheMaxEntries = builder.Configuration.GetValue<int?>("RecipeCatalog:Cache:MaxEntries") ?? 500;
builder.Services.AddMemoryCache(options => options.SizeLimit = Math.Clamp(recipeCacheMaxEntries, 10, 5000));
builder.Services.AddSingleton<DemoFoodAiService>();
builder.Services.AddSingleton<RecipeSafetyValidator>();
builder.Services.AddSingleton<IngredientNormalizer>();
builder.Services.AddSingleton<RecipeRankingService>();
builder.Services.AddSingleton<RecipeSearchCache>();
builder.Services.AddSingleton<IGroceryBasketService, DeliverooBasketService>();
builder.Services.AddSingleton(TimeProvider.System);
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
