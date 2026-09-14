using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Exceptions;

namespace PracticumApi.Services;

public class EventService() : IEventService, IDisposable
{
    private readonly List<Event> _events = [];

    // Сервис — singleton, к нему обращаются запросы и фоновый сервис из разных потоков.
    // ReaderWriterLockSlim вместо обычного lock: чтения (GetAll/Get) не блокируют друг
    // друга, эксклюзивным остаётся только доступ на запись (Add/Update/Delete/Reserve/Release).
    private readonly ReaderWriterLockSlim _sync = new();

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
        _sync.EnterReadLock();
        try
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
        finally
        {
            _sync.ExitReadLock();
        }

        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PaginatedResult<Event>(items, filtered.Count, page, pageSize);
    }

    public Event Get(Guid id)
    {
        _sync.EnterReadLock();
        try
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            return eventItem;
        }
        finally
        {
            _sync.ExitReadLock();
        }
    }

    public void Add(Event eventItem)
    {
        eventItem.Id = Guid.NewGuid();
        _sync.EnterWriteLock();
        try
        {
            _events.Add(eventItem);
        }
        finally
        {
            _sync.ExitWriteLock();
        }
    }

    public void Update(Event eventItem)
    {
        _sync.EnterWriteLock();
        try
        {
            var index = _events.FindIndex(x => x.Id == eventItem.Id);
            if (index == -1)
                throw new NotFoundException("Event", eventItem.Id);

            _events[index] = eventItem;
        }
        finally
        {
            _sync.ExitWriteLock();
        }
    }

    public void Delete(Guid id)
    {
        _sync.EnterWriteLock();
        try
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            _events.Remove(eventItem);
        }
        finally
        {
            _sync.ExitWriteLock();
        }
    }

    public void ReserveSeats(Guid id, int count = 1)
    {
        // Резерв места мутирует AvailableSeats, поэтому нужен эксклюзивный write-lock:
        // иначе два читателя могли бы одновременно пройти TryReserveSeats и увести
        // AvailableSeats в минус.
        _sync.EnterWriteLock();
        try
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            if (!eventItem.TryReserveSeats(count))
                throw new NoAvailableSeatsException();
        }
        finally
        {
            _sync.ExitWriteLock();
        }
    }

    public void ReleaseSeats(Guid id, int count = 1)
    {
        _sync.EnterWriteLock();
        try
        {
            var eventItem = _events.FirstOrDefault(x => x.Id == id);
            if (eventItem is null)
                throw new NotFoundException("Event", id);

            eventItem.ReleaseSeats(count);
        }
        finally
        {
            _sync.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _sync.Dispose();
        GC.SuppressFinalize(this);
    }
}
