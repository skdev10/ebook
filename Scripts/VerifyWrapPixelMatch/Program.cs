using System.Security.Cryptography;
using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Interfaces;
using EBookDashboard.Application.Kdp.Services;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// Pixel-verifies that wrap front panel matches standalone front cover (cover-fill only).
/// Usage: dotnet run --project Scripts/VerifyWrapPixelMatch -- &lt;front-cover.png&gt;
/// </summary>
var frontPath = args.Length > 0
    ? args[0]
    : @"C:\Users\Hp\.cursor\projects\c-Users-Hp-Desktop-newEbook\assets\c__Users_Hp_AppData_Roaming_Cursor_User_workspaceStorage_2770b44ea50ae9f9ad122d67a01a3212_images_sharukh-khan-221-front-cover-d23039db-4a82-4d05-91fb-7611272ca6f1.png";

if (!File.Exists(frontPath))
{
    Console.Error.WriteLine("Front cover file not found: " + frontPath);
    return 1;
}

var frontBytes = await File.ReadAllBytesAsync(frontPath);
var frontHash = Convert.ToHexString(SHA256.HashData(frontBytes));
Console.WriteLine("Front SHA256: " + frontHash);

var layout = new KdpCoverDimensionService().Calculate(new KdpCalculateRequest
{
    PageCount = 40,
    TrimWidth = 6m,
    TrimHeight = 9m,
    Dpi = KdpConstants.Dpi,
    Bleed = true,
    InteriorType = KdpPaperbackConstants.InteriorTypeStandardColor,
    PaperType = KdpPaperbackConstants.PaperTypeWhite
});

var compositor = new PrintWrapCompositor(new SpineRenderer());
var result = compositor.Compose(new PrintWrapComposeRequest
{
    FrontCoverBytes = frontBytes,
    FrontCoverAssetRef = frontPath,
    Layout = layout,
    Title = "SHARUKH KHAN",
    Author = "saad",
    Description = "Test biography description for back cover blurb rendering and barcode zone validation.",
    Theme = BookTheme.FromExportOptions(new BookPdfExportOptions { InteriorStyle = "Classic" })
});

var outDir = Path.Combine(Directory.GetCurrentDirectory(), "publish-check");
Directory.CreateDirectory(outDir);
var wrapOut = Path.Combine(outDir, "verify-wrap-composed.png");
await File.WriteAllBytesAsync(wrapOut, result.PngBytes);
Console.WriteLine("Wrote wrap: " + wrapOut);

using var wrap = Image.Load<Rgba32>(result.PngBytes);
using var extracted = PrintWrapCompositor.ExtractFrontPanel(wrap, result);
using var expected = PrintWrapCompositor.RenderFrontPanelOnly(frontBytes, result, layout.Dpi);

var extractedOut = Path.Combine(outDir, "verify-wrap-front-panel.png");
var expectedOut = Path.Combine(outDir, "verify-expected-front-panel.png");
await extracted.SaveAsPngAsync(extractedOut);
await expected.SaveAsPngAsync(expectedOut);

var mismatches = 0;
for (var y = 0; y < expected.Height; y++)
{
    for (var x = 0; x < expected.Width; x++)
    {
        var a = expected[x, y];
        var b = extracted[x, y];
        if (a.R != b.R || a.G != b.G || a.B != b.B || a.A != b.A)
            mismatches++;
    }
}

Console.WriteLine("Front panel pixels: " + expected.Width + "x" + expected.Height);
Console.WriteLine("Pixel mismatches vs independent cover-fill: " + mismatches);
Console.WriteLine(mismatches == 0 ? "PASS: front panel is pixel-identical to cover-fill of saved front." : "FAIL");
return mismatches == 0 ? 0 : 2;
