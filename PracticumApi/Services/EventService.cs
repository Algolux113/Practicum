using System.Threading;
using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Exceptions;

namespace PracticumApi.Services;

public class EventService() : IEventService
{
    private readonly List<Event> _events = [];

    // Сервис зарегистрирован как singleton, а обращения к нему идут из разных потоков
    // (запросы + фоновый сервис), поэтому любой доступ к _events защищён этим замком.
    private readonly Lock _sync = new();

    public PaginatedResult<Event> GetAll(
        string? title = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 10)
    {
        // Валидация параметров пагинации
        if (page < 1)
            throw new ValidationException(nameof(page), "Номер страницы должен быть больше или равен 1");

        if (pageSize < 1)
            throw new ValidationException(nameof(pageSize), "Размер страницы должен быть больше или равен 1");

        if (pageSize > 100)
            throw new ValidationException(nameof(pageSize), "Размер страницы не может превышать 100");

        List<Event> filtered;
        lock (_sync)
        {
            IEnumerable<Event> query = _events;

            if (!string.IsNullOrWhiteSpace(title))
                query = query.Where(x => !string.IsNullOrWhiteSpace(x.Title) && x.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

            if (from.HasValue)
                query = query.Where(x => x.StartAt >= from.Value);

            if (to.HasValue)
                query = query.Where(x => x.EndAt <= to.Value);

            // Материализуем отфильтрованный набор один раз: иначе фильтры и подсчёт
            // выполнялись бы дважды и могли бы разойтись между собой.
            filtered = query.ToList();
        }

        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PaginatedResult<Event>(items, filtered.Count, page, pageSize);
    }

    public Event Get(Guid id)
    {
        lock (_sync)
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            return eventItem;
        }
    }

    public void Add(Event eventItem)
    {
        eventItem.Id = Guid.NewGuid();
        lock (_sync)
            _events.Add(eventItem);
    }

    public void Update(Event eventItem)
    {
        lock (_sync)
        {
            var index = _events.FindIndex(x => x.Id == eventItem.Id);
            if (index == -1)
                throw new NotFoundException("Event", eventItem.Id);

            _events[index] = eventItem;
        }
    }

    public void Delete(Guid id)
    {
        lock (_sync)
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            _events.Remove(eventItem);
        }
    }

    public void ReserveSeats(Guid id, int count = 1)
    {
        // Проверка наличия события и резерв мест выполняются под одним замком,
        // иначе параллельные бронирования могли бы обе пройти TryReserveSeats
        // и увести AvailableSeats в минус.
        lock (_sync)
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            if (!eventItem.TryReserveSeats(count))
                throw new NoAvailableSeatsException();
        }
    }

    public void ReleaseSeats(Guid id, int count = 1)
    {
        lock (_sync)
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            eventItem.ReleaseSeats(count);
        }
    }
}
