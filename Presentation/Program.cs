using Application;
using Infrastructure;
using Presentation;
using Presentation.Middlewares;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddPresentation();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.Services.ApplyInfrastructureMigrations();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUi(options => options.DocumentPath = "/openapi/v1.json");
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();