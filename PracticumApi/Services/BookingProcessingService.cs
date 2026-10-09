using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Services;

/// <summary>
/// Фоновый сервис, который периодически опрашивает хранилище на предмет бронирований
/// в статусе <see cref="BookingStatus.Pending"/> и переводит их в статус Confirmed,
/// имитируя обработку внешней системой.
/// </summary>
public class BookingProcessingService(
    IServiceScopeFactory scopeFactory,
    ILogger<BookingProcessingService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<BookingProcessingService> _logger = logger;

    /// <summary>
    /// Интервал между циклами опроса хранилища.
    /// </summary>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Искусственная задержка, имитирующая обращение к внешней системе.
    /// </summary>
    private static readonly TimeSpan ProcessingDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Фоновый сервис обработки бронирований запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingBookingsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Нормальное завершение работы при остановке приложения.
                break;
            }
            catch (Exception ex)
            {
                // Ошибка не должна останавливать фоновый сервис — логируем и продолжаем.
                _logger.LogError(ex, "Произошла ошибка при обработке бронирований.");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Фоновый сервис обработки бронирований остановлен.");
    }

    private async Task ProcessPendingBookingsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var pendingBookings = (await bookingService.GetAllAsync(cancellationToken))
            .Where(b => b.Status == BookingStatus.Pending)
            .ToList();

        if (pendingBookings.Count == 0)
            return;

        _logger.LogInformation("Найдено {Count} бронирований в статусе Pending.", pendingBookings.Count);

        // Каждая бронь обрабатывается независимо, поэтому имитацию обращения к внешней
        // системе запускаем параллельно, а не ждём 2 секунды на каждую бронь по очереди.
        var processingTasks = pendingBookings.Select(booking =>
            ProcessBookingAsync(booking, cancellationToken));

        await Task.WhenAll(processingTasks);
    }

    private async Task ProcessBookingAsync(
        Booking booking,
        CancellationToken cancellationToken)
    {
        // По этому логу видно, что брони стартуют одновременно, а не по очереди.
        _logger.LogInformation(
            "Начата обработка брони {BookingId} (событие {EventId}, поток {ThreadId}).",
            booking.Id,
            booking.EventId,
            Environment.CurrentManagedThreadId);

        // Задержки для разных броней выполняются параллельно, до обращения к БД.
        await Task.Delay(ProcessingDelay, cancellationToken);

        // DbContext не потокобезопасен: каждой параллельной задаче — собственный scope.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
        try
        {
            // Проверка события и переход статуса выполняются по свежим данным под общим семафором.
            var status = await bookingService.ProcessPendingAsync(booking.Id, cancellationToken);
            _logger.LogInformation("Бронь {BookingId} обработана: {Status}.", booking.Id, status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("Бронь {BookingId} исчезла до завершения обработки.", booking.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обработать бронь {BookingId}, отклоняем и возвращаем место.", booking.Id);

            // После сбоя SaveChanges используем новый контекст, без несохранённых изменений.
            await using var recoveryScope = _scopeFactory.CreateAsyncScope();
            var recoveryService = recoveryScope.ServiceProvider.GetRequiredService<IBookingService>();
            try
            {
                await recoveryService.RejectAsync(booking.Id, cancellationToken);
            }
            catch (NotFoundException)
            {
                // Бронь удалена — сохранять отказ уже некуда.
            }
        }
    }
}
