using PracticumApi.Exceptions;

namespace PracticumApi.Models;

public class Event
{
    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public int TotalSeats { get; set; }

    public int AvailableSeats { get; set; }

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
    /// (TotalSeats - AvailableSeats) сохранилось, а результат остался в границах
    /// [0, totalSeats] — иначе смена вместимости могла бы нарушить инвариант
    /// AvailableSeats &lt;= TotalSeats или "забыть" освободившиеся места.
    /// </summary>
    public int RecalculateAvailableSeats(int totalSeats)
    {
        var takenSeats = TotalSeats - AvailableSeats;
        return Math.Clamp(totalSeats - takenSeats, 0, totalSeats);
    }
}
