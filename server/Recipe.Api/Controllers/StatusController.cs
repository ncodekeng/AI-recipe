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

        return Ok(new ServiceStatusResponse(
            "ok",
            provider,
            azureConfigured,
            "Edamam",
            catalog.Edamam.IsConfigured));
    }
}
