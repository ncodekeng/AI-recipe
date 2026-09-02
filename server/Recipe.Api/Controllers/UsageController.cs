using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/usage")]
public sealed class UsageController(
    AiUsageGuard usageGuard,
    IOptions<UsageControlOptions> options) : ControllerBase
{
    [HttpGet]
    public ActionResult<UsageStatusResponse> Get() =>
        Ok(usageGuard.GetStatus(ClientIdentity.Resolve(HttpContext)));

    [HttpPost("reset")]
    public ActionResult<UsageStatusResponse> Reset()
    {
        if (!options.Value.AllowTestReset)
        {
            return NotFound();
        }

        return Ok(usageGuard.ResetClient(ClientIdentity.Resolve(HttpContext)));
    }
}
