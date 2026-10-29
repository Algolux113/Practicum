using Domain.Models;
using Infrastructure.DataAccess;
using Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

public class UnitOfWorkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOperation_ShouldDiscardChangesAndAllowNextOperation(bool failOnSave)
    {
        // Arrange
        var failure = new SaveFailure();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(failure).Options;
        await using var context = new AppDbContext(options);
        var original = Event.Create("Исходное событие", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 2);
        context.Events.Add(original);
        await context.SaveChangesAsync();
        var events = new EventRepository(context);
        var bookings = new BookingRepository(context);
        var unitOfWork = new UnitOfWork(context);
        failure.Enabled = failOnSave;

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteAsync(async () =>
        {
            var eventItem = (await events.GetForUpdateAsync(original.Id))!;
            eventItem.TryReserveSeats();
            await bookings.AddAsync(Booking.Create(eventItem.Id));
            if (!failOnSave)
                throw new InvalidOperationException("Сбой операции");
            return true;
        }));

        // Assert
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(2, (await events.GetAsync(original.Id))!.AvailableSeats);
        failure.Enabled = false;
        await unitOfWork.ExecuteAsync(async () =>
        {
            var next = Event.Create("Следующее событие", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 1);
            await events.AddAsync(next);
            return true;
        });
        Assert.Equal(2, await context.Events.CountAsync());
        Assert.Empty(await context.Bookings.ToListAsync());
        Assert.Equal(2, (await events.GetAsync(original.Id))!.AvailableSeats);
    }

    private sealed class SaveFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
                throw new InvalidOperationException("Сбой сохранения");
            return ValueTask.FromResult(result);
        }
    }
}
