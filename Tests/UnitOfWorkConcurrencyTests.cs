using Domain.Exceptions;
using Domain.Models;
using Infrastructure.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

public class UnitOfWorkConcurrencyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task ConcurrencyFailure_ShouldRetryWholeOperationAndBoundAttempts(int failures)
    {
        var interceptor = new ConflictInterceptor(failures);
        await using var context = CreateContext(interceptor);
        var calls = 0;
        async Task<Guid> Operation()
        {
            calls++;
            var eventItem = Event.Create("Повтор", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 1);
            await context.Events.AddAsync(eventItem);
            return eventItem.Id;
        }

        if (failures == 1)
        {
            var id = await new UnitOfWork(context).ExecuteAsync(Operation);
            Assert.Equal(2, calls);
            Assert.Equal(id, (await context.Events.SingleAsync()).Id);
        }
        else
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new UnitOfWork(context).ExecuteAsync(Operation));
            Assert.Equal(20, calls);
            Assert.Empty(context.ChangeTracker.Entries());
            Assert.Empty(await context.Events.ToListAsync());
        }
    }

    [Fact]
    public async Task CancellationDuringRetry_ShouldStopAndDiscardPendingChanges()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new ConflictInterceptor(20, cancellation);
        await using var context = CreateContext(interceptor);
        var calls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UnitOfWork(context).ExecuteAsync(async () =>
        {
            calls++;
            await context.Events.AddAsync(Event.Create("Отмена", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 1));
            return true;
        }, cancellation.Token));

        Assert.Equal(1, calls);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await context.Events.ToListAsync());
    }

    private static AppDbContext CreateContext(SaveChangesInterceptor interceptor) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor).Options);

    private sealed class ConflictInterceptor(int failures, CancellationTokenSource? cancellation = null) : SaveChangesInterceptor
    {
        private int _attempts;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++_attempts <= failures)
            {
                cancellation?.Cancel();
                throw new DbUpdateConcurrencyException("Тестовый конфликт версии");
            }
            return ValueTask.FromResult(result);
        }
    }
}
