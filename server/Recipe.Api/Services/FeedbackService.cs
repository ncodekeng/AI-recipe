using Microsoft.Extensions.Caching.Memory;
using Recipe.Api.Models;

namespace Recipe.Api.Services;

public sealed class FeedbackService(ILogger<FeedbackService> logger) : IDisposable
{
    private readonly MemoryCache _recentClients = new(new MemoryCacheOptions { SizeLimit = 5_000 });

    public bool TrySubmit(string clientId, FeedbackRequest request)
    {
        if (_recentClients.TryGetValue(clientId, out _))
        {
            return false;
        }

        _recentClients.Set(
            clientId,
            true,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                Size = 1
            });

        var message = request.Message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        logger.LogInformation(
            "Prototype feedback received. ClientHash: {ClientHash}; Rating: {Rating}; Message: {Message}",
            clientId,
            request.Rating,
            message);
        return true;
    }

    public void Dispose() => _recentClients.Dispose();
}
