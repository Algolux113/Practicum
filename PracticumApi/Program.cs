using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PracticumApi.Interfaces;
using PracticumApi.Middlewares;
using PracticumApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<IEventService, EventService>();
builder.Services.AddSingleton<IBookingService, BookingService>();

// Регистрация фонового сервиса, обрабатывающего бронирования в статусе Pending.
builder.Services.AddHostedService<BookingProcessingService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        // Перечисления (BookingStatus) сериализуются строками: "Pending", "Confirmed", "Rejected".
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Ошибки привязки модели ([Required], IValidatableObject) возвращаются в том же
// формате ProblemDetails, что и доменные ValidationException из сервисов.
builder.Services.Configure<ApiBehaviorOptions>(options =>
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

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUi(options =>
    {
        options.DocumentPath = "/openapi/v1.json";
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
