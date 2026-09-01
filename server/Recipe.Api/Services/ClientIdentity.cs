using System.Security.Cryptography;
using System.Text;

namespace Recipe.Api.Services;

internal static class ClientIdentity
{
    private const string HeaderName = "X-Plate-Client-Id";

    public static string Resolve(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        var raw = IsValid(supplied)
            ? $"client:{supplied}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool IsValid(string? value) =>
        value is { Length: >= 16 and <= 128 } &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
}
