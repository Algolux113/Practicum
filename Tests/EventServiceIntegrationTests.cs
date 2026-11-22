using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.DataAccess;
using Infrastructure.DataAccess.Repositories;
using Application.Interfaces;
using Application.Services;
using Domain.Models;

namespace Tests;

/// <summary>
/// Интеграционные тесты для EventService с использованием реальной реализации.
/// Класс разбит на несколько файлов (partial):
/// EventServiceCrudTests.cs — Add/Get/Update/Delete,
/// EventServiceFilteringTests.cs — фильтрация по названию и датам,
/// EventServicePaginationTests.cs — пагинация и её валидация,
/// EventServiceDateHandlingTests.cs — поведение при некорректных датах.
/// </summary>
public partial class EventServiceIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly Guid _userId;

    public EventServiceIntegrationTests()
    {
        // Имя общее для всех scope этого теста; xUnit создаёт новый экземпляр на каждый тест.
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IUserRepository, UserRepository>();
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        _scope = _serviceProvider.CreateScope();
        var context = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = User.Create("test-user", "test-hash");
        context.Users.Add(user);
        context.SaveChanges();
        _userId = user.Id;
    }

    private IEventService CreateEventService() => _scope.ServiceProvider.GetRequiredService<IEventService>();

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }
}
