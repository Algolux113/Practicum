namespace PracticumApi.Models;

public static class UtcDateTime
{
    // Даты без часового пояса в API трактуются как UTC.
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
