// AuthorPlanFeaturesController.cs
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EBookDashboard.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthorPlanFeaturesController : ControllerBase
    {
        private readonly IPlanFeaturesService _planFeaturesService;
        private readonly ILogger<AuthorPlanFeaturesController> _logger;
        private readonly ApplicationDbContext _dbContext;

        public AuthorPlanFeaturesController(
            IPlanFeaturesService planFeaturesService,
            ILogger<AuthorPlanFeaturesController> logger,
            ApplicationDbContext dbContext)
        {
            _planFeaturesService = planFeaturesService;
            _logger = logger;
            _dbContext = dbContext;
        }

        [HttpPost("save-features")]
        public async Task<IActionResult> SaveAuthorPlanFeatures([FromBody] SaveFeaturesRequest request)
        {
            try
            {
                // Validate request
                if (request == null || request.FeatureIds == null || !request.FeatureIds.Any())
                {
                    return BadRequest(new { success = false, message = "No features selected" });
                }

                // Get user information from claims // request.UserId;
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier); // This is string
                var userEmailClaim = User.FindFirstValue(ClaimTypes.Email);
               
                //if (userIdClaim == null) {
                //    userIdClaim = request.UserId.ToString();
                //}

                // Parse string to int
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    // Fallback to request.UserId if claims are not available
                    userId = request.UserId;

                    if (userId <= 0)
                    {
                        return Unauthorized(new { success = false, message = "User not authenticated or invalid user ID" });
                    }
                }

                // Save features using service and get generated BillId
                var billId = await _planFeaturesService.SaveAuthorPlanFeaturesAsync(
                    request.AuthorId,
                    userId, // Use the determined userId, not request.UserId
                    userEmailClaim ?? string.Empty,
                    request.FeatureIds
                );

                if (billId>0)
                {
                    _logger.LogInformation("Successfully saved {Count} features for author {AuthorId} with BillId {BillId}",
                        request.FeatureIds.Count, request.AuthorId, billId);
                    // return BillId in response
                    return Ok(new
                    {
                        success = true,
                        message = "Features saved successfully",
                        featureCount = request.FeatureIds.Count,
                        billId = billId
                    });
                }
                else
                {
                    return StatusCode(500, new { success = false, message = "Failed to save features" });
                
            }
           }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving author plan features for author {AuthorId}", request?.AuthorId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("author-features/{authorId}")]
        public async Task<IActionResult> GetAuthorFeatures(int authorId)
        {
            try
            {
                var features = await _planFeaturesService.GetAuthorPlanFeaturesAsync(authorId);
                return Ok(new { success = true, data = features });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving author features for author {AuthorId}", authorId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
        [HttpGet("author-bills/{authorId}")]
        public async Task<IActionResult> GetAuthorBills(int authorId)
        {
            try
            {
                var bills = await _planFeaturesService.GetAuthorBillsAsync(authorId);
                return Ok(new { success = true, data = bills });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving author bills for author {AuthorId}", authorId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("bill-details/{billId}")]
        public async Task<IActionResult> GetBillDetails(int billId)
        {
            try
            {
                var bill = await _planFeaturesService.GetAuthorBillWithFeaturesAsync(billId);
                if (bill == null)
                {
                    return NotFound(new { success = false, message = "Bill not found" });
                }
                return Ok(new { success = true, data = bill });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving bill details for bill {BillId}", billId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
        // When user Click Remove button of Cart
        [HttpPost("remove-from-cart")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCart([FromBody] RemoveFeatureRequest request)
        {
            try
            {
                if (request?.FeatureId == null || request.FeatureId <= 0)
                {
                    return BadRequest(new { success = false, message = "Invalid feature ID" });
                }

                // Get the current user
                var userEmail = User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userEmail))
                {
                    return Unauthorized(new { success = false, message = "User not authenticated" });
                }

                // Find user by email
                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.UserEmail == userEmail);

                if (user == null)
                {
                    return NotFound(new { success = false, message = "User not found" });
                }

                // Use AuthorPlanFeaturesSet directly (not Set<>)
                var authorPlanFeature = await _dbContext.AuthorPlanFeaturesSet
                    .Include(apf => apf.AuthorBill) // Include Bill if you need to check Bill.Status
                    .FirstOrDefaultAsync(apf => apf.AuthorId == user.UserId
                        && apf.FeatureId == request.FeatureId);

                if (authorPlanFeature != null)
                {
                    // Check if the feature can be removed (if it's associated with a pending bill)
                    if (authorPlanFeature.BillId != null)
                    {
                        // If you have a Bill navigation property, check its status
                        if (authorPlanFeature.AuthorBill != null && authorPlanFeature.AuthorBill.Status != "Pending")
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message = "Cannot remove feature from a processed bill"
                            });
                        }
                    }

                    // Remove the feature using AuthorPlanFeaturesSet
                    _dbContext.AuthorPlanFeaturesSet.Remove(authorPlanFeature);
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("Feature {FeatureId} removed from cart for user {UserId}",
                        request.FeatureId, user.UserId);

                    return Ok(new
                    {
                        success = true,
                        message = "Feature removed successfully",
                        featureId = request.FeatureId
                    });
                }

                return NotFound(new
                {
                    success = false,
                    message = "Feature not found in your cart"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing feature {FeatureId} from cart", request?.FeatureId);
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while removing the feature",
                    error = ex.Message // Only include in development
                });
            }
        }
        // Request model - moved outside or keep as nested class
        public class RemoveFeatureRequest
        {
            public int FeatureId { get; set; }
        }
    }
}

// SaveFeaturesRequest model (if not defined elsewhere)
public class SaveFeaturesRequest
{
    public int AuthorId { get; set; }
    public int UserId { get; set; }
    public List<int> FeatureIds { get; set; }
}


  
   
