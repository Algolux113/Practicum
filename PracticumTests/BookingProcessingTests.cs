using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PracticumApi.DataAccess;
using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Services;

namespace PracticumTests;

public class BookingProcessingTests
{
    [Fact]
    public async Task FailedConfirmation_ShouldRecoverInFreshScopeAndReturnSeat()
    {
        // Реальные сервисы; перехватчик имитирует сбой БД при сохранении подтверждения.
        var fault = new ConfirmationFailure();
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName).AddInterceptors(fault));
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IBookingService, BookingService>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Guid bookingId;
        Guid eventId;
        using (var scope = provider.CreateScope())
        {
            var eventItem = Event.Create("Сбой подтверждения", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 1);
            await scope.ServiceProvider.GetRequiredService<IEventService>().AddAsync(eventItem);
            eventId = eventItem.Id;
            bookingId = (await scope.ServiceProvider.GetRequiredService<IBookingService>()
                .CreateBookingAsync(eventId)).Id;
        }

        using var worker = new BookingProcessingService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BookingProcessingService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await fault.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        using var verification = provider.CreateScope();
        var bookings = verification.ServiceProvider.GetRequiredService<IBookingService>();
        var events = verification.ServiceProvider.GetRequiredService<IEventService>();
        Assert.Equal(BookingStatus.Rejected, (await bookings.GetAsync(bookingId)).Status);
        Assert.Equal(1, (await events.GetAsync(eventId)).AvailableSeats);
        Assert.NotNull(fault.FailedContext);
        Assert.NotEqual(fault.FailedContext, fault.RecoveryContext);
    }

    private sealed class ConfirmationFailure : SaveChangesInterceptor
    {
        public Guid? FailedContext { get; private set; }
        public Guid? RecoveryContext { get; private set; }
        public TaskCompletionSource Recovered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (FailedContext is null && context.ChangeTracker.Entries<Booking>()
                .Any(e => e.Entity.Status == BookingStatus.Confirmed))
            {
                FailedContext = context.ContextId.InstanceId;
                throw new DbUpdateException("Тестовый сбой сохранения подтверждения.");
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<Booking>().Any(e => e.Entity.Status == BookingStatus.Rejected))
            {
                RecoveryContext = context.ContextId.InstanceId;
                Recovered.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }
}
