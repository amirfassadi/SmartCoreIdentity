using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace SmartCore.Identity;

public sealed record AccessProof(Guid PersonId,Guid SessionId,long Epoch,string ClientId);
public sealed record SessionTokens(Guid SessionId,string AccessToken,string RefreshToken,DateTimeOffset ExpiresAt,DateTimeOffset AccessExpiresAt);

// Development issuer/validator, deliberately independent of provisioning/material keys.
public sealed class AccessTokens
{
    private readonly SigningCredentials signing;
    private readonly TokenValidationParameters validation;
    private readonly JwtSecurityTokenHandler handler=new() {MapInboundClaims=false};
    private readonly TimeProvider clock;
    public AccessTokens(string key,TimeProvider clock)
    {
        var bytes=Convert.FromBase64String(key);
        if(bytes.Length!=32) throw new InvalidOperationException("A 32-byte access signing key is required.");
        this.clock=clock;
        var securityKey=new SymmetricSecurityKey(bytes);
        signing=new(securityKey,SecurityAlgorithms.HmacSha256);
        validation=new()
        {
            ValidateIssuer=true,ValidIssuer="smartcore-development-identity",
            ValidateAudience=true,ValidAudience="smartcore-development-bff",
            ValidateIssuerSigningKey=true,IssuerSigningKey=securityKey,RequireSignedTokens=true,
            RequireExpirationTime=true,ValidateLifetime=true,ClockSkew=TimeSpan.Zero,
            ValidAlgorithms=new[]{SecurityAlgorithms.HmacSha256},
            LifetimeValidator=(start,end,_,_)=>start is not null && end is not null
                && start.Value<=clock.GetUtcNow().UtcDateTime && end.Value>clock.GetUtcNow().UtcDateTime
        };
    }
    public SessionTokens Issue(Guid person,Guid session,long epoch,string client,string refresh,DateTimeOffset deadline)
    {
        var now=DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        var end=DateTimeOffset.FromUnixTimeSeconds(Math.Min(now.AddSeconds(900).ToUnixTimeSeconds(),deadline.ToUnixTimeSeconds()));
        if(end<=now) throw new ApiError(401,"UNAUTHORIZED");
        var claims=new[]{new Claim("sub",person.ToString()),new Claim("sid",session.ToString()),
            new Claim("epoch",epoch.ToString(CultureInfo.InvariantCulture)),new Claim("client",client)};
        var jwt=new JwtSecurityToken(validation.ValidIssuer,validation.ValidAudience,claims,now.UtcDateTime,end.UtcDateTime,signing);
        return new(session,handler.WriteToken(jwt),refresh,deadline,end);
    }
    public AccessProof Read(string token,string client)
    {
        try
        {
            if(token.Length>4096) throw new ApiError(401,"UNAUTHORIZED");
            var claims=handler.ValidateToken(token,validation,out _);
            if(!Guid.TryParse(claims.FindFirstValue("sub"),out var person) || !Guid.TryParse(claims.FindFirstValue("sid"),out var session)
                || !long.TryParse(claims.FindFirstValue("epoch"),NumberStyles.None,CultureInfo.InvariantCulture,out var epoch)
                || epoch<0 || claims.FindFirstValue("client")!=client) throw new ApiError(401,"UNAUTHORIZED");
            return new(person,session,epoch,client);
        }
        catch(Exception e) when(e is SecurityTokenException or ArgumentException or FormatException)
        {throw new ApiError(401,"UNAUTHORIZED");}
    }
}
