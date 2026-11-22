using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Interfaces;
using Domain.Models;
using Infrastructure.DataAccess;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public class EventAuthorizationTests
{
    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task CreateEvent_ShouldRequireAdmin(bool authenticated, HttpStatusCode expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (authenticated)
        {
            var tokenService = factory.Services.GetRequiredService<IJwtTokenService>();
            var user = User.Create("test-user", "test-hash", UserRole.User);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenService.GenerateToken(user));
        }

        var start = DateTime.UtcNow.AddDays(1);
        using var response = await client.PostAsJsonAsync("/events", new
        {
            title = "Тестовое событие",
            startAt = start,
            endAt = start.AddHours(1),
            totalSeats = 10
        });

        Assert.Equal(expected, response.StatusCode);
    }
}
