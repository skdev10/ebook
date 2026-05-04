using EBookDashboard.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EBookDashboard.Interfaces
{
    public interface IJwtTokenService
    {
        string GenerateToken(Users user, string roleName);
    }

    public class JwtTokenService : IJwtTokenService
    {
        private readonly IConfiguration _config;

        public JwtTokenService(IConfiguration config)
        {
            _config = config;
        }

        public string GenerateToken(Users user, string roleName)
        {
            var jwt = _config.GetSection("JwtSettings");
            var secret = jwt["SecretKey"];
            var issuer = jwt["Issuer"];
            var audience = jwt["Audience"];
            var expiryMinutes = int.Parse(jwt["ExpiryMinutes"] ?? "30");

            var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserEmail),
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.FullName ?? user.UserEmail),
            new Claim(ClaimTypes.Role, roleName ?? user.RoleId.ToString())
        };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }


}
