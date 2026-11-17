using Domain.Models;

namespace Application.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default);
    Task<User?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
}
