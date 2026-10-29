using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.DataAccess;

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    private const int MaxAttempts = 20;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await operation();
                if (context.ChangeTracker.HasChanges())
                    await context.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                // SaveChanges атомарен: при конфликте бронь и резерв не сохраняются.
                // Перечитываем данные и повторяем бизнес-операцию целиком.
                context.ChangeTracker.Clear();
                if (attempt >= MaxAttempts)
                    throw new ConcurrencyConflictException();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1, 25) * attempt), cancellationToken);
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }
    }
}
