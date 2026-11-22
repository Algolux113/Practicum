using System.Text.Json;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Presentation.Middlewares;

namespace Tests;

public class ConcurrencyConflictResponseTests
{
    [Fact]
    public async Task InvalidCredentials_ShouldReturn400ProblemDetails()
    {
        using var provider = new ServiceCollection().AddOptions().BuildServiceProvider();
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = body;
        var middleware = new GlobalExceptionHandlingMiddleware(_ => throw new InvalidCredentialsException(),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Неверный логин или пароль.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ConcurrencyConflict_ShouldReturn409ProblemDetails()
    {
        using var provider = new ServiceCollection().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var middleware = new GlobalExceptionHandlingMiddleware(_ => throw new ConcurrencyConflictException(),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.Equal(409, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Concurrency conflict", json.RootElement.GetProperty("title").GetString());
    }
}
