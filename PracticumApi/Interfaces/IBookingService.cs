using PracticumApi.Models;

namespace PracticumApi.Interfaces;

public interface IBookingService
{
    /// <summary>
    /// Возвращает снимок всех бронирований (копию, безопасную для перечисления).
    /// </summary>
    public List<Booking> GetAll();

    public Booking Get(Guid id);

    /// <summary>
    /// Создаёт бронь для указанного события: назначает уникальный Id, статус Pending
    /// и текущее время в CreatedAt. Если событие не найдено — выбрасывает NotFoundException.
    /// </summary>
    public Task<Booking> CreateBookingAsync(Guid eventId);

    public void Update(Booking booking);

    public void Delete(Guid id);
}
