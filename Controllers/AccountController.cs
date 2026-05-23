using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using Humanizer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace EBookDashboard.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {

        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<AccountController> _logger;
        private readonly IConfiguration _configuration;

        public AccountController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<AccountController> logger, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
            _logger = logger;
        }
        //---------------------------------------------------
        //------   Action Method to Display User Login  ----
        //---------------------------------------------------           
        // GET: /Account/Login — redirect to primary user sign-in (Google/Facebook + password).
        [HttpGet]
        public IActionResult Login()
        {
            return RedirectToAction(nameof(UserLogin));
        }

        // GET: /Account/AdminLogin - Administrator login page
        [HttpGet]
        public IActionResult AdminLogin()
        {
            return View();
        }

        // POST: /Account/AdminLogin - Only allows users with Admin role
        [HttpPost]
        public async Task<IActionResult> AdminLogin(string UserEmail, string Password, bool RememberMe)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserEmail == UserEmail && u.Password == Password);

            if (user == null)
            {
                ViewBag.Error = "Invalid email or password.";
                return View();
            }

            var role = await _context.Roles
                .Where(r => r.RoleId == user.RoleId)
                .Select(r => r.RoleName)
                .FirstOrDefaultAsync();

            if (role != "Admin")
            {
                ViewBag.Error = "This page is for administrators only. Please use the User login.";
                return View();
            }

            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("FullName", user.FullName ?? "");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? ""),
                new Claim(ClaimTypes.Email, user.UserEmail),
                new Claim(ClaimTypes.Role, role ?? "Admin")
            };

            var claimsIdentity = new ClaimsIdentity(claims, "AdminCookie");
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = RememberMe,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
            };

            await HttpContext.SignInAsync(
                "AdminCookie",
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return RedirectToAction("Dashboard", "Admin");
        }

        private async Task SetOAuthLoginAvailabilityAsync()
        {
            var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            // Always show Google UI on login; /auth/google validates when ClientId+Secret are configured.
            ViewBag.GoogleLoginAvailable = true;
            ViewBag.GoogleOAuthConfigured = await schemeProvider.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) != null;
            ViewBag.FacebookLoginAvailable = true;
            ViewBag.FacebookOAuthConfigured = await schemeProvider.GetSchemeAsync(FacebookDefaults.AuthenticationScheme) != null;
        }

        // GET: /Account/UserLogin - User login page
        [HttpGet]
        public async Task<IActionResult> UserLogin()
        {
            await SetOAuthLoginAvailabilityAsync();
            return View();
        }

        // POST: /Account/UserLogin - Only allows non-Admin roles (User, Author, Reader)
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> UserLogin(string UserEmail, string Password, bool RememberMe)
        {
            try
            {
                await SetOAuthLoginAvailabilityAsync();
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserEmail == UserEmail && u.Password == Password);

                if (user == null)
                {
                    ViewBag.Error = "Invalid email or password. Please try again.";
                    return View();
                }

                var role = await _context.Roles
                    .Where(r => r.RoleId == user.RoleId)
                    .Select(r => r.RoleName)
                    .FirstOrDefaultAsync();

                if (role == "Admin")
                {
                    ViewBag.Error = "This page is for users. Administrators should use the Admin login.";
                    return View();
                }

                HttpContext.Session.SetInt32("UserId", user.UserId);
                HttpContext.Session.SetString("FullName", user.FullName ?? "");

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Name, user.FullName ?? ""),
                    new Claim(ClaimTypes.Email, user.UserEmail),
                    new Claim(ClaimTypes.Role, role ?? "Reader")
                };

                var claimsIdentity = new ClaimsIdentity(claims, "UserCookie");
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = RememberMe,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(120)
                };

                await HttpContext.SignInAsync(
                    "UserCookie",
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Always open dashboard after password login (saved resume URLs can point at broken pages).
                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UserLogin failed for {Email}", UserEmail);
                ViewBag.Error = "Sign-in failed due to a server error. Please try again or contact support.";
                await SetOAuthLoginAvailabilityAsync();
                return View();
            }
        }

        /// <summary>Entry point for Google OAuth (middleware callback is Authentication:Google:CallbackPath, default /auth/google/callback).</summary>
        [HttpGet("/auth/google")]
        public async Task<IActionResult> GoogleLoginStart([FromServices] IAuthenticationSchemeProvider schemeProvider)
        {
            if (await schemeProvider.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) == null)
            {
                _logger.LogWarning("Google login requested but Google authentication is not configured (missing ClientSecret or credentials).");
                return RedirectToAction(nameof(UserLogin), new { error = "google_not_configured" });
            }

            var redirectUrl = Url.Action("ExternalLoginCallback", "Account");
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        // GET: /Account/ExternalLogin - Redirect to Google or Facebook
        [HttpGet]
        public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl)
        {
            if (string.IsNullOrEmpty(provider))
                return RedirectToAction("UserLogin");

            var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            if (await schemeProvider.GetSchemeAsync(provider) == null)
            {
                _logger.LogWarning("External login requested for '{Provider}' but that scheme is not configured.", provider);
                var err = string.Equals(provider, FacebookDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase)
                    ? "facebook_not_configured"
                    : "social_unavailable";
                return RedirectToAction(nameof(UserLogin), new { error = err });
            }

            var redirectUrl = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, provider);
        }

        // GET: /Account/ExternalLoginCallback - Called after Google/Facebook returns
        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl, string? remoteError = null)
        {
            if (!string.IsNullOrEmpty(remoteError))
            {
                _logger.LogWarning("External login error: {Error}", remoteError);
                var qErr = string.Equals(remoteError, "access_denied", StringComparison.OrdinalIgnoreCase)
                    ? "oauth_denied"
                    : "oauth_failed";
                return RedirectToAction("UserLogin", new { error = qErr });
            }

            var authSchemes = new[] { "Google", "Facebook" };
            AuthenticateResult? authResult = null;
            string? schemeUsed = null;

            foreach (var scheme in authSchemes)
            {
                authResult = await HttpContext.AuthenticateAsync(scheme);
                if (authResult?.Succeeded == true)
                {
                    schemeUsed = scheme;
                    break;
                }
            }

            if (authResult?.Succeeded != true || authResult.Principal == null)
            {
                _logger.LogWarning("External login: no identity from provider.");
                return RedirectToAction("UserLogin", new { error = "oauth_failed" });
            }

            var email = authResult.Principal.FindFirstValue(ClaimTypes.Email)
                ?? authResult.Principal.FindFirstValue("email")
                ?? authResult.Principal.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");
            var name = authResult.Principal.FindFirstValue(ClaimTypes.Name)
                ?? authResult.Principal.FindFirstValue("name");

            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("External login: no email claim from provider.");
                await HttpContext.SignOutAsync(schemeUsed!);
                return RedirectToAction("UserLogin", new { error = "oauth_failed" });
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == email);

            if (user == null)
            {
                user = new Users
                {
                    UserEmail = email,
                    FullName = name ?? email,
                    Password = "", // no password for external-only users
                    RoleId = 3, // Reader
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = new DateTime(1980, 1, 1)
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                user = await _context.Users.Include(u => u.Role).FirstAsync(u => u.UserId == user.UserId);
            }

            if (user.Role?.RoleName == "Admin")
            {
                await HttpContext.SignOutAsync(schemeUsed!);
                return RedirectToAction("UserLogin");
            }

            user.LastLoginAt = DateTime.UtcNow;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("FullName", user.FullName ?? "");

            var role = user.Role?.RoleName ?? "Reader";
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? ""),
                new Claim(ClaimTypes.Email, user.UserEmail),
                new Claim(ClaimTypes.Role, role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, "UserCookie");
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
            };

            await HttpContext.SignOutAsync(schemeUsed!);
            await HttpContext.SignInAsync("UserCookie", new ClaimsPrincipal(claimsIdentity), authProperties);

            var resumeExt = TryRedirectToSavedResume(user.UserId);
            if (resumeExt != null) return resumeExt;
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Dashboard");
        }

        //================================================
        //---- Action Method to Display User OTP Page ----
        //================================================
        [HttpGet]
        public IActionResult LoginOtp()
        {
            if (TempData["UserEmail"] == null)
            {
                return RedirectToAction(nameof(UserLogin));
            }

            ViewBag.UserEmail = TempData["UserEmail"];
            TempData.Keep("UserEmail"); // Keep for subsequent requests
            return View();
        }
        //---------------------------------------------------
        //--------------- Check User Login OTP --------------
        //---------------------------------------------------  
        // POST: /Account/Login
        [HttpPost]
        public async Task<IActionResult> Login1(string UserEmail, string Password, bool RememberMe)
        {
            int? userId = 0;
            // check Users table in MySQL & verify user credentials
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserEmail == UserEmail && u.Password == Password);
            try
            {
                if (user == null)
                {
                    ViewBag.Error = "Invalid email or password";
                    return View();
                }
                HttpContext.Session.SetInt32("UserId", user.UserId);
                HttpContext.Session.SetString("FullName", user.FullName ?? "");
                userId = user.UserId;

                // Generate and send OTP
                var otp = GenerateOtp();
                await SaveOtp(UserEmail, otp);

                // Send OTP via email
                await SendOtpEmail(UserEmail, otp);

                // Store email in TempData for OTP verification
                TempData["UserEmail"] = UserEmail;
                TempData["RememberMe"] = RememberMe;

                return RedirectToAction("LoginOtp");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "An error occurred during login. Please try again."; ex.Message.ToString();
                return View();
            }
        }
        // OTP Verification
        [HttpPost]
        public async Task<IActionResult> VerifyOtp(string UserEmail, string OtpCode)
        {
            try
            {
                if (string.IsNullOrEmpty(UserEmail) || string.IsNullOrEmpty(OtpCode))
                {
                    ViewBag.Error = "Please enter the OTP code";
                    ViewBag.UserEmail = UserEmail;
                    return View("LoginOtp");
                }

                // Verify OTP
                var isValid = await VerifyOtpAsync(UserEmail, OtpCode);

                if (!isValid)
                {
                    ViewBag.Error = "Invalid or expired OTP code";
                    ViewBag.UserEmail = UserEmail;
                    return View("LoginOtp");
                }

                // Get user and create session
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == UserEmail);
                if (user != null)
                {
                    // Set session variables
                    HttpContext.Session.SetInt32("UserId", user.UserId);
                    HttpContext.Session.SetString("FullName", user.FullName ?? "");

                    // ✅ fetch role name from Roles table
                    var role = await _context.Roles
                        .Where(r => r.RoleId == user.RoleId)
                        .Select(r => r.RoleName)
                        .FirstOrDefaultAsync();

                    var scheme = (role == "Admin") ? "AdminCookie" : "UserCookie";
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                        new Claim(ClaimTypes.Name, user.FullName ?? ""),
                        new Claim(ClaimTypes.Email, user.UserEmail),
                        new Claim(ClaimTypes.Role, role ?? "Reader")
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, scheme);
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = TempData["RememberMe"] != null ? (bool)TempData["RememberMe"] : false,
                        ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
                    };

                    await HttpContext.SignInAsync(scheme, new ClaimsPrincipal(claimsIdentity), authProperties);
                    await MarkOtpAsUsed(UserEmail, OtpCode);

                    if (role == "Admin")
                        return RedirectToAction("Dashboard", "Admin");
                    var resumeOtp = TryRedirectToSavedResume(user.UserId);
                    if (resumeOtp != null) return resumeOtp;
                    if (role == "User" || role == "Author" || role == "Reader")
                        return RedirectToAction("AIGenerateBook", "Books");
                    return RedirectToAction("AIGenerateBook", "Books");
                }
                else
                {
                    ViewBag.Error = "User not found";
                    ViewBag.UserEmail = UserEmail;
                    return View("LoginOtp");
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "An error occurred during verification. Please try again."; ex.Message.ToString(); ;
                ViewBag.UserEmail = UserEmail;
                return View("LoginOtp");
            }
        }
        // Resend OTP
        [HttpGet]
        public async Task<IActionResult> ResendOtp(string userEmail)
        {
            try
            {
                var otp = GenerateOtp();
                await SaveOtp(userEmail, otp);
                await SendOtpEmail(userEmail, otp);

                TempData["UserEmail"] = userEmail;
                ViewBag.Success = "A new OTP has been sent to your email";
                return View("LoginOtp");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Failed to resend OTP. Please try again."; ex.Message.ToString();
                ViewBag.UserEmail = userEmail;
                return View("LoginOtp");
            }
        }

        //--------------------------------------------------------------------------
        //-----------------  Private Helper Methods  -------------------------------
        //--------------------------------------------------------------------------
        private string GenerateOtp()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private async Task SaveOtp(string email, string otp)
        {
            // Remove existing OTPs for this email
            var existingOtps = await _context.OtpVerifications
                .Where(o => o.Email == email && !o.IsUsed)
                .ToListAsync();

            _context.OtpVerifications.RemoveRange(existingOtps);

            // Save new OTP
            var otpVerification = new OtpVerification
            {
                Email = email,
                OtpCode = otp,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5), // 5 minutes expiry
                IsUsed = false,
                Purpose = "Login"
            };

            _context.OtpVerifications.Add(otpVerification);
            await _context.SaveChangesAsync();
        }

        private async Task<bool> VerifyOtpAsync(string email, string otp)
        {
            var otpRecord = await _context.OtpVerifications
                .FirstOrDefaultAsync(o => o.Email == email &&
                                         o.OtpCode == otp &&
                                         !o.IsUsed &&
                                         o.ExpiresAt > DateTime.UtcNow);

            return otpRecord != null;
        }

        private async Task MarkOtpAsUsed(string email, string otp)
        {
            var otpRecord = await _context.OtpVerifications
                .FirstOrDefaultAsync(o => o.Email == email && o.OtpCode == otp);

            if (otpRecord != null)
            {
                otpRecord.IsUsed = true;
                await _context.SaveChangesAsync();
            }
        }

        private async Task SendOtpEmail(string email, string otp)
        {
            var subject = "Your eBook Publisher Login OTP";
            var body = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                <h2 style='color: #667eea;'>eBook Publisher - OTP Verification</h2>
                <p>Dear User,</p>
                <p>Your One-Time Password (OTP) for login is:</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <span style='font-size: 32px; font-weight: bold; color: #667eea; letter-spacing: 10px;'>{otp}</span>
                </div>
                <p>This OTP is valid for 5 minutes. Please do not share this code with anyone.</p>
                <p>If you didn't request this OTP, please ignore this email.</p>
                <br>
                <p>Best regards,<br>eBook Publisher Team</p>
            </div>";

            await _emailService.SendEmailAsync(email, subject, body);
        }

        //---------------------------------------------------
        //--------------- Check User Login Simple -----------
        //---------------------------------------------------  
        // POST: /Account/Login
        [HttpPost]
        public async Task<IActionResult> Login(string UserEmail, string Password, bool RememberMe)
        {
            int? userId = 0;
            // check Users table in MySQL & verify user credentials
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserEmail == UserEmail && u.Password == Password);

            if (user != null)
            {
                HttpContext.Session.SetInt32("UserId", user.UserId);
                HttpContext.Session.SetString("FullName", user.FullName ?? "");
                userId = user.UserId;

                // ✅ Login success → redirect to main layout (Dashboard, Home, etc.)

                // ✅ fetch role name from Roles table
                var role = await _context.Roles
                    .Where(r => r.RoleId == user.RoleId)
                    .Select(r => r.RoleName)
                    .FirstOrDefaultAsync();

                // ✅ Use dual schemes so Admin and User can be logged in in different tabs
                var scheme = (role == "Admin") ? "AdminCookie" : "UserCookie";
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Name, user.FullName ?? ""),
                    new Claim(ClaimTypes.Email, user.UserEmail),
                    new Claim(ClaimTypes.Role, role ?? "Reader")
                };

                var claimsIdentity = new ClaimsIdentity(claims, scheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = RememberMe,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
                };

                await HttpContext.SignInAsync(scheme, new ClaimsPrincipal(claimsIdentity), authProperties);

                if (role == "Admin")
                    return RedirectToAction("Dashboard", "Admin");

                var resumeLogin = TryRedirectToSavedResume(user.UserId);
                if (resumeLogin != null) return resumeLogin;

                if (role == "User" || role == "Author" || role == "Reader")
                    return RedirectToAction("AIGenerateBook", "Books");

                return RedirectToAction("Index", "Dashboard"); // Default redirect to Dashboard
            }
            else
            {
                // ❌ Login failed → show error message
                await SetOAuthLoginAvailabilityAsync();
                ViewBag.Error = "Invalid username or password. Please try again.";
                ViewBag.UserId = userId; // ✅ send to Razor view
                return View();
            }
        }
        //--------------------------------------------------------------------------
        //-----------------  User Registration -------------------------------------
        //--------------------------------------------------------------------------
        // GET: Registration
        [HttpGet]
        public IActionResult Registration()
        {
            return View();
        }

        // POST: Registration
        [HttpPost]
        public async Task<IActionResult> Register(Users model, string ConfirmPassword)
        {

            // Check ConfirmPassword
            if (model.Password != ConfirmPassword)
            {
                ViewBag.Error = "Passwords do not match!";
                return View("Registration", model);
            }
            // Check if email already exists
            var existingUser = _context.Users.FirstOrDefault(u => u.UserEmail == model.UserEmail);
            if (existingUser != null)
            {
                ViewBag.Error = "Email already registered!";
                return View("Registration", model);
            }
            // OK Save the Data

            // Set default values
            model.CreatedAt = DateTime.UtcNow;
            model.LastLoginAt = DateTime.UtcNow;
            model.Status = "Active";
            model.RoleId = 3; // Default Reader Role

            // Save to DB
            _context.Users.Add(model);
            await _context.SaveChangesAsync();

            HttpContext.Session.SetInt32("UserId", model.UserId);
            HttpContext.Session.SetString("OnboardingPending", "1");
            return RedirectToAction("OnboardingProfile", "Account");
        }

        [HttpGet]
        public IActionResult OnboardingProfile()
        {
            if (HttpContext.Session.GetString("OnboardingPending") != "1")
                return RedirectToAction(nameof(UserLogin));
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnboardingProfile(string publisherName, string genre, string publishingGoal)
        {
            if (HttpContext.Session.GetString("OnboardingPending") != "1")
                return RedirectToAction(nameof(UserLogin));
            var uid = HttpContext.Session.GetInt32("UserId");
            if (!uid.HasValue)
                return RedirectToAction(nameof(UserLogin));

            var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == uid.Value);
            if (user == null)
                return RedirectToAction(nameof(UserLogin));

            HttpContext.Session.Remove("OnboardingPending");

            var roleName = user.Role?.RoleName ?? "Reader";
            var scheme = roleName == "Admin" ? "AdminCookie" : "UserCookie";
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? ""),
                new Claim(ClaimTypes.Email, user.UserEmail),
                new Claim(ClaimTypes.Role, roleName)
            };
            var identity = new ClaimsIdentity(claims, scheme);
            await HttpContext.SignInAsync(scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTime.UtcNow.AddHours(8)
            });

            var resumeOnboard = TryRedirectToSavedResume(user.UserId);
            if (resumeOnboard != null) return resumeOnboard;
            return RedirectToAction("Index", "Dashboard");
        }
        //--------------------------------------------------------------------------
        //-----------------  Forgot Password   --------------------------------------
        //--------------------------------------------------------------------------
        // GET: ForgotPassword Action --> It will return Views\Account\ForgotPassword.cshtml page
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }
        // POST: ForgotPassword Action --> It will generate email and return Views\Account\ForgotPassword.cshtml page
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword(ForgotPassword model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserEmail == model.UserEmail);

                    if (user != null)
                    {
                        // Generate OTP for password reset
                        var otp = GenerateOtp();
                        // Generate a random reset token (you can store it in DB with expiry)
                        var token = Guid.NewGuid().ToString();

                        // Save OTP & token to database
                        await SavePasswordResetOtp(model.UserEmail, otp, token);
                        // Send OTP via email
                        await SendPasswordResetOtpEmail(model.UserEmail, otp);

                        // Store email in TempData for OTP verification
                        TempData["ResetEmail"] = model.UserEmail;
                        TempData["ResetToken"] = token;

                        return RedirectToAction("ResetPasswordOtp");

                        //// Save token in DB (create a PasswordResetTokens table or add field in Users)
                        //var resetLink = Url.Action(
                        //    "ResetPassword",
                        //    "Account",
                        //    new { email = model.UserEmail, token = token },
                        //    Request.Scheme
                        //);

                        //var emailBody = $@"
                        //    <h2>Password Reset</h2>
                        //    <p>Click the link below to reset your password:</p>
                        //    <p><a href='{resetLink}'>Reset Password</a></p>
                        //";

                        //await _emailService.SendEmailAsync(model.UserEmail, "Password Reset Request", emailBody);
                        //_logger.LogWarning(resetLink);
                        //return RedirectToAction("ForgotPasswordConfirmation", "Account");
                    }

                    // Don’t reveal if email doesn’t exist (security best practice)
                    return RedirectToAction("ForgotPasswordConfirmation", "Account");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ForgotPassword for email: {Email}", model.UserEmail);
                    ViewBag.Error = "An error occurred while processing your request. Please try again.";
                    return View(model);
                }
                //return View(model);
            }
            return View(model);
        }
        //--------------------------------------------------------------------------
        //-----------------  Confirmation Actions  ---------------------------------
        //--------------------------------------------------------------------------

        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }
        //--------------------------------------------------------------------------
        //-----------------  Reset Password  2 --------------------------------------
        //--------------------------------------------------------------------------
        // POST: ForgotPassword Action --> It will generate email and return Views\Account\ForgotPassword.cshtml page
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPasswordOld(ResetPassword model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserEmail == model.UserEmail);
            // if user did not provide valid email address this part will not execut
            if (user == null)
            {
                // don’t reveal user existence
                return View("ResetPasswordConfirmation");
            }

            // ✅ TODO: validate token manually (since no Identity is used)

            // Update password (hash it in production!)
            user.Password = model.Password;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password reset for {Email}", model.UserEmail);

            return View("ResetPasswordConfirmation");
        }

        // GET: Reset Password Action --> It will Check toke and email and return Message page
        //--------------------------------------------------------------------------
        //-----------------  Reset Password OTP Verification  ----------------------
        //--------------------------------------------------------------------------
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPasswordOtp()
        {
            if (TempData["ResetEmail"] == null)
            {
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.ResetEmail = TempData["ResetEmail"];
            TempData.Keep("ResetEmail"); // Keep for subsequent requests
            return View();
        }


        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // Logout - sign out both Admin and User cookies so both tabs are cleared
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            try
            {
                HttpContext.Session.Clear();
            }
            catch
            {
                // Session store may be unavailable; still clear auth cookies
            }

            await HttpContext.SignOutAsync("AdminCookie");
            await HttpContext.SignOutAsync("UserCookie");

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers.Pragma = "no-cache";

            return RedirectToAction(nameof(UserLogin));
        }
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyResetPasswordOtp(string UserEmail, string OtpCode)
        {
            try
            {
                if (string.IsNullOrEmpty(UserEmail) || string.IsNullOrEmpty(OtpCode))
                {
                    ViewBag.Error = "Please enter the OTP code";
                    ViewBag.ResetEmail = UserEmail;
                    return View("ResetPasswordOtp");
                }

                // Verify OTP
                var isValid = await VerifyPasswordResetOtpAsync(UserEmail, OtpCode);

                if (!isValid)
                {
                    ViewBag.Error = "Invalid or expired OTP code";
                    ViewBag.ResetEmail = UserEmail;
                    return View("ResetPasswordOtp");
                }

                // Get the reset record to pass token
                var resetRecord = await _context.PasswordResets
                    .FirstOrDefaultAsync(r => r.Email == UserEmail &&
                                             r.OTP == OtpCode &&
                                             !r.IsUsed &&
                                             r.ExpiresAt > DateTime.UtcNow);

                if (resetRecord != null)
                {
                    // Mark OTP as used
                    resetRecord.IsUsed = true;
                    await _context.SaveChangesAsync();

                    // Redirect to reset password page with token
                    return RedirectToAction("ResetPassword", new
                    {
                        email = UserEmail,
                        token = resetRecord.Token
                    });
                }

                ViewBag.Error = "Invalid OTP verification";
                ViewBag.ResetEmail = UserEmail;
                return View("ResetPasswordOtp");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in VerifyResetPasswordOtp for email: {Email}", UserEmail);
                ViewBag.Error = "An error occurred during verification. Please try again.";
                ViewBag.ResetEmail = UserEmail;
                return View("ResetPasswordOtp");
            }
        }
        //=================================================================
        //--------- HttpGet  -->  Reset Password New  
        //===============================================================
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                ViewBag.Error = "Invalid reset link";
                return View();
            }

            var model = new ResetPassword
            {
                UserEmail = email,
                Token = token
            };

            return View(model);
        }
        //===========================================================
        //---- HttpPost --> Action Method to Reset User Password ----
        //=========================================================== 
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(ResetPassword model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Validate token
                var isValidToken = await ValidateResetTokenAsync(model.UserEmail, model.Token);

                if (!isValidToken)
                {
                    ViewBag.Error = "Invalid or expired reset token";
                    return View(model);
                }

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserEmail == model.UserEmail);

                if (user == null)
                {
                    // Don't reveal user existence
                    return RedirectToAction("ResetPasswordConfirmation");
                }

                // Update password (in production, hash the password!)
                user.Password = model.Password;
                _context.Users.Update(user);

                // Mark all reset tokens for this email as used
                var resetRecords = await _context.PasswordResets
                    .Where(r => r.Email == model.UserEmail && !r.IsUsed)
                    .ToListAsync();

                foreach (var record in resetRecords)
                {
                    record.IsUsed = true;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Password reset successfully for {Email}", model.UserEmail);

                return RedirectToAction("ResetPasswordConfirmation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ResetPassword for email: {Email}", model.UserEmail);
                ViewBag.Error = "An error occurred while resetting your password. Please try again.";
                return View(model);
            }
        }

        //--------------------------------------------------------------------------
        //-----------------  Resend Reset Password OTP  ----------------------------
        //--------------------------------------------------------------------------
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ResendResetPasswordOtp(string userEmail)
        {
            try
            {
                var otp = GenerateOtp();
                var token = Guid.NewGuid().ToString();

                await SavePasswordResetOtp(userEmail, otp, token);
                await SendPasswordResetOtpEmail(userEmail, otp);

                TempData["ResetEmail"] = userEmail;
                TempData["ResetToken"] = token;

                ViewBag.Success = "A new OTP has been sent to your email";
                ViewBag.ResetEmail = userEmail;
                return View("ResetPasswordOtp");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ResendResetPasswordOtp for email: {Email}", userEmail);
                ViewBag.Error = "Failed to resend OTP. Please try again.";
                ViewBag.ResetEmail = userEmail;
                return View("ResetPasswordOtp");
            }
        }
        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }
        private async Task SavePasswordResetOtp(string email, string otp, string token)
        {
            if (string.IsNullOrEmpty(email))
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            // Remove existing unused OTPs for this email
            var existingResets = await _context.PasswordResets
                .Where(r => r.Email == email && !r.IsUsed)
                .ToListAsync();

            if (existingResets.Any())
            {
                _context.PasswordResets.RemoveRange(existingResets);
            }

            // Save new OTP
            var passwordReset = new PasswordReset
            {
                Email = email,
                OTP = otp,
                Token = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10), // 10 minutes expiry
                IsUsed = false
            };

            _context.PasswordResets.Add(passwordReset);
            await _context.SaveChangesAsync();
        }

        private async Task<bool> VerifyPasswordResetOtpAsync(string email, string otp)
        {
            var resetRecord = await _context.PasswordResets
                .FirstOrDefaultAsync(r => r.Email == email &&
                                         r.OTP == otp &&
                                         !r.IsUsed &&
                                         r.ExpiresAt > DateTime.UtcNow);

            return resetRecord != null;
        }

        private async Task<bool> ValidateResetTokenAsync(string email, string token)
        {
            var resetRecord = await _context.PasswordResets
                .FirstOrDefaultAsync(r => r.Email == email &&
                                         r.Token == token &&
                                         !r.IsUsed &&
                                         r.ExpiresAt > DateTime.UtcNow);

            return resetRecord != null;
        }

        private async Task SendPasswordResetOtpEmail(string email, string otp)
        {
            var subject = "eBook Publisher - Password Reset OTP";
            var body = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                <h2 style='color: #667eea;'>eBook Publisher - Password Reset</h2>
                <p>Dear User,</p>
                <p>You have requested to reset your password. Use the OTP below to verify your identity:</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <span style='font-size: 32px; font-weight: bold; color: #667eea; letter-spacing: 10px;'>{otp}</span>
                </div>
                <p>This OTP is valid for 10 minutes. Please do not share this code with anyone.</p>
                <p>If you didn't request a password reset, please ignore this email.</p>
                <br>
                <p>Best regards,<br>eBook Publisher Team</p>
            </div>";

            await _emailService.SendEmailAsync(email, subject, body);
        }

        /// <summary>Returns a redirect to the user's last book-design URL if it is safe, otherwise null.</summary>
        private IActionResult? TryRedirectToSavedResume(int userId)
        {
            if (userId <= 0) return null;
            try
            {
                var key = $"user:{userId}:lastBookWorkUrl";
                var row = _context.Settings.AsNoTracking().FirstOrDefault(s => s.Key == key);
                var path = row?.Value?.Trim();
                if (string.IsNullOrEmpty(path) || path.Length > 600) return null;
                if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)) return null;
                if (path.Contains("://", StringComparison.Ordinal) || path.Contains('\\')) return null;
                // After login, only resume within Dashboard (book writer URLs can crash on partial DB state).
                var pathOnly = path.Split('?', 2)[0];
                if (!pathOnly.StartsWith("/Dashboard", StringComparison.OrdinalIgnoreCase))
                    return null;
                return LocalRedirect(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load saved resume URL for user {UserId}; continuing to dashboard.", userId);
                return null;
            }
        }

        // ... [Your existing other methods] ...
        private async Task CreateUserSession(Users user)
        {
        }
    }
}