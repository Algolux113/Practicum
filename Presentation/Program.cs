using Application;
using Infrastructure;
using Infrastructure.Security;
using Presentation;
using Presentation.Middlewares;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddPresentation();
builder.Services.AddJwtAuthentication(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.Services.ApplyInfrastructureMigrations();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUi(options => options.DocumentPath = "/openapi/v1.json");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
