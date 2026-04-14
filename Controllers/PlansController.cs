using EBookDashboard.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers
{
    public class PlansController : Controller
    {
        private readonly IPlansService _plansService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApplicationDbContext _context;
        public PlansController(IPlansService planService, IHttpClientFactory httpClientFactory, ApplicationDbContext context)
        {
            _plansService = planService;
            _httpClientFactory = httpClientFactory;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var plans = await _plansService.GetActivePlansAsync();
            return View(plans);
        }

        [HttpPost]
        public async Task<IActionResult> BuyPlan(int planId)
        {
            var plan = (await _plansService.GetAllActivePlansAsync()).FirstOrDefault(p => p.PlanId == planId);
            if (plan == null) return NotFound();

            
            // 🔗 Call WebPaymentAPI
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri("https://localhost:5001/"); // your WebPaymentAPI base URL
            // call method of WebPaymentAPI-->[HttpPost("create-session-form")]
            var response = await client.PostAsJsonAsync("api/checkout/create-session-form", new
            {
                itemName = plan.PlanName,
                itemPrice = plan.PlanRate,
                quantity = 1,
                currency = "USD",
                customerEmail = User.Identity?.Name ?? "test@example.com"
            });

            if (!response.IsSuccessStatusCode)
                return BadRequest("Unable to create payment session");

            var result = await response.Content.ReadFromJsonAsync<dynamic>();
            string? sessionUrl = result?.sessionUrl;

            return Redirect(sessionUrl);
        }
        //=======================================================
        //-------- added on 09-10-2025 Filter only active plans
        //=======================================================
        [HttpGet]
        public IActionResult GetPlans()
        {
            try
            {
                var plans = _context.Plans
                     .Where(p => p.IsActive == 1) // Only active plans
                     .Select(p => new
                     {

                         p.PlanId,
                         p.PlanName,
                         p.PlanRate,
                         p.Currency,
                         p.PlanDays,
                         p.MaxEBooks,
                         p.PlanDescription
                     }).ToList();

                return Json(plans);
            }
            catch (Exception ex)
            {
                // Log the exception
                //_logger.LogError(ex, "Error retrieving plan features");
                return StatusCode(500, new { error = "Internal server error", ex });
            }
        }
        //=======================================================
        //-------- added on 01-11-2025 Filter only active plan features
        //=======================================================
        [HttpGet]
        public IActionResult GetPlanFeatures()
        {
            try
            {
                var plans = _context.PlanFeatures
                    .Where(p => p.IsActive == 1) // Only active plans
                    .Select(p => new
                    {
                        p.FeatureId,
                        p.PlanId,
                        p.FeatureName,
                        p.FeatureRate,
                        p.Currency,
                        p.Description
                    })
                    .OrderBy(p => p.FeatureId) // Optional: order the results
                    .ToList();

                return Json(plans);
            }
            catch (Exception ex)
            {
                // Log the exception
                //_logger.LogError(ex, "Error retrieving plan features");
                return StatusCode(500, new { error = "Internal server error",ex });
            }
        }
    }

}
