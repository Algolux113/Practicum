using Microsoft.AspNetCore.Mvc;
using PracticumApi.Exceptions;

namespace PracticumApi.Middlewares;

public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleException(httpContext, ex);
        }
    }

    private async Task HandleException(HttpContext httpContext, Exception ex)
    {
        var (statusCode, title, type) = MapStatusCode(ex);

        var requestId = httpContext.Request.Headers.TryGetValue("x-request-id", out var header)
            && !string.IsNullOrWhiteSpace(header)
                ? header.ToString()
                : httpContext.TraceIdentifier;

        // Доменные исключения (4xx) — это ожидаемый результат, а не сбой сервера:
        // логируем их как Warning, а стеки трасс приберегаем для настоящих 5xx.
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                ex,
                "Unhandled exception. Method={Method}, Path={Path}, RequestId={RequestId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                requestId);
        }
        else
        {
            _logger.LogWarning(
                "Handled domain exception ({StatusCode}). Method={Method}, Path={Path}, RequestId={RequestId}, Detail={Detail}",
                statusCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                requestId,
                ex.Message);
        }

        if (httpContext.Response.HasStarted)
        {
            return;
        }

        var error = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = ex.Message,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(error, options: null, contentType: "application/problem+json");
    }

    private static (int statusCode, string title, string type) MapStatusCode(Exception ex)
        => ex switch
        {
            NotFoundException => (
                StatusCodes.Status404NotFound,
                "Resource not found",
                "https://tools.ietf.org/html/rfc7231#section-6.5.4"),
            Exceptions.ValidationException => (
                StatusCodes.Status400BadRequest,
                "Validation error",
                "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            NoAvailableSeatsException => (
                StatusCodes.Status409Conflict,
                "No available seats",
                "https://tools.ietf.org/html/rfc7231#section-6.5.8"),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "https://tools.ietf.org/html/rfc7231#section-6.6.1")
        };
}
