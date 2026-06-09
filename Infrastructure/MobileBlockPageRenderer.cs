using System.Net;
using System.Text;
using EBookDashboard.Models;

namespace EBookDashboard.Infrastructure;

/// <summary>Renders the standalone desktop-only block page returned by middleware.</summary>
public static class MobileBlockPageRenderer
{
    /// <summary>Builds a complete HTML document for mobile-blocked visitors.</summary>
    public static string Render(MobileAccessOptions options)
    {
        var title = WebUtility.HtmlEncode(options.Title);
        var message = WebUtility.HtmlEncode(options.Message);
        var subMessage = WebUtility.HtmlEncode(options.SubMessage);

        var sb = new StringBuilder(2048);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.Append("<meta name=\"robots\" content=\"noindex\" />");
        sb.Append("<title>").Append(title).Append(" - EBookDashboard</title>");
        sb.Append("<style>");
        sb.Append("""
            *{box-sizing:border-box;margin:0;padding:0}
            body{min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px;
            font-family:Segoe UI,system-ui,-apple-system,sans-serif;background:#f5f3ff;color:#1e1b4b}
            .card{max-width:440px;width:100%;background:#fff;border-radius:16px;padding:32px 28px;
            box-shadow:0 10px 40px rgba(79,70,229,.12);text-align:center;border:1px solid #e0e7ff}
            .icon{width:56px;height:56px;margin:0 auto 20px;border-radius:14px;background:#eef2ff;
            display:flex;align-items:center;justify-content:center;font-size:26px}
            h1{font-size:1.35rem;font-weight:600;margin-bottom:12px;color:#312e81}
            p{font-size:.98rem;line-height:1.55;color:#4338ca;margin-bottom:8px}
            .sub{color:#64748b;font-size:.92rem}
            .brand{margin-top:24px;font-size:.8rem;color:#94a3b8;letter-spacing:.04em}
            """);
        sb.Append("</style></head><body><main class=\"card\" role=\"main\">");
        sb.Append("<div class=\"icon\" aria-hidden=\"true\">&#128187;</div>");
        sb.Append("<h1>").Append(title).Append("</h1>");
        sb.Append("<p>").Append(message).Append("</p>");
        sb.Append("<p class=\"sub\">").Append(subMessage).Append("</p>");
        sb.Append("<p class=\"brand\">EBookDashboard</p>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}
