using Microsoft.OpenApi.Models;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using EBookDashboard.Services;
using EBookDashboard.Services.BookApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System;
using EBookDashboard.Filters;
using EBookDashboard.Hubs;
using EBookDashboard.Infrastructure;
using EBookDashboard.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
// Optional local overrides (secrets); never commit — see DigitalOcean-EnvironmentVariables.txt for production env vars.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

var urlsCfgEarly = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? builder.Configuration["Urls"]
    ?? string.Empty;
var httpsEndpointsConfigured = urlsCfgEarly.Contains("https://", StringComparison.OrdinalIgnoreCase);

var persistDir = Environment.GetEnvironmentVariable("EBOOKAI_PERSIST_DIR")?.Trim();
var dataProtectionKeysPath = !string.IsNullOrEmpty(persistDir)
    ? Path.Combine(persistDir, "DataProtection-Keys")
    : Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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

builder.Services.AddRequestTimeouts(options =>
{
    options.AddPolicy("CoverGeneration", TimeSpan.FromMinutes(60));
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

var isDevelopmentEnvironment = builder.Environment.IsDevelopment();

// Auth + session cookies: Production used CookieSecurePolicy.Always, which breaks login on plain HTTP
// (e.g. http://IP:5000) — browsers drop Secure cookies. SameAsRequest marks Secure only on HTTPS requests.
static CookieSecurePolicy ResolveAuthCookieSecurePolicy(IConfiguration configuration)
{
    var s = configuration["Authentication:CookieSecurePolicy"]?.Trim();
    if (string.Equals(s, "Always", StringComparison.OrdinalIgnoreCase))
        return CookieSecurePolicy.Always;
    if (string.Equals(s, "None", StringComparison.OrdinalIgnoreCase))
        return CookieSecurePolicy.None;
    return CookieSecurePolicy.SameAsRequest;
}

var authCookieSecurePolicy = ResolveAuthCookieSecurePolicy(builder.Configuration);

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
    options.ExpireTimeSpan = TimeSpan.FromMinutes(120);
    options.SlidingExpiration = true;
    options.Cookie.SecurePolicy = authCookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Events.OnRedirectToLogin = AuthRedirectHelper.RedirectToLoginOrUnauthorized;
})
.AddCookie("UserCookie", options =>
{
    options.Cookie.Name = ".AspNetCore.User";
    options.LoginPath = "/Account/UserLogin";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(120);
    options.SlidingExpiration = true;
    options.Cookie.SecurePolicy = authCookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Events.OnRedirectToLogin = AuthRedirectHelper.RedirectToLoginOrUnauthorized;
});

// OAuth: read from Authentication:*, Google:*, Facebook:*, User Secrets, appsettings.Local.json, and common env var names.
var googleClientId = OAuthCredentialResolver.GoogleClientId(builder.Configuration);
var googleClientSecret = OAuthCredentialResolver.GoogleClientSecret(builder.Configuration);
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
    Console.WriteLine("[OAuth] Google handlers not registered: missing ClientId and/or ClientSecret in configuration.");
}

var facebookAppId = OAuthCredentialResolver.FacebookAppId(builder.Configuration);
var facebookAppSecret = OAuthCredentialResolver.FacebookAppSecret(builder.Configuration);
if (!string.IsNullOrWhiteSpace(facebookAppId) && !string.IsNullOrWhiteSpace(facebookAppSecret))
{
    authenticationBuilder.AddFacebook(options =>
    {
        options.AppId = facebookAppId.Trim();
        options.AppSecret = facebookAppSecret.Trim();
        options.CallbackPath = builder.Configuration["Authentication:Facebook:CallbackPath"] ?? "/signin-facebook";
        options.Scope.Add("email");
        options.Fields.Add("email");
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                ?.CreateLogger("FacebookOAuth");
            logger?.LogWarning(context.Failure, "Facebook sign-in remote failure.");
            context.HandleResponse();
            context.Response.Redirect("/Account/UserLogin?error=oauth_failed");
            return Task.CompletedTask;
        };
    });
}
else
{
    Console.WriteLine("[OAuth] Facebook handlers not registered: missing AppId and/or AppSecret in configuration.");
}

// ✅ Authorization middleware (roles, policies etc.)
builder.Services.AddAuthorization();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
// Add EmailService
builder.Services.AddScoped<IEmailService, EmailService>();
// Add BookService
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<BookPublishReadinessService>();
builder.Services.AddScoped<BookFlowStateService>();
builder.Services.AddScoped<IEditorDraftResetService, EditorDraftResetService>();
builder.Services.AddScoped<ISpineCalculatorService, SpineCalculatorService>();
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
// OpenAIService2 disabled — voice uses ExternalApi:AudioUrl only.
builder.Services.AddScoped<CommonMethodsService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddSingleton<EBookDashboard.Services.PdfExport.PdfHtmlExportServiceResolver>();
builder.Services.AddScoped<IBookPdfService, BookPdfService>();
builder.Services.AddScoped<IEpubExportService, EpubExportService>();
builder.Services.AddScoped<IDocxExportService, DocxExportService>();
builder.Services.AddScoped<IBookPageMetricsService, BookPageMetricsService>();
builder.Services.AddScoped<IChapterIterationService, ChapterIterationService>();
builder.Services.Configure<ChapterGenerationOptions>(
    builder.Configuration.GetSection(ChapterGenerationOptions.SectionName));
builder.Services.Configure<MobileAccessOptions>(
    builder.Configuration.GetSection(MobileAccessOptions.SectionName));
builder.Services.Configure<BookPaymentOptions>(
    builder.Configuration.GetSection(BookPaymentOptions.SectionName));
builder.Services.AddBookUpstreamHttpClients(builder.Configuration);
builder.Services.AddScoped<IUpstreamQueueProbe, UpstreamQueueProbe>();
builder.Services.AddHealthChecks()
    .AddCheck<UpstreamBookApiHealthCheck>("upstream_book_api", failureStatus: HealthStatus.Degraded, tags: ["ready"]);
builder.Services.AddScoped<IBookChapterPipelineService, BookChapterPipelineService>();

// Add session services
builder.Services.AddDistributedMemoryCache();
//Register HttpClient factory
builder.Services.AddHttpClient();
//Register Sessions
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(120); // long AI edit / generate waits (was 30 — caused save failures after idle)
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = authCookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
// ✅ Swagger for API documentation
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "EBookDashboard API",
        Version = "v1",
        Description = "API for EBookDashboard including Amazon KDP paperback cover calculator (POST /api/kdp/calculate)."
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
builder.Services.AddSingleton<EBookDashboard.Application.Kdp.Interfaces.IKdpCoverDimensionService,
    EBookDashboard.Application.Kdp.Services.KdpCoverDimensionService>();
builder.Services.AddSingleton<EBookDashboard.Services.CoverCalculator>();
builder.Services.AddSingleton<EBookDashboard.Services.CoverConformer>();

// AJAX profile/password: allow antiforgery token in header (must match client: RequestVerificationToken)
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

var stripeSecretKey = StripeKeys.Secret(builder.Configuration);
if (!string.IsNullOrWhiteSpace(stripeSecretKey))
    Stripe.StripeConfiguration.ApiKey = stripeSecretKey.Trim();

var app = builder.Build();

{
    using var scope = app.Services.CreateScope();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var cfgForStartup = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var extOpt = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalApiOptions>>().Value;
    var hasBase = !string.IsNullOrWhiteSpace(extOpt.BaseUrl)
                  || !string.IsNullOrWhiteSpace(extOpt.GenerateUrl);
    var hasKey = !string.IsNullOrWhiteSpace(ExternalApiKeyResolver.Resolve(cfgForStartup));
    startupLogger.LogInformation(
        "External API: BaseUrl or per-endpoint URLs configured={HasBase}, upstream API credential configured={HasKey}",
        hasBase,
        hasKey);
    var gOk = !string.IsNullOrWhiteSpace(OAuthCredentialResolver.GoogleClientId(cfgForStartup))
              && !string.IsNullOrWhiteSpace(OAuthCredentialResolver.GoogleClientSecret(cfgForStartup));
    var fOk = !string.IsNullOrWhiteSpace(OAuthCredentialResolver.FacebookAppId(cfgForStartup))
              && !string.IsNullOrWhiteSpace(OAuthCredentialResolver.FacebookAppSecret(cfgForStartup));
    startupLogger.LogInformation(
        "OAuth handlers: Google={GoogleOk}, Facebook={FacebookOk}.",
        gOk,
        fOk);

    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await db.Database.CloseConnectionAsync();
        startupLogger.LogInformation("MySQL connection OK.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "MySQL connection FAILED at startup — login and dashboard will not work. Error: {Message}", ex.Message);
    }
}

app.UseForwardedHeaders();
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
    if (httpsEndpointsConfigured)
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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "EBookDashboard API v1"));
}

if (httpsEndpointsConfigured)
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/health"),
        appBuilder => appBuilder.UseHttpsRedirection());
}
app.UseStaticFiles();

app.UseRouting();

app.UseMiddleware<EBookDashboard.Middleware.MobileBlockMiddleware>();

app.UseRequestTimeouts();

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
    pattern: "{controller=Account}/{action=UserLogin}/{id?}");

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
app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false,
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
}).AllowAnonymous();

app.Run();