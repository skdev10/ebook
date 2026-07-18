using Newtonsoft.Json.Linq;
using System.Linq;

namespace EBookDashboard.Services
{
    /// <summary>Shared parsing / style mapping for external Python cover APIs (generate-cover, edit-cover).</summary>
    public static class CoverExternalApiHelper
    {
        public sealed class CoverAssetBundle
        {
            public string Front { get; set; } = "";
            public string Spine { get; set; } = "";
            public string Back { get; set; } = "";
            public string Wrap { get; set; } = "";
        }

        /// <summary>
        /// Appended to every non-cartoon style so the image API renders photorealistic covers.
        /// Without this the model drifts toward flat/cartoon illustration (word "illustration"
        /// or genre alone was enough to produce cartoonish fronts on production).
        /// </summary>
        public const string RealisticStyleDirective =
            "PHOTOREALISTIC ONLY: ultra-detailed professional book cover photography, shot on a professional camera, "
            + "cinematic lighting, realistic materials and textures, natural skin and surfaces, depth of field, 8k detail. "
            + "STRICTLY FORBIDDEN: cartoon, anime, comic, manga, flat vector illustration, clipart, 3D cartoon render, "
            + "childish drawing, doodle, sticker art, chibi, caricature. The result must look like a real photograph "
            + "or cinematic movie poster, never an illustration.";

        public static string MapCoverStyleForExternalApi(string styleKey, string? imageDirection)
        {
            var k = (styleKey ?? "modern").Trim().ToLowerInvariant();
            var label = k switch
            {
                "minimal" => "Minimalist, clean photographic composition",
                "bold" => "Bold typography over a striking photorealistic image",
                "elegant" => "Elegant, refined, cinematic photographic style",
                "modern" => "Modern, sleek, photorealistic design",
                "vintage" => "Vintage, aged realistic photographic style",
                "cartoon" => "Children's book cartoon style, bright bold colors, thick outlines, playful bubbly typography, fun illustrated background",
                _ => styleKey.Trim()
            };

            // Cartoon is the only style the user can explicitly opt into for illustrated art;
            // everything else must stay realistic.
            if (k != "cartoon")
                label = $"{label}. {RealisticStyleDirective}";

            var dir = (imageDirection ?? "").Trim();
            if (string.IsNullOrEmpty(dir)) return label;
            if (dir.Length > 400) dir = dir.Substring(0, 400) + "…";
            return $"{label} Visual direction: {dir}";
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

                var fullCoverB64 = jo["full_cover_base64"]?.ToString();
                if (!string.IsNullOrEmpty(fullCoverB64))
                {
                    AddUrl(fullCoverB64.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                        ? fullCoverB64
                        : "data:image/png;base64," + fullCoverB64);
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

        public static CoverAssetBundle ExtractNamedCoverAssetsFromApiResponse(string? responseData)
        {
            var bundle = new CoverAssetBundle();
            if (string.IsNullOrWhiteSpace(responseData)) return bundle;

            JToken root;
            try { root = JToken.Parse(responseData); }
            catch { return bundle; }

            if (root is not JObject jo) return bundle;
            var all = new List<JObject> { jo };
            if (jo["data"] is JObject data) all.Add(data);

            foreach (var obj in all)
            {
                if (obj["panels"] is JObject panels)
                {
                    bundle.Front = FirstNonEmpty(bundle.Front,
                        NormalizeImageRef(panels["front"]?.ToString()),
                        NormalizeImageRef(panels["front_cover"]?.ToString()));
                    bundle.Back = FirstNonEmpty(bundle.Back,
                        NormalizeImageRef(panels["back"]?.ToString()),
                        NormalizeImageRef(panels["back_cover"]?.ToString()));
                    bundle.Spine = FirstNonEmpty(bundle.Spine,
                        NormalizeImageRef(panels["spine"]?.ToString()),
                        NormalizeImageRef(panels["spine_cover"]?.ToString()));
                }

                if (obj["cover"] is JObject coverObj)
                {
                    bundle.Front = FirstNonEmpty(bundle.Front,
                        NormalizeImageRef(coverObj["front"]?.ToString()),
                        NormalizeImageRef(coverObj["front_cover"]?.ToString()));
                    bundle.Back = FirstNonEmpty(bundle.Back,
                        NormalizeImageRef(coverObj["back"]?.ToString()),
                        NormalizeImageRef(coverObj["back_cover"]?.ToString()));
                    bundle.Spine = FirstNonEmpty(bundle.Spine,
                        NormalizeImageRef(coverObj["spine"]?.ToString()),
                        NormalizeImageRef(coverObj["spine_cover"]?.ToString()));
                    bundle.Wrap = FirstNonEmpty(bundle.Wrap,
                        NormalizeImageRef(coverObj["wrap"]?.ToString()),
                        NormalizeImageRef(coverObj["full_wrap"]?.ToString()));
                }

                bundle.Front = FirstNonEmpty(bundle.Front,
                    NormalizeImageRef(obj["front"]?.ToString()),
                    NormalizeImageRef(obj["front_cover"]?.ToString()),
                    NormalizeImageRef(obj["frontCover"]?.ToString()),
                    NormalizeImageRef(obj["front_image"]?.ToString()));

                bundle.Back = FirstNonEmpty(bundle.Back,
                    NormalizeImageRef(obj["back"]?.ToString()),
                    NormalizeImageRef(obj["back_cover"]?.ToString()),
                    NormalizeImageRef(obj["backCover"]?.ToString()),
                    NormalizeImageRef(obj["back_image"]?.ToString()));

                bundle.Spine = FirstNonEmpty(bundle.Spine,
                    NormalizeImageRef(obj["spine"]?.ToString()),
                    NormalizeImageRef(obj["spine_cover"]?.ToString()),
                    NormalizeImageRef(obj["spineCover"]?.ToString()),
                    NormalizeImageRef(obj["spine_image"]?.ToString()));

                bundle.Wrap = FirstNonEmpty(bundle.Wrap,
                    NormalizeImageRef(obj["wrap"]?.ToString()),
                    NormalizeImageRef(obj["full_wrap"]?.ToString()),
                    NormalizeImageRef(obj["full_cover"]?.ToString()),
                    NormalizeImageRef(obj["full_cover_base64"]?.ToString()),
                    NormalizeImageRef(obj["cover_url"]?.ToString()),
                    NormalizeImageRef(obj["image_url"]?.ToString()),
                    NormalizeImageRef(obj["url"]?.ToString()));
            }

            var fallbacks = ExtractCoverImageUrlsFromApiResponse(responseData);
            // Never assign the same single image as BOTH wrap and front — a portrait front
            // stuffed into the wrap slot is what makes Ebook+Paperback covers look "weird".
            if (string.IsNullOrWhiteSpace(bundle.Wrap) && fallbacks.Count > 0)
            {
                // Prefer a landscape-looking distinct URL when front is already filled.
                var wrapCandidate = fallbacks.FirstOrDefault(u =>
                    !string.Equals(u, bundle.Front, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(wrapCandidate))
                    bundle.Wrap = wrapCandidate;
                else if (string.IsNullOrWhiteSpace(bundle.Front))
                    bundle.Wrap = fallbacks[0];
                // else: leave Wrap empty — caller must use local compositor from the front.
            }
            if (string.IsNullOrWhiteSpace(bundle.Front) && fallbacks.Count > 0)
            {
                var frontCandidate = fallbacks.FirstOrDefault(u =>
                    !string.Equals(u, bundle.Wrap, StringComparison.OrdinalIgnoreCase));
                bundle.Front = !string.IsNullOrWhiteSpace(frontCandidate) ? frontCandidate : fallbacks[0];
            }

            return bundle;
        }

        public static string NormalizeImageRef(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.Trim();
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("/", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
                return s;
            if (LooksLikeBase64(s))
                return "data:image/png;base64," + s;
            return "";
        }

        private static bool LooksLikeBase64(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.Length < 40) return false;
            return s.All(ch => char.IsLetterOrDigit(ch) || ch is '+' or '/' or '=' or '-' or '_');
        }

        private static string FirstNonEmpty(params string[] candidates)
            => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";

        public static List<string> Dedupe(List<string> urls) =>
            urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Best-effort user-facing message from a non-success upstream JSON body.</summary>
        public static string? TryExtractErrorMessage(string? responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return null;
            try
            {
                if (JToken.Parse(responseData) is not JObject jo) return null;
                foreach (var key in new[] { "message", "error", "detail", "details", "reason" })
                {
                    var val = jo[key]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
            catch
            {
                // ignore malformed JSON
            }

            return null;
        }
    }
}
