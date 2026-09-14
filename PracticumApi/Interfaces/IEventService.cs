using PracticumApi.Models;

namespace PracticumApi.Interfaces;

public interface IEventService
{
    public PaginatedResult<Event> GetAll(
        string? title = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 10);

    public Event Get(Guid id);

    public void Add(Event eventItem);

    public void Update(Event eventItem);

    public void Delete(Guid id);

    /// <summary>
    /// Атомарно резервирует count мест у события. Бросает NotFoundException, если
    /// событие не найдено, и NoAvailableSeatsException, если свободных мест недостаточно.
    /// </summary>
    public void ReserveSeats(Guid id, int count = 1);

    /// <summary>
    /// Атомарно возвращает count мест событию (например, при отклонении брони).
    /// Бросает NotFoundException, если событие не найдено.
    /// </summary>
    public void ReleaseSeats(Guid id, int count = 1);
}
