using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthorPlansController : ControllerBase
    {
        private readonly IAuthorService _authorService;
        private readonly ApplicationDbContext _context;

        public AuthorPlansController(IAuthorService authorService, ApplicationDbContext context)
        {
            _authorService = authorService;
            _context = context;
        }

        // Get all plans for a specific author (with active flag)
        [HttpGet("by-author/{authorId}")]
        public async Task<IActionResult> GetAuthorPlans(int authorId)
        {
            var plans = await _authorService.GetPlansByAuthorAsync(authorId);
            if (plans == null) return NotFound("No plans found for this author");

            return Ok(plans);
        }

        //[HttpGet]
        //public async Task<IActionResult> GetAuthorPlans()
        //{
        //    var plans = await _context.AuthorPlans
        //        .Include(ap => ap.Plan) // 👈 include Plan details
        //        .ToListAsync();

        //    return Ok(plans);
        //}



        // Get all available plans (for selection)
        [HttpGet("all-plans")]
        public async Task<IActionResult> GetAllPlans()
        {
            var allPlans = await _context.Plans.ToListAsync(); // Assuming you have Plans table
            return Ok(allPlans);
        }

        // Assign a new plan to author
        [HttpPost("assign")]
        public async Task<IActionResult> AssignPlan([FromBody] AuthorPlans plan)
        {
            var createdPlan = await _authorService.AssignPlanToAuthorAsync(plan);
            return Ok(createdPlan);
        }

        // Cancel plan
        [HttpPost("cancel/{planId}")]
        public async Task<IActionResult> CancelPlan(int planId, [FromBody] string reason)
        {
            var result = await _authorService.CancelAuthorPlanAsync(planId, reason);
            if (!result) return BadRequest("Failed to cancel plan");
            return Ok(new { message = "Plan cancelled successfully" });
        }
    }

}
