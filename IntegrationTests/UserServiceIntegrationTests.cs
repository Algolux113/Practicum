using System.IdentityModel.Tokens.Jwt;
using Application.Services;
using Domain.Exceptions;
using Domain.Models;
using Infrastructure.DataAccess;
using Infrastructure.DataAccess.Repositories;
using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class UserServiceIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RegisterAndLogin_ShouldPersistHashedPasswordAndIssueToken()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var context = database.Context;
        var hasher = new Pbkdf2PasswordHasher();
        var tokens = new JwtTokenService(Options.Create(new JwtOptions
        {
            Secret = "test-signing-key-with-at-least-32-bytes",
            Issuer = "test-issuer",
            Audience = "test-audience",
            LifetimeMinutes = 15
        }));
        var users = new UserService(new UserRepository(context), hasher, tokens, new UnitOfWork(context));

        var id = await users.RegisterAsync("test-user", "password");
        var stored = await context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.Equal(UserRole.User, stored.Role);
        Assert.True(hasher.Verify("password", stored.PasswordHash));
        Assert.False(hasher.NeedsRehash(stored.PasswordHash));
        await Assert.ThrowsAsync<LoginAlreadyExistsException>(() => users.RegisterAsync("test-user", "other"));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => users.LoginAsync("test-user", "other"));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => users.LoginAsync("missing", "password"));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(await users.LoginAsync("test-user", "password"));
        Assert.Equal(id.ToString(), token.Subject);

        const string legacyHash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        var legacyUser = User.Create("legacy-user", legacyHash);
        context.Users.Add(legacyUser);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => users.LoginAsync(legacyUser.Login, "wrong"));
        var repository = new UserRepository(context);
        Assert.Equal(legacyHash, (await repository.FindByLoginAsync(legacyUser.Login))!.PasswordHash);
        await users.LoginAsync(legacyUser.Login, "abc");
        var upgraded = (await repository.FindByLoginAsync(legacyUser.Login))!.PasswordHash;
        Assert.False(hasher.NeedsRehash(upgraded));
        Assert.True(hasher.Verify("abc", upgraded));
    }
}
