using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController(
    IOptions<FoodAiOptions> options,
    IOptions<RecipeCatalogOptions> catalogOptions) : ControllerBase
{
    [HttpGet]
    public ActionResult<ServiceStatusResponse> Get()
    {
        var settings = options.Value;
        var azureConfigured = settings.AzureOpenAI.IsConfigured;
        var provider = settings.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase) && azureConfigured
            ? "Azure OpenAI"
            : "Demo";

        var catalog = catalogOptions.Value;
        var useEdamam = catalog.Provider.Equals("Edamam", StringComparison.OrdinalIgnoreCase);
        var useAzureWebSearch = catalog.Provider.Equals("AzureWebSearch", StringComparison.OrdinalIgnoreCase);
        var recipeProvider = useEdamam
            ? "Edamam"
            : useAzureWebSearch ? "Azure Web Search" : "Unknown recipe provider";
        var recipeProviderConfigured = useEdamam
            ? catalog.Edamam.IsConfigured
            : useAzureWebSearch && azureConfigured;

        return Ok(new ServiceStatusResponse(
            "ok",
            provider,
            azureConfigured,
            recipeProvider,
            recipeProviderConfigured));
    }
}
