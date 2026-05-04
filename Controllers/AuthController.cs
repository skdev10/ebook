using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtTokenService _tokenService;

        public AuthController(ApplicationDbContext context, IJwtTokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        public class LoginRequest { public string UserEmail { get; set; } = ""; public string Password { get; set; } = ""; }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.UserEmail) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { message = "Email and password required" });

            // NOTE: in production compare hashed passwords, not plain text
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == req.UserEmail && u.Password == req.Password);

            if (user == null)
                return Unauthorized(new { message = "Invalid credentials" });

            // find role name (if you have Roles table)
            string? roleName = null;
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == user.RoleId); // adjust to your Roles DbSet
            if (role != null) roleName = role.RoleName;

            var token = _tokenService.GenerateToken(user, roleName);

            return Ok(new { token, expiresInMinutes = int.Parse(_tokenServiceConfigExpiry(_tokenService)) });
        }

        // helper to read expiry if needed (not required)
        private string _tokenServiceConfigExpiry(IJwtTokenService s) => "30";
    }

}
