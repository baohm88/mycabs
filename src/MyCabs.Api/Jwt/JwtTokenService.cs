// CHANGED: Không khai báo interface ở đây nữa.
// Implement interface từ Application để tránh trùng tên.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MyCabs.Domain.Entities;

namespace MyCabs.Api.Jwt
{
    // CHANGED: implement interface của Application
    public class JwtTokenService : MyCabs.Application.IJwtTokenService
    {
        private readonly IConfiguration _cfg;
        public JwtTokenService(IConfiguration cfg) { _cfg = cfg; }

        public string Generate(User user)
        {
            var issuer = _cfg["Jwt:Issuer"] ?? "mycabs.local";
            var audience = _cfg["Jwt:Audience"] ?? "mycabs.local";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var minutes = int.TryParse(_cfg["Jwt:AccessTokenMinutes"], out var m) ? m : 120;
            var expires = DateTime.UtcNow.AddMinutes(minutes);

            var role = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role;

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                // role claims
                new(ClaimTypes.Role, role),
                new("role", role)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
