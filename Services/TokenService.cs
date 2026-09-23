using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Services;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "PragmaticBot.Server";
    public string Audience { get; set; } = "PragmaticBot";
    public int LifetimeHours { get; set; } = 24;
}

public class TokenService(JwtOptions options)
{
    /// <summary>Claim carrying the single-login session id this token was minted for.</summary>
    public const string SessionIdClaim = "sid";

    /// <summary>Claim carrying the bot version this token was minted for (버전 게이트용).</summary>
    public const string VersionClaim = "ver";

    public SymmetricSecurityKey SigningKey { get; } =
        new(Encoding.UTF8.GetBytes(options.Key));

    public string Create(User user, string sessionId, string? clientVersion = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(SessionIdClaim, sessionId),
            new(VersionClaim, clientVersion ?? string.Empty),
            new(ClaimTypes.Name, user.DisplayName)
        };
        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(options.LifetimeHours),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
