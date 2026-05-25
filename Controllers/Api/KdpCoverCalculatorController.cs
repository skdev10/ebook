using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Interfaces;
using EBookDashboard.Application.Kdp.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers.Api;

/// <summary>
/// REST API for Amazon KDP paperback cover dimension calculations (back + spine + full wrap).
/// Used by the print-ready file flow and external tooling.
/// </summary>
[ApiController]
[Route("api/kdp")]
[Produces("application/json")]
[AllowAnonymous]
public class KdpCoverCalculatorController : ControllerBase
{
    private readonly IKdpCoverDimensionService _calculator;
    private readonly ILogger<KdpCoverCalculatorController> _logger;

    public KdpCoverCalculatorController(
        IKdpCoverDimensionService calculator,
        ILogger<KdpCoverCalculatorController> logger)
    {
        _calculator = calculator;
        _logger = logger;
    }

    /// <summary>
    /// Calculate KDP paperback full-wrap dimensions for any page count.
    /// Default: Paperback, Standard Color, White Paper, 6×9 in, bleed on, 150 DPI.
    /// </summary>
    [HttpPost("calculate")]
    [ProducesResponseType(typeof(KdpCalculateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<KdpCalculateResponse> Calculate([FromBody] KdpCalculateRequest request)
    {
        var errors = KdpCalculateRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            _logger.LogDebug("KDP calculate validation failed: {Errors}", string.Join("; ", errors));
            return BadRequest(new { errors });
        }

        var result = _calculator.Calculate(request);
        return Ok(result);
    }

    /// <summary>Runs built-in KDP calculator examples (24-page official case, dynamic spine, no-bleed).</summary>
    [HttpGet("verify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult VerifyExamples()
    {
        var failures = Application.Kdp.Verification.KdpCoverDimensionVerification.RunAll();
        return Ok(new
        {
            success = failures.Count == 0,
            passed = failures.Count == 0,
            failureCount = failures.Count,
            failures
        });
    }
}
