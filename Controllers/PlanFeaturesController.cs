using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace EBookDashboard.Controllers
{
    [Authorize]
    [Route("Admin/[controller]")]
    [ServiceFilter(typeof(RequireAdminAuthorizationFilter))]
    public class PlanFeaturesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPlanFeaturesService _planFeaturesService;

        public PlanFeaturesController(ApplicationDbContext context, IPlanFeaturesService planFeaturesService)
        {
            _planFeaturesService = planFeaturesService;
            _context = context;
        }
        // ✅ This action serves the Razor View
        [HttpGet]
        [Route("")]
        public async Task<IActionResult> Index()
        {
            var pf = await _planFeaturesService.GetAllFeaturesAsync();
            return View("~/Views/Admin/PlanFeatures.cshtml", pf);
        }

        // ✅ JSON API under /Admin so Admin cookie is used for auth
        [HttpGet("/Admin/api/planfeatures")]
        public async Task<IActionResult> GetAll()
        {
            var pf = await _planFeaturesService.GetAllFeaturesAsync();
            return Ok(pf);
        }
        [HttpPost("/Admin/api/planfeatures/add")]
        public async Task<IActionResult> AddFeature([FromBody] PlanFeatures model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            await _planFeaturesService.AddFeatureAsync(model);
            return Ok(model);
        }

        [HttpPut("/Admin/api/planfeatures/{id}")]
        public async Task<IActionResult> UpdateFeature(int id, [FromBody] UpdatePlanFeatureRequest request)
        {
            var existing = await _context.PlanFeatures.FindAsync(id);
            if (existing == null)
                return NotFound(new { message = "Feature not found" });
            if (request == null)
                return BadRequest("Request body is required.");
            try
            {
                existing.FeatureName = request.FeatureName ?? existing.FeatureName;
                existing.Description = request.Description;
                existing.FeatureRate = request.FeatureRate;
                existing.Currency = request.Currency ?? existing.Currency;
                existing.Status = request.Status ?? existing.Status;
                existing.IsActive = request.IsActive;
                existing.UsageLimit = request.UsageLimit;
                existing.FeatureType = request.FeatureType;
                existing.IsUnlimited = request.IsUnlimited;
                if (request.PlanId.HasValue) existing.PlanId = request.PlanId;
                _context.Entry(existing).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return Ok(existing);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Update failed.", detail = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteFeature(int id)
        {
            var feature = await _context.PlanFeatures.FindAsync(id);
            if (feature == null)
                return NotFound(new { message = "Feature not found" });

            _context.PlanFeatures.Remove(feature);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Feature deleted successfully" });
        }

        [HttpDelete("/Admin/api/planfeatures/{id}")]
        public async Task<ActionResult> ApiDeleteFeature(int id)
        {
            var feature = await _context.PlanFeatures.FindAsync(id);
            if (feature == null)
                return NotFound(new { message = "Feature not found" });

            _context.PlanFeatures.Remove(feature);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Feature deleted successfully" });
        }
    }

}
