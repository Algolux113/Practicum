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
}
