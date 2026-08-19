namespace PracticumApi.Exceptions;

/// <summary>
/// Исключение, которое выбрасывается когда ресурс не найден
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string resourceName, int id) 
        : base($"{resourceName} с ID {id} не найден")
    {
    }
}
