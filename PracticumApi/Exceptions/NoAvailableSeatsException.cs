namespace PracticumApi.Exceptions;

/// <summary>
/// Исключение, которое выбрасывается, когда для события не осталось свободных мест
/// </summary>
public class NoAvailableSeatsException() : Exception("Нет свободных мест на это событие")
{
}
