using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services
{
    /// <summary>Shared parsing / style mapping for external Python cover APIs (generate-cover, edit-cover).</summary>
    public static class CoverExternalApiHelper
    {
        public static string MapCoverStyleForExternalApi(string styleKey, string? imageDirection)
        {
            var k = (styleKey ?? "modern").Trim().ToLowerInvariant();
            var label = k switch
            {
                "minimal" => "Minimalist",
                "bold" => "Bold Typography",
                "elegant" => "Elegant",
                "modern" => "Modern Illustration",
                "vintage" => "Vintage",
                _ => styleKey.Trim()
            };
            var dir = (imageDirection ?? "").Trim();
            if (string.IsNullOrEmpty(dir)) return label;
            if (dir.Length > 400) dir = dir.Substring(0, 400) + "…";
            return $"{label}. Visual direction: {dir}";
        }

        public static List<string> ExtractCoverImageUrlsFromApiResponse(string? responseData)
        {
            var urls = new List<string>();
            if (string.IsNullOrWhiteSpace(responseData)) return urls;

            void AddUrl(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                s = s.Trim().Trim('"');
                if (s.Length == 0) return;
                if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
                    urls.Add(s);
            }

            void WalkArray(JArray? arr)
            {
                if (arr == null) return;
                foreach (var item in arr)
                {
                    if (item == null || item.Type == JTokenType.Null) continue;
                    if (item.Type == JTokenType.String) AddUrl(item.ToString());
                    else if (item is JObject o)
                    {
                        AddUrl(o["url"]?.ToString());
                        AddUrl(o["image_url"]?.ToString());
                        AddUrl(o["href"]?.ToString());
                        AddUrl(o["path"]?.ToString());
                        AddUrl(o["src"]?.ToString());
                        var ib = o["image_base64"]?.ToString() ?? o["base64"]?.ToString();
                        if (!string.IsNullOrEmpty(ib))
                            AddUrl(ib.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? ib : "data:image/png;base64," + ib);
                    }
                }
            }

            JToken root;
            try { root = JToken.Parse(responseData); }
            catch { return urls; }

            if (root.Type == JTokenType.String)
            {
                AddUrl(root.ToString());
                return urls;
            }

            if (root is JArray ja) { WalkArray(ja); return Dedupe(urls); }

            if (root is JObject jo)
            {
                var st = jo["status"]?.ToString();
                var ib64 = jo["image_base64"]?.ToString();
                if (string.Equals(st, "success", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ib64))
                {
                    AddUrl(ib64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? ib64 : "data:image/png;base64," + ib64);
                }

                WalkArray(jo["options"] as JArray);
                WalkArray(jo["urls"] as JArray);
                WalkArray(jo["images"] as JArray);
                WalkArray(jo["results"] as JArray);
                WalkArray(jo["covers"] as JArray);
                WalkArray(jo["data"] as JArray);
                if (jo["data"] is JObject djo)
                {
                    WalkArray(djo["options"] as JArray);
                    AddUrl(djo["url"]?.ToString());
                }
                AddUrl(jo["url"]?.ToString());
                AddUrl(jo["image_url"]?.ToString());
                AddUrl(jo["cover_url"]?.ToString());
                var b64 = jo["image"]?.ToString() ?? jo["encoded_image"]?.ToString();
                if (!string.IsNullOrEmpty(b64))
                    AddUrl(b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? b64 : "data:image/png;base64," + b64);
            }

            return Dedupe(urls);
        }

        public static List<string> Dedupe(List<string> urls) =>
            urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
