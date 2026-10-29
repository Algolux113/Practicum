using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));
        // Генератор OpenAPI использует настройки HTTP JSON, а контроллеры — MVC JSON.
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        // Ошибки привязки модели ([Required], IValidatableObject) возвращаются в том же
        // формате ProblemDetails, что и доменные ValidationException из сервисов.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var detail = string.Join("; ", context.ModelState
                    .Where(entry => entry.Value is not null)
                    .SelectMany(entry => entry.Value!.Errors)
                    .Select(error => error.ErrorMessage)
                    .Where(message => !string.IsNullOrWhiteSpace(message)));

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation error",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Detail = detail,
                    Instance = context.HttpContext.Request.Path
                };

                return new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

        services.AddOpenApi();


        return services;
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
