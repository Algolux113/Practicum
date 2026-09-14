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

    // Параллельные задачи обрабатывают разные брони одновременно; семафор сериализует
    // только сам вызов BookingService.Update (он и так потокобезопасен изнутри, но здесь
    // мы явно гарантируем, что в один момент времени пишет только одна задача). Проверка
    // события и Confirm()/Reject() — не запись в это хранилище, поэтому семафор их не держит.
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

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
        var eventService = scope.ServiceProvider.GetRequiredService<IEventService>();

        var pendingBookings = bookingService.GetAll()
            .Where(b => b.Status == BookingStatus.Pending)
            .ToList();

        if (pendingBookings.Count == 0)
            return;

        _logger.LogInformation("Найдено {Count} бронирований в статусе Pending.", pendingBookings.Count);

        // Каждая бронь обрабатывается независимо, поэтому имитацию обращения к внешней
        // системе запускаем параллельно, а не ждём 2 секунды на каждую бронь по очереди.
        var processingTasks = pendingBookings.Select(booking =>
            ProcessBookingAsync(bookingService, eventService, booking, cancellationToken));

        await Task.WhenAll(processingTasks);
    }

    private async Task ProcessBookingAsync(
        IBookingService bookingService,
        IEventService eventService,
        Booking booking,
        CancellationToken cancellationToken)
    {
        // По этому логу видно, что брони стартуют одновременно, а не по очереди.
        _logger.LogInformation(
            "Начата обработка брони {BookingId} (событие {EventId}, поток {ThreadId}).",
            booking.Id,
            booking.EventId,
            Environment.CurrentManagedThreadId);

        // Имитация обращения к внешней системе выполняется до захвата семафора,
        // чтобы задержки для разных броней не блокировали друг друга.
        await Task.Delay(ProcessingDelay, cancellationToken);

        try
        {
            // Событие могли удалить, пока бронь ждала обработки — тогда подтверждать нечего.
            if (!EventExists(eventService, booking.EventId))
            {
                booking.Reject();
                await UpdateBookingAsync(bookingService, booking, cancellationToken);

                _logger.LogWarning(
                    "Событие {EventId} для брони {BookingId} не найдено, бронь отклонена.",
                    booking.EventId,
                    booking.Id);
                return;
            }

            booking.Confirm();
            await UpdateBookingAsync(bookingService, booking, cancellationToken);

            _logger.LogInformation("Бронь {BookingId} переведена в статус Confirmed.", booking.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            // Бронь удалили, пока мы её обрабатывали — это не ошибка,
            // просто пропускаем и продолжаем с остальными.
            _logger.LogInformation("Бронь {BookingId} исчезла до завершения обработки, пропускаем.", booking.Id);
        }
        catch (Exception ex)
        {
            // Неожиданная ошибка при подтверждении: откатываемся — отклоняем бронь
            // и возвращаем место в событие, чтобы оно не пропало впустую.
            _logger.LogError(ex, "Не удалось обработать бронь {BookingId}, отклоняем и возвращаем место.", booking.Id);

            booking.Reject();

            try
            {
                eventService.ReleaseSeats(booking.EventId);
            }
            catch (NotFoundException)
            {
                // Событие уже удалено — освобождать место некуда.
            }

            try
            {
                await UpdateBookingAsync(bookingService, booking, cancellationToken);
            }
            catch (NotFoundException)
            {
                // Бронь тоже удалили — сохранять отказ уже некуда.
            }
        }
    }

    private static bool EventExists(IEventService eventService, Guid eventId)
    {
        try
        {
            eventService.Get(eventId);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }

    private async Task UpdateBookingAsync(IBookingService bookingService, Booking booking, CancellationToken cancellationToken)
    {
        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            bookingService.Update(booking);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public override void Dispose()
    {
        _writeSemaphore.Dispose();
        base.Dispose();
    }
}