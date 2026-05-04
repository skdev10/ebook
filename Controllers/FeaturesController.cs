using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers
{
    [Authorize]
    [Route("Features")]
    public class FeaturesController : Controller
    {
        private readonly IFeatureCartService _featureCartService;
        private readonly ApplicationDbContext _context;

        public FeaturesController(IFeatureCartService featureCartService, ApplicationDbContext context)
        {
            _featureCartService = featureCartService;
            _context = context;
        }

        [HttpGet]
        [Route("")]
        [Route("Index")]
        public async Task<IActionResult> Index()
        {
            // Check if user has an active confirmed plan
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("UserLogin", "Account");
            }

            var hasActivePlan = await _featureCartService.HasActivePlanAsync(userId);
            if (hasActivePlan)
            {
                // User already has an active plan, redirect to dashboard
                return RedirectToAction("Index", "Dashboard");
            }

            // Get all features
            var features = await _featureCartService.GetAllFeaturesAsync();
            
            // Get temp features for this user
            var sessionId = HttpContext.Session.Id;
            var tempFeatures = await _featureCartService.GetTempFeaturesAsync(sessionId, userId);

            var viewModel = new FeaturesViewModel
            {
                Features = features.ToList(),
                TempFeatures = tempFeatures.ToList(),
                PlanMap = _featureCartService.GetPlanMap()
            };

            return View(viewModel);
        }

        [HttpPost]
        [Route("AddToCart")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var sessionId = HttpContext.Session.Id;
            var result = await _featureCartService.AddFeatureToTempAsync(sessionId, userId, request.FeatureId);

            if (result)
            {
                // Get updated temp features count
                var tempFeatures = await _featureCartService.GetTempFeaturesAsync(sessionId, userId);
                var totalCount = tempFeatures.Count();
                
                // Get suggested plan
                var suggestedPlan = await _featureCartService.SuggestPlanAsync(sessionId, userId);
                
                return Json(new { 
                    success = true, 
                    totalItems = totalCount,
                    suggestedPlan = suggestedPlan != null ? new { 
                        id = suggestedPlan.PlanId, 
                        name = suggestedPlan.PlanName, 
                        price = suggestedPlan.PlanRate 
                    } : null
                });
            }

            return Json(new { success = false, message = "Failed to add feature to cart" });
        }

        [HttpPost]
        [Route("RemoveFromCart")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCart([FromBody] RemoveFromCartRequest request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var sessionId = HttpContext.Session.Id;
            var result = await _featureCartService.RemoveFeatureFromTempAsync(sessionId, userId, request.FeatureId);

            if (result)
            {
                // Get updated temp features count
                var tempFeatures = await _featureCartService.GetTempFeaturesAsync(sessionId, userId);
                var totalCount = tempFeatures.Count();
                
                // Get suggested plan
                var suggestedPlan = await _featureCartService.SuggestPlanAsync(sessionId, userId);
                
                return Json(new { 
                    success = true, 
                    totalItems = totalCount,
                    suggestedPlan = suggestedPlan != null ? new { 
                        id = suggestedPlan.PlanId, 
                        name = suggestedPlan.PlanName, 
                        price = suggestedPlan.PlanRate 
                    } : null
                });
            }

            return Json(new { success = false, message = "Failed to remove feature from cart" });
        }

        [HttpPost]
        [Route("ConfirmPlan")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPlan([FromBody] ConfirmPlanRequest request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var sessionId = HttpContext.Session.Id;
            var result = await _featureCartService.ConfirmPlanAndMigrateAsync(sessionId, userId, request.PlanId);

            if (result)
            {
                return Json(new { success = true, redirect = "/Dashboard/Index" });
            }

            return Json(new { success = false, message = "Failed to confirm plan" });
        }

        [HttpGet]
        [Route("CartPreview")]
        public async Task<IActionResult> CartPreview()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var sessionId = HttpContext.Session.Id;
            var tempFeatures = await _featureCartService.GetTempFeaturesAsync(sessionId, userId);
            var total = await _featureCartService.CalculateTotalTempPriceAsync(sessionId, userId);

            var cartItems = tempFeatures.Select(tf => new TempCartItemDto
            {
                Id = tf.FeatureId ?? 0,
                Name = tf.FeatureName ?? "",
                //Type = tf.PlanFeature?.FeatureName ?? "", // This might need adjustment
                Price = tf.FeatureRate
            }).ToList();

            return Json(new { 
                success = true, 
                items = cartItems, 
                total = total,
                count = cartItems.Count
            });
        }

        [HttpPost]
        [Route("ClearCart")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearCart()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var sessionId = HttpContext.Session.Id;
            var result = await _featureCartService.ClearTempAsync(sessionId, userId);

            if (result)
            {
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Failed to clear cart" });
        }
    }

    // View Models
    public class FeaturesViewModel
    {
        public List<PlanFeatures> Features { get; set; } = new List<PlanFeatures>();
        public List<AuthorPlanFeatures> TempFeatures { get; set; } = new List<AuthorPlanFeatures>();
        public Dictionary<string, int> PlanMap { get; set; } = new Dictionary<string, int>();
    }

    public class TempCartItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public decimal Price { get; set; }
    }

    // Request Models
    public class AddToCartRequest
    {
        public int FeatureId { get; set; }
    }

    public class RemoveFromCartRequest
    {
        public int FeatureId { get; set; }
    }

    public class ConfirmPlanRequest
    {
        public int PlanId { get; set; }
    }
}