using Domain.Models;

namespace Application.Interfaces;

public interface IUserService
{
    Task<Guid> RegisterAsync(string login, string password, UserRole role = UserRole.User,
        CancellationToken cancellationToken = default);
    Task<string> LoginAsync(string login, string password, CancellationToken cancellationToken = default);
}
