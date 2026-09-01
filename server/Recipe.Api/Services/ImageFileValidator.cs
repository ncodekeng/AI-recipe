namespace Recipe.Api.Services;

internal static class ImageFileValidator
{
    public static bool TryDetectContentType(ReadOnlySpan<byte> content, out string contentType)
    {
        if (content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            contentType = "image/jpeg";
            return true;
        }

        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            contentType = "image/png";
            return true;
        }

        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
        {
            contentType = "image/gif";
            return true;
        }

        if (content.Length >= 12 &&
            content[..4].SequenceEqual("RIFF"u8) &&
            content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            contentType = "image/webp";
            return true;
        }

        contentType = string.Empty;
        return false;
    }
}
