using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using DonateWeb.Models.Entities;

namespace DonateWeb.Services
{
    public class JwtTokenService : IJwtTokenService
    {
        private readonly IConfiguration _configuration;

        public JwtTokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateToken(User user, IEnumerable<string> roles, string? streamerSlug = null)
        {
            var secretKey = _configuration["Jwt:SecretKey"];
            if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
            {
                throw new InvalidOperationException("JWT signing key is missing or too short. Configure Jwt__SecretKey with at least 32 UTF-8 bytes using a secret store.");
            }

            var issuer = _configuration["Jwt:Issuer"] ?? "DonateWeb";
            var audience = _configuration["Jwt:Audience"] ?? "DonateWebUsers";
            var expiryHours = int.TryParse(_configuration["Jwt:ExpiryHours"], out var hours) ? hours : 24;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName ?? string.Empty),
                new Claim("AccountId", user.AccountId ?? string.Empty)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            if (!string.IsNullOrEmpty(streamerSlug))
            {
                claims.Add(new Claim("StreamerSlug", streamerSlug));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(expiryHours),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = credentials
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
