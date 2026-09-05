using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CharServer;

/// <summary>
/// Who is asking. This server does not issue tokens and cannot — signing people in belongs to the login
/// service, and having two programs able to mint identity is exactly the thing worth avoiding. It only
/// CHECKS, with the same secret, which is the whole reason that service issues a token rather than handing
/// out a session nobody else can read.
/// </summary>
public sealed class Auth
{
    readonly TokenValidationParameters rules;
    readonly JwtSecurityTokenHandler handler = new();

    public Auth(string jwtSecret)
    {
        rules = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // The login service does not stamp an issuer or an audience, so demanding either would refuse
            // every token it has ever made. What must hold is the signature and the clock, and both do.
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            // No grace period. The default is five minutes, which quietly keeps an expired token working.
            ClockSkew = TimeSpan.Zero,
        };
    }

    /// <summary>The account the token speaks for, or null for anything that is not a token this login service
    /// signed and that has not expired. One answer, so nothing downstream has to decide what "sort of valid"
    /// means.</summary>
    /// <summary>Which SIGN-IN this pass belongs to, or null on a pass issued before sign-ins were marked. The
    /// login service writes a fresh one on every sign-in, so a pass carrying an older one is a machine that has
    /// since been taken over — which is the only thing that stops one account being played in two places, and
    /// the only place it can be stopped, because further down nothing knows the two are the same person.</summary>
    public string? SessionIdFrom(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            return null;

        var token = authorizationHeader[7..];
        if (token.Length == 0) return null;

        try
        {
            var claims = handler.ValidateToken(token, rules, out _);
            var sid = claims.FindFirst("sessionId")?.Value;
            return string.IsNullOrEmpty(sid) ? null : sid;
        }
        catch
        {
            return null;
        }
    }

    public string? AccountIdFrom(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader)) return null;
        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal)) return null;

        var token = authorizationHeader[7..];
        if (token.Length == 0) return null;

        try
        {
            var claims = handler.ValidateToken(token, rules, out _);
            var id = claims.FindFirst("accountId")?.Value;
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch
        {
            // Anything at all wrong with it is the same answer to the caller. Saying WHICH way a token failed
            // tells whoever sent it how to get closer.
            return null;
        }
    }
}
