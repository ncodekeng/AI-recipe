using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public sealed class AdminSessionService
{
    public const string CookieName = "plate.admin-session";
    private readonly PromptAdminOptions _options;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public AdminSessionService(
        IOptions<PromptAdminOptions> options,
        IDataProtectionProvider dataProtection,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _protector = dataProtection.CreateProtector("PLATE.AdminSession.v1");
        _timeProvider = timeProvider;
    }

    public bool IsConfigured =>
        _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiKey);

    public bool TryAuthenticate(HttpContext context, string suppliedKey)
    {
        if (!IsConfigured || !KeyMatches(suppliedKey))
        {
            return false;
        }

        IssueCookie(context);
        return true;
    }

    public bool IsAuthenticated(HttpContext context)
    {
        if (!IsConfigured ||
            !context.Request.Cookies.TryGetValue(CookieName, out var protectedValue) ||
            string.IsNullOrWhiteSpace(protectedValue))
        {
            return false;
        }

        try
        {
            var values = _protector.Unprotect(protectedValue).Split(':', 2);
            if (values.Length != 2 ||
                !long.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt) ||
                _timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expiresAt)
            {
                return false;
            }

            var expected = KeyFingerprint(_options.ApiKey);
            var supplied = Convert.FromHexString(values[1]);
            return supplied.Length == expected.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private void IssueCookie(HttpContext context)
    {
        var lifetime = TimeSpan.FromHours(Math.Clamp(_options.SessionHours, 1, 24));
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);
        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{expiresAt.ToUnixTimeSeconds()}:{Convert.ToHexString(KeyFingerprint(_options.ApiKey))}");
        context.Response.Cookies.Append(
            CookieName,
            _protector.Protect(payload),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Path = "/",
                MaxAge = lifetime,
                Expires = expiresAt
            });
    }

    private bool KeyMatches(string suppliedKey)
    {
        var expected = KeyFingerprint(_options.ApiKey);
        var supplied = KeyFingerprint(suppliedKey ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static byte[] KeyFingerprint(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
