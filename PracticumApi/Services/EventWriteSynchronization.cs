namespace PracticumApi.Services;

internal static class EventWriteSynchronization
{
    // Общая секция для scoped-сервисов: резервирование, изменение вместимости и возврат мест.
    // Синхронизация действует только внутри одного процесса приложения.
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}
