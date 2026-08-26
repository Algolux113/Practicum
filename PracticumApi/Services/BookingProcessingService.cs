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
    private static readonly TimeSpan ExternalSystemDelay = TimeSpan.FromSeconds(2);

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
        using var scope = _scopeFactory.CreateScope();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();

        var pendingBookings = bookingService.GetAll()
            .Where(b => b.Status == BookingStatus.Pending)
            .ToList();

        if (pendingBookings.Count == 0)
            return;

        _logger.LogInformation("Найдено {Count} бронирований в статусе Pending.", pendingBookings.Count);

        foreach (var booking in pendingBookings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Имитация обращения к внешней системе.
            await Task.Delay(ExternalSystemDelay, cancellationToken);

            booking.Status = BookingStatus.Confirmed;
            booking.ProcessedAt = DateTime.UtcNow;

            bookingService.Update(booking);

            _logger.LogInformation("Бронь {BookingId} переведена в статус Confirmed.", booking.Id);
        }
    }
}