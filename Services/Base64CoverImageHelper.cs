namespace EBookDashboard.Services
{
    /// <summary>
    /// Decodes API cover responses: raw Base64 or <c>data:image/...;base64,...</c> data URLs.
    /// Use <see cref="TryDecodeToImageBytes"/> for validation + <c>img</c> <c>src</c>; use <see cref="TrySaveToWebRoot"/> to persist under wwwroot.
    /// </summary>
    public static class Base64CoverImageHelper
    {
        public sealed record DecodeResult(bool Success, byte[]? Bytes, string ContentType, string? DataUrl, string? Error)
        {
            public static DecodeResult Fail(string error) =>
                new(false, null, "application/octet-stream", null, error);
        }

        /// <summary>
        /// Decodes <paramref name="encodedImageOrDataUrl"/> into image bytes and a browser-safe data URL for &lt;img src&gt;.
        /// </summary>
        /// <param name="encodedImageOrDataUrl">Raw Base64, or full data URL with comma separator.</param>
        public static DecodeResult TryDecodeToImageBytes(string? encodedImageOrDataUrl)
        {
            if (string.IsNullOrWhiteSpace(encodedImageOrDataUrl))
                return DecodeResult.Fail("Input is null or empty.");

            var s = encodedImageOrDataUrl.Trim();
            string declaredMime = "image/png";
            string b64 = s;

            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = s.IndexOf(',', StringComparison.Ordinal);
                if (comma <= 0)
                    return DecodeResult.Fail("Invalid data URL: missing comma before payload.");

                var header = s[..comma];
                var mimeEnd = header.IndexOf(';', StringComparison.Ordinal);
                if (mimeEnd > 5)
                    declaredMime = header[5..mimeEnd].Trim();
                if (string.IsNullOrEmpty(declaredMime))
                    declaredMime = "image/png";

                b64 = s[(comma + 1)..].Trim();
            }

            b64 = new string(b64.Where(c => !char.IsWhiteSpace(c)).ToArray());
            b64 = PadBase64(b64);

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                return DecodeResult.Fail("Invalid Base64: decoding failed.");
            }

            if (bytes.Length == 0)
                return DecodeResult.Fail("Decoded buffer is empty.");

            var sniffed = SniffImageContentType(bytes);
            var contentType = IsGenericImageMime(declaredMime) ? declaredMime : sniffed;
            var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";

            return new DecodeResult(true, bytes, contentType, dataUrl, null);
        }

        /// <summary>
        /// Writes bytes to <paramref name="webRootPath"/>/<paramref name="relativeFolder"/>/<paramref name="fileName"/> and returns a URL path starting with '/'.
        /// </summary>
        public static string? TrySaveToWebRoot(byte[] bytes, string webRootPath, string relativeFolder, string fileName)
        {
            if (bytes == null || bytes.Length == 0) return null;
            if (string.IsNullOrWhiteSpace(webRootPath)) return null;

            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(safeName)) return null;

            relativeFolder = relativeFolder.Trim().Replace('\\', '/').Trim('/');
            var dir = Path.Combine(webRootPath, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            var full = Path.Combine(dir, safeName);
            File.WriteAllBytes(full, bytes);
            return "/" + relativeFolder + "/" + safeName;
        }

        private static string PadBase64(string s)
        {
            var pad = s.Length % 4;
            if (pad == 0) return s;
            return s + (pad == 2 ? "==" : pad == 3 ? "=" : "");
        }

        private static bool IsGenericImageMime(string mime)
        {
            return mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                   && !mime.Equals("image/octet-stream", StringComparison.OrdinalIgnoreCase);
        }

        private static string SniffImageContentType(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return "image/webp";
            if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return "image/gif";
            return "image/png";
        }
    }
}
