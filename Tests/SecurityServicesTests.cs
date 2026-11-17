using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Application.Interfaces;
using Domain.Models;
using Infrastructure;
using Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Tests;

public class SecurityServicesTests
{
    [Fact]
    public void PasswordHasher_ShouldUseUniqueSaltsAndVerifyLegacyHashes()
    {
        var hasher = new Pbkdf2PasswordHasher();
        const string legacy = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        var hash = hasher.Hash("abc");

        Assert.NotEqual(hash, hasher.Hash("abc"));
        Assert.True(hasher.Verify("abc", hash));
        Assert.False(hasher.Verify("abcd", hash));
        Assert.False(hasher.NeedsRehash(hash));
        Assert.True(hasher.Verify("abc", legacy.ToLowerInvariant()));
        Assert.False(hasher.Verify("abcd", legacy));
        Assert.False(hasher.Verify("abc", new string('Z', legacy.Length)));
        Assert.True(hasher.NeedsRehash(legacy));
    }

    [Fact]
    public void JwtTokenService_ShouldSignUserClaimsWithConfiguredSettings()
    {
        const string secret = "test-signing-key-with-at-least-32-bytes";
        var services = new ServiceCollection();
        services.AddInfrastructure("Host=localhost;Database=practicum");
        services.Configure<JwtOptions>(options =>
        {
            options.Secret = secret;
            options.Issuer = "test-issuer";
            options.Audience = "test-audience";
            options.LifetimeMinutes = 15;
        });
        using var provider = services.BuildServiceProvider();
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        var user = User.Create("admin", hasher.Hash("password"), UserRole.Admin);

        var token = provider.GetRequiredService<IJwtTokenService>().GenerateToken(user);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuerSigningKey = true,
            ValidIssuer = "test-issuer",
            ValidAudience = "test-audience",
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero
        }, out var validatedToken);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(user.Login, principal.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value);
        Assert.Equal("Admin", principal.FindFirst("role")?.Value);
        Assert.Equal(TimeSpan.FromMinutes(15), validatedToken.ValidTo - validatedToken.ValidFrom);
    }
}
