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

// Long-running AI chapter generation
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

// ✅ Add services
builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
})
.AddRazorRuntimeCompilation()
.AddJsonOptions(o => o.JsonSerializerOptions.PropertyNameCaseInsensitive = true);

builder.Services.AddSession();

// ✅ DB
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
    throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        defaultConnection,
        new MySqlServerVersion(new Version(8, 0, 34)),
        mysql => mysql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null)));

// ✅ Auth
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Cookies";
    options.DefaultSignInScheme = "Cookies";
    options.DefaultChallengeScheme = "Cookies";
})
.AddCookie("Cookies", options =>
{
    options.ForwardDefaultSelector = ctx =>
        ctx.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
            ? "AdminCookie"
            : "UserCookie";
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
})
.AddFacebook(options =>
{
    options.AppId = builder.Configuration["Authentication:Facebook:AppId"] ?? "";
    options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"] ?? "";
    options.CallbackPath = builder.Configuration["Authentication:Facebook:CallbackPath"] ?? "/signin-facebook";
    options.Scope.Add("email");
    options.Fields.Add("email");
});

// ✅ Authorization
builder.Services.AddAuthorization();

// ✅ Services (same as tumhari original)
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IPlansService, PlansService>();
builder.Services.AddScoped<IFeatureCartService, FeatureCartService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddScoped<IPlanFeaturesService, PlanFeaturesService>();
builder.Services.AddScoped<IAPIRawResponseService, APIRawResponseService>();
builder.Services.AddScoped<BookProcessingService>();
builder.Services.AddScoped<IAuthorPlansService, AuthorPlansService>();
builder.Services.AddScoped<IAuthorBillsService, AuthorBillsService>();
builder.Services.AddScoped<OpenAIService2>();
builder.Services.AddScoped<CommonMethodsService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IBookPdfService, BookPdfService>();
builder.Services.AddScoped<IChapterIterationService, ChapterIterationService>();
builder.Services.AddScoped<IBookChapterPipelineService, BookChapterPipelineService>();
builder.Services.AddScoped<IAudioToTextService, AudioToTextService>();
builder.Services.AddScoped<IBookDesignService, BookDesignService>();

builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// ✅ Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ✅ Swagger (fixed duplicate)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Audio to Text API",
        Version = "v1"
    });
    c.OperationFilter<SwaggerFileOperationFilter>();
});

// ✅ Antiforgery
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Extra configs jo pehle miss ho gaye thay
builder.Services.Configure<ChapterGenerationOptions>(
    builder.Configuration.GetSection(ChapterGenerationOptions.SectionName));

builder.Services.AddHttpClient("ExternalChapterGeneration", (sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var mins = int.TryParse(cfg["ChapterGeneration:HttpTimeoutMinutes"], out var m) ? m : 30;
    mins = Math.Clamp(mins, 1, 120);
    client.Timeout = TimeSpan.FromMinutes(mins);
});

builder.Services.Configure<EBookDashboard.Services.OpenAiOptions>(
    builder.Configuration.GetSection(EBookDashboard.Services.OpenAiOptions.SectionName));

builder.Services.AddHttpClient<IChatService, ChatService>();

var app = builder.Build();


// ✅🔥 MAIN FIX (IMPORTANT)
var bookCoversPath = Path.Combine(app.Environment.ContentRootPath, "Images", "book_covers");
Directory.CreateDirectory(bookCoversPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(bookCoversPath),
    RequestPath = "/book-covers"
});


// ✅ Middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<EBookDashboard.Middleware.SessionManagementMiddleware>();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

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

app.MapGet("/Dashboard/undefined", (Microsoft.AspNetCore.Http.HttpContext context) =>
{
    context.Response.Redirect("/Account/Login");
    return System.Threading.Tasks.Task.CompletedTask;
});

app.Run();
