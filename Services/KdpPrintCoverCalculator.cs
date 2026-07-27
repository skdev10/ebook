using EBookDashboard.Configuration;
using EBookDashboard.Models;

namespace EBookDashboard.Services
{
    /// <summary>
    /// Print cover dimensions: KDP paperback calculator + case-bound hardcover layout.
    /// All numeric values come from <see cref="KdpSpecsAccessor"/>.
    /// </summary>
    public static class KdpPrintCoverCalculator
    {
        private static KdpSpecs S => KdpSpecsAccessor.Current;

        public static double MinSpineInches => S.AlternateCaseLayout.MinSpineInches;
        public static double PaperbackBleedInches => S.BleedIn;
        public static double CaseWrapMarginInches => S.AlternateCaseLayout.WrapMarginInches;
        public static double CaseHingeGapInches => S.AlternateCaseLayout.HingeGapInches;
        public static int MinPages => S.Paperback.MinPages;
        public static int MaxPagesPaperback => S.Paperback.MaxPages;
        public static double CaseSpineInchesPerPage => S.PaperThicknessInPerPage.Cream;

        public sealed class CoverLayoutSpec
        {
            public int PageCount { get; init; }
            public string BindingType { get; init; } = "Paperback";
            public string InteriorType { get; init; } = "Black & white";
            public string PaperType { get; init; } = "White paper";
            public string TrimSizeLabel { get; init; } = "6 x 9 in";
            public double TrimWidthInches { get; init; } = 6;
            public double TrimHeightInches { get; init; } = 9;
            public double SpineInches { get; init; }
            public double SpineMm { get; init; }
            public double WrapWidthInches { get; init; }
            public double WrapHeightInches { get; init; }
            public double WrapWidthMm { get; init; }
            public double WrapHeightMm { get; init; }
            public double SpineWidthInchesPerPage { get; init; }
            public double OuterMarginInches { get; init; }
            public double HingeGapInches { get; init; }
            public double BackPanelXInches { get; init; }
            public double HingeLeftXInches { get; init; }
            public double SpineXInches { get; init; }
            public double HingeRightXInches { get; init; }
            public double FrontPanelXInches { get; init; }
            public double PanelTopYInches { get; init; }
        }

        public static CoverLayoutSpec Calculate(
            int pageCount,
            string? trimSize = null,
            string? paperType = null,
            string? interiorType = null,
            string? bindingType = null)
        {
            var pages = Math.Clamp(pageCount, MinPages, MaxPagesPaperback);
            var binding = NormalizeBinding(bindingType);
            var interior = NormalizeInterior(interiorType);
            var paper = NormalizePaper(paperType);
            var trim = ParseTrim(trimSize);
            var isHardcover = binding == "Hardcover";

            var paperEnum = paper.Contains("Cream", StringComparison.OrdinalIgnoreCase)
                ? PaperType.Cream
                : PaperType.White;
            if (interior.Contains("Premium", StringComparison.OrdinalIgnoreCase))
                paperEnum = PaperType.PremiumColor;
            else if (interior.Contains("Standard", StringComparison.OrdinalIgnoreCase) && paperEnum == PaperType.White)
                paperEnum = PaperType.StandardColor;

            var projectType = isHardcover ? ProjectType.Hardcover : ProjectType.Paperback;
            var calc = new KdpCalculationService(S);
            var spineIn = Math.Max(MinSpineInches, calc.CalculateSpineWidthIn(pages, paperEnum, projectType));
            var perPage = isHardcover
                ? (pages == 0 ? S.Hardcover.SpineCaseExtraIn : spineIn / Math.Max(1, pages))
                : S.ResolveThicknessInPerPage(paperEnum);

            var outerMargin = isHardcover ? CaseWrapMarginInches : PaperbackBleedInches;
            var hingeGap = isHardcover ? CaseHingeGapInches : 0.0;

            var backPanelX = outerMargin;
            var hingeLeftX = backPanelX + trim.W;
            var spineX = hingeLeftX + hingeGap;
            var hingeRightX = spineX + spineIn;
            var frontPanelX = hingeRightX + hingeGap;

            var wrapWIn = frontPanelX + trim.W + outerMargin;
            var wrapHIn = outerMargin * 2 + trim.H;
            var panelTopY = outerMargin;

            return new CoverLayoutSpec
            {
                PageCount = pages,
                BindingType = binding,
                InteriorType = interior,
                PaperType = paper,
                TrimSizeLabel = trim.Label,
                TrimWidthInches = trim.W,
                TrimHeightInches = trim.H,
                SpineInches = spineIn,
                SpineMm = spineIn * 25.4,
                WrapWidthInches = wrapWIn,
                WrapHeightInches = wrapHIn,
                WrapWidthMm = wrapWIn * 25.4,
                WrapHeightMm = wrapHIn * 25.4,
                SpineWidthInchesPerPage = perPage,
                OuterMarginInches = outerMargin,
                HingeGapInches = hingeGap,
                BackPanelXInches = backPanelX,
                HingeLeftXInches = hingeLeftX,
                SpineXInches = spineX,
                HingeRightXInches = hingeRightX,
                FrontPanelXInches = frontPanelX,
                PanelTopYInches = panelTopY
            };
        }

        public static string BuildCohesiveCoverStyleDirective(string baseStyle, CoverLayoutSpec dims)
        {
            var style = (baseStyle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(style))
                style = "Premium market-ready wrap cover with elegant typography and rich layered background.";

            var layoutNote = dims.BindingType == "Hardcover"
                ? $"Case-bound layout: {dims.OuterMarginInches:F2} in wrap margin, {dims.HingeGapInches:F3} in hinge joint each side of spine, spine board {dims.SpineInches:F3} in for {dims.PageCount} pages."
                : $"Paperback layout: {dims.OuterMarginInches:F3} in bleed, spine {dims.SpineInches:F3} in for {dims.PageCount} pages.";

            var cohesion = string.Join(" ",
                "MANDATORY PRINT WRAP RULES:",
                "Create ONE continuous wraparound design (back + spine + front) with identical palette, textures, gradients, and ornamental language on all three panels.",
                "The back cover MUST repeat the same background colors and decorative frame system as the front — never a different color scheme on the back.",
                "The spine strip must visually continue the front/back background seamlessly across the exact spine width.",
                layoutNote,
                $"Full cover canvas: {dims.WrapWidthInches:F3} in × {dims.WrapHeightInches:F3} in.",
                "Panel order left-to-right: back cover, hinge joint, spine, hinge joint, front cover.",
                "No unrelated artwork on the back; back continues the front design with synopsis-safe space.");

            return style.Contains("MANDATORY PRINT WRAP", StringComparison.OrdinalIgnoreCase)
                ? style
                : style + " " + cohesion;
        }

        private static (double W, double H, string Label) ParseTrim(string? trimSize)
        {
            var src = (trimSize ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            foreach (var t in S.TrimPresets)
            {
                var key = t.Key.Replace(" ", "").ToLowerInvariant();
                if (src.Contains(key) || (Math.Abs(t.WidthIn - 6) < 0.01 && src.Contains("6") && src.Contains("9") && t.Key == "6x9"))
                    return (t.WidthIn, t.HeightIn, t.Label);
            }

            var def = S.TrimPresets.FirstOrDefault(t => t.IsDefault) ?? S.TrimPresets.FirstOrDefault(t => t.Key == "6x9");
            return def != null
                ? (def.WidthIn, def.HeightIn, def.Label)
                : (6, 9, "6 x 9 in");
        }

        private static string NormalizeBinding(string? value)
        {
            var v = (value ?? "").Trim();
            return v.Equals("Hardcover", StringComparison.OrdinalIgnoreCase) ? "Hardcover" : "Paperback";
        }

        private static string NormalizeInterior(string? value)
        {
            var v = (value ?? "").Trim();
            if (v.Contains("premium", StringComparison.OrdinalIgnoreCase) && v.Contains("color", StringComparison.OrdinalIgnoreCase))
                return "Premium color";
            if (v.Contains("color", StringComparison.OrdinalIgnoreCase))
                return "Standard color";
            return "Black & white";
        }

        private static string NormalizePaper(string? value)
        {
            var v = (value ?? "").Trim();
            if (v.Contains("cream", StringComparison.OrdinalIgnoreCase))
                return "Cream paper";
            return "White paper";
        }
    }
}
