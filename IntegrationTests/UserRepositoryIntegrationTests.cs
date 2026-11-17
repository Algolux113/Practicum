using Domain.Models;
using Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class UserRepositoryIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task AddAsync_WithDuplicateLogin_ShouldBeRejectedByDatabase()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var original = User.Create("test-user", "first-hash");
        var repository = new UserRepository(database.Context);
        await repository.AddAsync(original);
        await database.Context.SaveChangesAsync();

        await using var duplicateContext = database.CreateContext();
        var duplicateRepository = new UserRepository(duplicateContext);
        await duplicateRepository.AddAsync(User.Create(original.Login, "second-hash"));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresError.SqlState);
        Assert.Equal("IX_users_login", postgresError.ConstraintName);

        await using var verification = database.CreateContext();
        var saved = await new UserRepository(verification).FindByLoginAsync(original.Login);
        Assert.NotNull(saved);
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(original.PasswordHash, saved.PasswordHash);
    }
}
