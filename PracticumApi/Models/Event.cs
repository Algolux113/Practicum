using PracticumApi.Exceptions;
using System.Text.Json.Serialization;

namespace PracticumApi.Models;

public class Event
{
    private Event() { }

    public Event(string? title)
    {
        Title = title;
    }

    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public int TotalSeats { get; set; }

    public int AvailableSeats { get; set; }

    [JsonIgnore]
    public bool IsDeleted { get; set; }

    public ICollection<Booking> Bookings { get; set; } = [];

    /// <summary>
    /// Создаёт новое событие. AvailableSeats при создании равно totalSeats.
    /// </summary>
    public static Event Create(string? title, string? description, DateTime startAt, DateTime endAt, int totalSeats)
    {
        if (totalSeats <= 0)
            throw new ValidationException(nameof(TotalSeats), "Количество мест должно быть больше 0.");

        return new Event
        {
            Title = title,
            Description = description,
            StartAt = startAt,
            EndAt = endAt,
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
        AvailableSeats = Math.Min(AvailableSeats + count, TotalSeats);
    }

    /// <summary>
    /// Вычисляет AvailableSeats для нового totalSeats так, чтобы число уже занятых мест
    /// (TotalSeats - AvailableSeats) сохранилось. Бросает ValidationException, если
    /// totalSeats меньше уже занятых мест — иначе уменьшение вместимости молча оставило
    /// бы лишние Pending-брони, которые фоновый сервис потом подтвердил бы поверх лимита.
    /// </summary>
    public int RecalculateAvailableSeats(int totalSeats)
    {
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
