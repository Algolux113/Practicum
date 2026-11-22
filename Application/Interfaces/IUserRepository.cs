using Domain.Models;

namespace Application.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default);
    Task<User?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Проверяет наличие пользователя и защищает создание брони от конкурентных изменений при сохранении.
    /// Возвращает false, если пользователь не найден.
    /// </summary>
    Task<bool> LockUserForBookingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);
}
