using Microsoft.OpenApi.Models;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System;
using EBookDashboard.Filters;
using EBookDashboard.Hubs;
using EBookDashboard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
// Optional local overrides (secrets); never commit — see DigitalOcean-EnvironmentVariables.txt for production env vars.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// DigitalOcean App Platform (and similar) inject PORT; default URLs may not bind correctly in the container.
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv))
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");

// Long-running AI chapter generation: keep connection alive longer than default (~2 min).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

// ✅ Add services to the container.
var mvcBuilder = builder.Services.AddControllersWithViews(options =>
{
    // Global authorization policy: require authenticated users by default
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
})
.AddJsonOptions(o => o.JsonSerializerOptions.PropertyNameCaseInsensitive = true);

// Runtime compilation expects loose .cshtml on disk — publish/Linux typically only ships precompiled views.
if (builder.Environment.IsDevelopment())
    mvcBuilder.AddRazorRuntimeCompilation();
builder.Services.AddSession();  // ✅ add session support

// ✅ Configure MySQL DbContext (optional DO managed DB CA: Certificates/ca-certificate.crt or Database__SslCaPem)
var defaultConnection = MySqlConnectionStringFactory.Build(
    builder.Configuration,
    builder.Environment.ContentRootPath,
    DefaultConnectionResolver.Resolve(builder.Configuration));

builder.Services.AddSingleton<BookLifecycleSaveChangesInterceptor>();
builder.Services.AddSingleton<BookLifecycleAfterSaveService>();

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<BookLifecycleSaveChangesInterceptor>();
    options.UseMySql(
            defaultConnection,
            new MySqlServerVersion(new Version(8, 0, 34)),
            mysql => mysql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null))
        .AddInterceptors(interceptor);
});

builder.Services.AddSignalR();
builder.Services.AddScoped<RequireAdminAuthorizationFilter>();

// ✅ Dual cookie schemes so Admin and User can be logged in in different tabs simultaneously.
// Path-based: /Admin/* uses AdminCookie, everything else uses UserCookie.
var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Cookies";
    options.DefaultSignInScheme = "Cookies";
    options.DefaultChallengeScheme = "Cookies";
})
.AddCookie("Cookies", options =>
{
    options.ForwardDefaultSelector = ctx =>
    {
        var p = ctx.Request.Path;
        if (p.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
            || p.StartsWithSegments("/hubs/admin", StringComparison.OrdinalIgnoreCase))
            return "AdminCookie";
        return "UserCookie";
    };
})
.AddCookie("AdminCookie", options =>
{
    options.Cookie.Name = ".AspNetCore.Admin";
    options.LoginPath = "/Account/AdminLogin";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
})
.AddCookie("UserCookie", options =>
{
    options.Cookie.Name = ".AspNetCore.User";
    options.LoginPath = "/Account/UserLogin";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
});

// OAuth handlers validate ClientId/AppId on first request — skip registration when secrets are missing (e.g. cloud env vars not set).
// Credentials: Authentication:Google:* or top-level Google:* (env: Google__ClientSecret, Authentication__Google__ClientSecret, etc.)
var googleClientId = builder.Configuration["Authentication:Google:ClientId"]
                     ?? builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]
                        ?? builder.Configuration["Google:ClientSecret"];
var googleCallbackPath = builder.Configuration["Authentication:Google:CallbackPath"]
                         ?? builder.Configuration["Google:CallbackPath"]
                         ?? "/auth/google/callback";
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId.Trim();
        options.ClientSecret = googleClientSecret.Trim();
        options.CallbackPath = googleCallbackPath;
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = true;
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                ?.CreateLogger("GoogleOAuth");
            logger?.LogWarning(context.Failure, "Google sign-in remote failure.");
            context.HandleResponse();
            context.Response.Redirect("/Account/UserLogin?error=google_login_failed");
            return Task.CompletedTask;
        };
    });
}
else
{
    Console.WriteLine("[OAuth] Google login disabled: set Authentication:Google:ClientId and Authentication:Google:ClientSecret (e.g. Authentication__Google__ClientId / Authentication__Google__ClientSecret). Client ID alone is not enough for the web OAuth flow.");
}

var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];
if (!string.IsNullOrWhiteSpace(facebookAppId) && !string.IsNullOrWhiteSpace(facebookAppSecret))
{
    authenticationBuilder.AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
        options.CallbackPath = builder.Configuration["Authentication:Facebook:CallbackPath"] ?? "/signin-facebook";
        options.Scope.Add("email");
        options.Fields.Add("email");
    });
}
else
{
    Console.WriteLine("[OAuth] Facebook login disabled: set Authentication:Facebook:AppId and Authentication:Facebook:AppSecret (e.g. Authentication__Facebook__AppId on DigitalOcean).");
}

// ✅ Authorization middleware (roles, policies etc.)
builder.Services.AddAuthorization();
builder.Services.AddScoped<IUserService, UserService>();
// Add EmailService
builder.Services.AddScoped<IEmailService, EmailService>();
// Add BookService
builder.Services.AddScoped<IBookService, BookService>();
// Add PlanService
builder.Services.AddScoped<IPlansService, PlansService>();
// Add FeatureCartService
builder.Services.AddScoped<IFeatureCartService, FeatureCartService>();
// Add CheckoutService
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
// Register Plan Features Service
builder.Services.AddScoped<IPlanFeaturesService, PlanFeaturesService>();
//
builder.Services.AddScoped<IAPIRawResponseService, APIRawResponseService>();
// Add this to your services
builder.Services.AddScoped<BookProcessingService>();
builder.Services.AddScoped<IAuthorPlansService, AuthorPlansService>();
builder.Services.AddScoped<IAuthorBillsService, AuthorBillsService>();
//builder.Services.AddScoped<OpenAIService2>();
builder.Services.AddScoped<OpenAIService2>();
builder.Services.AddScoped<CommonMethodsService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IBookPdfService, BookPdfService>();
builder.Services.AddScoped<IChapterIterationService, ChapterIterationService>();
builder.Services.Configure<ChapterGenerationOptions>(
    builder.Configuration.GetSection(ChapterGenerationOptions.SectionName));
builder.Services.AddHttpClient("ExternalChapterGeneration", (sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var mins = int.TryParse(cfg["ChapterGeneration:HttpTimeoutMinutes"], out var m) ? m : 30;
    mins = Math.Clamp(mins, 1, 120);
    client.Timeout = TimeSpan.FromMinutes(mins);
});
// Cover generation/edit and other multi-minute AI calls — default HttpClient times out at 100s without this.
builder.Services.AddHttpClient("ExternalSlowApi", (sp, client) =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddScoped<IBookChapterPipelineService, BookChapterPipelineService>();

// Add session services
builder.Services.AddDistributedMemoryCache();
//Register HttpClient factory
builder.Services.AddHttpClient();
//Register Sessions
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // session timeout
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
// ✅ Swagger for API documentation
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Audio to Text API",
        Version = "v1",
        Description = "API for converting audio files to text using Azure Cognitive Services"
    });
    c.OperationFilter<SwaggerFileOperationFilter>();
});
// Register services
builder.Services.AddScoped<IAudioToTextService, AudioToTextService>();
builder.Services.Configure<EBookDashboard.Services.OpenAiOptions>(
builder.Configuration.GetSection(EBookDashboard.Services.OpenAiOptions.SectionName));
builder.Services.AddHttpClient<IChatService, ChatService>();

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IBookDesignService, BookDesignService>();

// AJAX profile/password: allow antiforgery token in header (must match client: RequestVerificationToken)
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
if (!string.IsNullOrWhiteSpace(stripeSecretKey))
    Stripe.StripeConfiguration.ApiKey = stripeSecretKey.Trim();

var app = builder.Build();
// Serve book cover images from Images/book_covers at /book-covers
var bookCoversPath = Path.Combine(app.Environment.ContentRootPath, "Images", "book_covers");
Directory.CreateDirectory(bookCoversPath); // publish/container often omits empty folders; PhysicalFileProvider requires an existing root
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(bookCoversPath),
    RequestPath = "/book-covers"
});

// ✅ Middleware pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// ✅ Ensure database exists & apply migrations on startup (creates DB if missing)
//try
//{
//    using var scope = app.Services.CreateScope();
//    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
//    db.Database.Migrate();
//}
//catch (Exception ex)
//{
//    // Optional: log to console to help diagnose startup DB issues (e.g., wrong credentials or server down)
//    Console.WriteLine($"[Startup:Migrate] {ex.GetType().Name}: {ex.Message}");
//}

// HTTPS redirect only when Kestrel actually listens on HTTPS (avoids "Failed to determine the https port" on http-only profiles).
var urlsCfg = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? builder.Configuration["Urls"]
    ?? string.Empty;
var httpsEndpointsConfigured = urlsCfg.Contains("https://", StringComparison.OrdinalIgnoreCase);
if (httpsEndpointsConfigured)
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/health"),
        appBuilder => appBuilder.UseHttpsRedirection());
}
app.UseStaticFiles();

app.UseRouting();

app.UseSession(); // ✅ must be after UseRouting and before UseEndpoints

// ✅ Authentication + Authorization
app.UseAuthentication();
app.UseAuthorization();

// Sync Session from current request identity (Admin vs User cookie) so each tab shows correct user
app.UseMiddleware<EBookDashboard.Middleware.SessionManagementMiddleware>();
//app.MapRazorPages();

// Register attribute routes (e.g. /api/planfeatures from PlanFeaturesController)
app.MapControllers();
app.MapHub<AdminActivityHub>("/hubs/admin").RequireAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

// Role-based dashboard routes
app.MapControllerRoute(
    name: "admin",
    pattern: "Admin/{action=Dashboard}/{id?}",
    defaults: new { controller = "Admin" });

app.MapControllerRoute(
    name: "author",
    pattern: "Author/{action=Dashboard}/{id?}",
    defaults: new { controller = "Author" });

app.MapControllerRoute(
    name: "reader",
    pattern: "Reader/{action=Dashboard}/{id?}",
    defaults: new { controller = "Reader" });

app.MapControllerRoute(
    name: "dashboard",
    pattern: "Dashboard/{action=Index}/{id?}",
    defaults: new { controller = "Dashboard" });

app.MapControllerRoute(
    name: "features",
    pattern: "Features/{action=Index}/{id?}",
    defaults: new { controller = "Features" });

app.MapControllerRoute(
    name: "checkout",
    pattern: "Checkout/{action}/{id?}",
    defaults: new { controller = "Checkout", action = "GetPublishableKey" });

// Fix accidental /Dashboard/undefined by redirecting to Login
app.MapGet("/Dashboard/undefined", (Microsoft.AspNetCore.Http.HttpContext context) =>
{
    context.Response.Redirect("/Account/UserLogin");
    return System.Threading.Tasks.Task.CompletedTask;
});

// Anonymous health for load balancers / App Platform HTTP probes (global MVC auth does not apply here).
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();