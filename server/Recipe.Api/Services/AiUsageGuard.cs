using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public enum AiOperation
{
    IngredientScan,
    RecipeGeneration
}

public sealed record UsageRejection(int StatusCode, string Title, string Detail);

public sealed record UsageAdmission(
    AiUsageLease? Lease,
    UsageStatusResponse Status,
    UsageRejection? Rejection)
{
    public bool Allowed => Lease is not null;
}

public sealed class AiUsageLease(Action release) : IDisposable
{
    private Action? _release = release;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

public sealed class AiUsageGuard(IOptions<UsageControlOptions> options, TimeProvider timeProvider)
{
    private readonly object _sync = new();
    private readonly UsageControlOptions _options = options.Value;
    private readonly Dictionary<string, ClientUsage> _usage = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeClients = new(StringComparer.Ordinal);
    private DateOnly _currentDay = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    private decimal _estimatedSpendUsd;

    public UsageAdmission TryAcquire(
        string clientKey,
        AiOperation operation,
        bool hasUnlimitedQuota = false)
    {
        lock (_sync)
        {
            ResetIfNeeded();
            var usage = GetOrCreateUsage(clientKey);

            if (!_options.Enabled)
            {
                return Allow(clientKey, usage, operation, trackUsage: false, isUnlimited: true);
            }

            if (!_options.AiEnabled)
            {
                return Reject(
                    usage,
                    503,
                    "AI requests are temporarily paused.",
                    "Please try again later. Saved recipes remain available.");
            }

            if (_activeClients.Contains(clientKey))
            {
                return Reject(
                    usage,
                    429,
                    "One request is already running.",
                    "Wait for the current scan or recipe request to finish before trying again.");
            }

            var used = operation == AiOperation.IngredientScan ? usage.Scans : usage.Recipes;
            var limit = operation == AiOperation.IngredientScan
                ? Math.Max(0, _options.DailyScanLimit)
                : Math.Max(0, _options.DailyRecipeLimit);

            if (!hasUnlimitedQuota && used >= limit)
            {
                return Reject(
                    usage,
                    429,
                    "Today's free allowance has been used.",
                    $"Your allowance resets at {NextResetUtc():HH:mm} UTC.");
            }

            var estimatedCost = operation == AiOperation.IngredientScan
                ? Math.Max(0, _options.EstimatedScanCostUsd)
                : Math.Max(0, _options.EstimatedRecipeCostUsd);

            if (_options.GlobalDailyBudgetUsd > 0 &&
                _estimatedSpendUsd + estimatedCost > _options.GlobalDailyBudgetUsd)
            {
                return Reject(
                    usage,
                    503,
                    "The daily AI budget has been reached.",
                    "Free AI requests are paused until the next UTC day.");
            }

            return Allow(
                clientKey,
                usage,
                operation,
                trackUsage: true,
                isUnlimited: hasUnlimitedQuota);
        }
    }

    public UsageStatusResponse GetStatus(string clientKey, bool hasUnlimitedQuota = false)
    {
        lock (_sync)
        {
            ResetIfNeeded();
            return CreateStatus(GetOrCreateUsage(clientKey), hasUnlimitedQuota);
        }
    }

    public UsageStatusResponse ResetClient(string clientKey, bool hasUnlimitedQuota = false)
    {
        lock (_sync)
        {
            ResetIfNeeded();
            _usage.Remove(clientKey);
            _activeClients.Remove(clientKey);
            return CreateStatus(GetOrCreateUsage(clientKey), hasUnlimitedQuota);
        }
    }

    private UsageAdmission Allow(
        string clientKey,
        ClientUsage usage,
        AiOperation operation,
        bool trackUsage,
        bool isUnlimited)
    {
        _activeClients.Add(clientKey);
        if (trackUsage)
        {
            if (operation == AiOperation.IngredientScan)
            {
                usage.Scans++;
                _estimatedSpendUsd += Math.Max(0, _options.EstimatedScanCostUsd);
            }
            else
            {
                usage.Recipes++;
                _estimatedSpendUsd += Math.Max(0, _options.EstimatedRecipeCostUsd);
            }
        }

        var lease = new AiUsageLease(() => Release(clientKey));
        return new UsageAdmission(lease, CreateStatus(usage, isUnlimited), null);
    }

    private UsageAdmission Reject(
        ClientUsage usage,
        int statusCode,
        string title,
        string detail) =>
        new(null, CreateStatus(usage), new UsageRejection(statusCode, title, detail));

    private void Release(string clientKey)
    {
        lock (_sync)
        {
            _activeClients.Remove(clientKey);
        }
    }

    private ClientUsage GetOrCreateUsage(string clientKey)
    {
        if (_usage.TryGetValue(clientKey, out var usage))
        {
            return usage;
        }

        usage = new ClientUsage();
        _usage[clientKey] = usage;
        return usage;
    }

    private UsageStatusResponse CreateStatus(ClientUsage usage, bool hasUnlimitedQuota = false)
    {
        var isUnlimited = !_options.Enabled || hasUnlimitedQuota;
        var scanLimit = isUnlimited ? int.MaxValue : Math.Max(0, _options.DailyScanLimit);
        var recipeLimit = isUnlimited ? int.MaxValue : Math.Max(0, _options.DailyRecipeLimit);
        return new UsageStatusResponse(
            !_options.Enabled || _options.AiEnabled,
            NextResetUtc().ToString("O"),
            usage.Scans,
            scanLimit,
            Math.Max(0, scanLimit - usage.Scans),
            usage.Recipes,
            recipeLimit,
            Math.Max(0, recipeLimit - usage.Recipes),
            _options.AllowTestReset,
            isUnlimited);
    }

    private DateTimeOffset NextResetUtc() =>
        new(_currentDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private void ResetIfNeeded()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (today == _currentDay)
        {
            return;
        }

        _currentDay = today;
        _usage.Clear();
        _activeClients.Clear();
        _estimatedSpendUsd = 0;
    }

    private sealed class ClientUsage
    {
        public int Scans { get; set; }
        public int Recipes { get; set; }
    }
}
