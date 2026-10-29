using Domain.Exceptions;

namespace Domain.Models;

public class Event
{
    private Event() { }

    public Guid Id { get; private set; }

    public string? Title { get; private set; }

    public string? Description { get; private set; }

    public DateTime StartAt { get; private set; }

    public DateTime EndAt { get; private set; }

    public int TotalSeats { get; private set; }

    public int AvailableSeats { get; private set; }

    public bool IsDeleted { get; private set; }

    private readonly List<Booking> _bookings = [];
    public IReadOnlyCollection<Booking> Bookings => _bookings.AsReadOnly();

    /// <summary>
    /// Создаёт новое событие. AvailableSeats при создании равно totalSeats.
    /// </summary>
    public static Event Create(string? title, string? description, DateTime startAt, DateTime endAt, int totalSeats)
    {
        if (totalSeats <= 0)
            throw new ValidationException(nameof(TotalSeats), "Количество мест должно быть больше 0.");

        return new Event
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            StartAt = Normalize(startAt),
            EndAt = Normalize(endAt),
            TotalSeats = totalSeats,
            AvailableSeats = totalSeats,
        };
    }

    /// <summary>
    /// Пытается зарезервировать <paramref name="count"/> мест. Возвращает false, если
    /// свободных мест недостаточно, иначе уменьшает AvailableSeats и возвращает true.
    /// </summary>
    public bool TryReserveSeats(int count = 1)
    {
        ValidateSeatCount(count);
        if (count > AvailableSeats)
            return false;

        AvailableSeats -= count;
        return true;
    }

    /// <summary>
    /// Освобождает <paramref name="count"/> мест (например, при отклонении брони).
    /// AvailableSeats не может превысить TotalSeats.
    /// </summary>
    public void ReleaseSeats(int count = 1)
    {
        ValidateSeatCount(count);
        AvailableSeats = (int)Math.Min((long)AvailableSeats + count, TotalSeats);
    }

    private static void ValidateSeatCount(int count)
    {
        if (count <= 0)
            throw new ValidationException(nameof(count), "Количество мест должно быть больше 0.");
    }

    /// <summary>
    /// Обновляет событие целиком, сохраняя занятые места. При ошибке состояние не меняется.
    /// </summary>
    public void Update(string? title, string? description, DateTime startAt, DateTime endAt, int totalSeats)
    {
        var availableSeats = RecalculateAvailableSeats(totalSeats);
        var normalizedStart = Normalize(startAt);
        var normalizedEnd = Normalize(endAt);
        Title = title;
        Description = description;
        StartAt = normalizedStart;
        EndAt = normalizedEnd;
        TotalSeats = totalSeats;
        AvailableSeats = availableSeats;
    }

    public void Delete() => IsDeleted = true;

    private static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private int RecalculateAvailableSeats(int totalSeats)
    {
        if (totalSeats <= 0)
            throw new ValidationException(nameof(TotalSeats), "Количество мест должно быть больше 0.");
        var takenSeats = TotalSeats - AvailableSeats;
        if (totalSeats < takenSeats)
        {
            throw new ValidationException(
                nameof(TotalSeats),
                $"Нельзя уменьшить количество мест ниже уже забронированных ({takenSeats}).");
        }

        return totalSeats - takenSeats;
    }
}
