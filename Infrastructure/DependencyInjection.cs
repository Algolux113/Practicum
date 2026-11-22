using System.Text;
using Application.Interfaces;
using Infrastructure.DataAccess;
using Infrastructure.DataAccess.Repositories;
using Infrastructure.Hosting;
using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddHostedService<BookingProcessingService>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddOptions<JwtOptions>()
            .Validate(options => !string.IsNullOrWhiteSpace(options.Secret) &&
                                 Encoding.UTF8.GetByteCount(options.Secret) >= 32,
                "Секрет JWT должен содержать не менее 32 байт в UTF-8.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer) &&
                                 !string.IsNullOrWhiteSpace(options.Audience),
                "Издатель и аудитория JWT обязательны.")
            .Validate(options => options.LifetimeMinutes > 0,
                "Время жизни JWT должно быть положительным.")
            .ValidateOnStart();

        return services;
    }

    public static void ApplyInfrastructureMigrations(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (context.Database.IsRelational())
            context.Database.Migrate();
    }
}
