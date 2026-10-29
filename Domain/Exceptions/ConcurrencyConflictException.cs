namespace Domain.Exceptions;

public class ConcurrencyConflictException() : Exception("Данные изменились параллельно. Повторите запрос.");
