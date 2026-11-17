using Application.Interfaces;
using Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.DataAccess.Repositories;

public sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Login == login, cancellationToken);

    public async Task<User?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tracked = context.Users.Local.FirstOrDefault(user => user.Id == id);
        if (tracked is not null)
            await context.Entry(tracked).ReloadAsync(cancellationToken);

        return await context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await context.Users.AddAsync(user, cancellationToken);
}
