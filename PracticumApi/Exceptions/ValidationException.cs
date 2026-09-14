namespace PracticumApi.Exceptions;

/// <summary>
/// Исключение, которое выбрасывается при ошибках валидации данных
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(string fieldName, string message)
        : base($"Ошибка валидации поля '{fieldName}': {message}")
    {
    }
}
