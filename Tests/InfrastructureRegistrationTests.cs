using Application.Interfaces;
using Infrastructure;
using Infrastructure.DataAccess;
using Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_ShouldRegisterScopedPersistenceAndDiscoverMigrations()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure("Host=localhost;Database=practicum;Username=postgres;Password=postgres");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var context = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.IsType<EventRepository>(firstScope.ServiceProvider.GetRequiredService<IEventRepository>());
        Assert.IsType<BookingRepository>(firstScope.ServiceProvider.GetRequiredService<IBookingRepository>());
        Assert.Same(context, firstScope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.NotSame(context, secondScope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.All(services.Where(descriptor => descriptor.ServiceType == typeof(IEventRepository) ||
                                               descriptor.ServiceType == typeof(IBookingRepository)),
            descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Contains("20261013060334_InitialCreate", context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
