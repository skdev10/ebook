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

var builder = WebApplication.CreateBuilder(args);

// Long-running AI chapter generation: keep connection alive longer than default (~2 min).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

// ✅ Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    // Global authorization policy: require authenticated users by default
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
})
.AddRazorRuntimeCompilation()
.AddJsonOptions(o => o.JsonSerializerOptions.PropertyNameCaseInsensitive = true);
builder.Services.AddSession();  // ✅ add session support

// ✅ Configure MySQL DbContext
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
    throw new InvalidOperationException("Connection string 'DefaultConnection' is missing. Set it in appsettings.json, appsettings.Development.json, or user secrets.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        defaultConnection,
        new MySqlServerVersion(new Version(8, 0, 34)),
        mysql => mysql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

// ✅ Dual cookie schemes so Admin and User can be logged in in different tabs simultaneously.
// Path-based: /Admin/* uses AdminCookie, everything else uses UserCookie.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Cookies";
    options.DefaultSignInScheme = "Cookies";
    options.DefaultChallengeScheme = "Cookies";
})
.AddCookie("Cookies", options =>
{
    options.ForwardDefaultSelector = ctx =>
        ctx.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase) ? "AdminCookie" : "UserCookie";
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
})
.AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "";
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
        options.CallbackPath = builder.Configuration["Authentication:Google:CallbackPath"] ?? "/signin-google";
        // Google includes email/profile scopes by default
    })
    .AddFacebook(options =>
    {
        options.AppId = builder.Configuration["Authentication:Facebook:AppId"] ?? "";
        options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"] ?? "";
        options.CallbackPath = builder.Configuration["Authentication:Facebook:CallbackPath"] ?? "/signin-facebook";
        // Ensure email is requested from Facebook
        options.Scope.Add("email");
        options.Fields.Add("email");
    });

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
builder.Services.AddScoped<IAuthorPlansService, AuthorPlansService>();
//builder.Services.AddScoped<OpenAIService2>();
builder.Services.AddScoped<OpenAIService2>();
builder.Services.AddScoped<CommonMethodsService>(); // ✅ Add this line
builder.Services.AddScoped<IAPIRawResponseService, APIRawResponseService>();
builder.Services.AddScoped<IBookService, BookService>();
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

    // Enable file upload in Swagger
    c.OperationFilter<SwaggerFileOperationFilter>();
});

builder.Services.AddSwaggerGen(c =>
{
    c.OperationFilter<SwaggerFileOperationFilter>();
});
// Register services
builder.Services.AddScoped<IAudioToTextService, AudioToTextService>();
// Add services to the container for ChatGPT in AIGenerateBook
builder.Services.AddControllersWithViews();
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

var app = builder.Build();
// Serve book cover images from Images/book_covers at /book-covers
var bookCoversPath = Path.Combine(app.Environment.ContentRootPath, "Images", "book_covers");
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

app.UseHttpsRedirection();
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
    context.Response.Redirect("/Account/Login");
    return System.Threading.Tasks.Task.CompletedTask;
});

app.Run();