using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfigurationSection section)
    {
        var jwt = section.Get<JwtOptions>() ?? new JwtOptions();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                var key = Encoding.UTF8.GetBytes(jwt.Secret);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = key.Length >= 32 ? new SymmetricSecurityKey(key) : null,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.UniqueName,
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.Zero
                };
            });
        return services;
    }

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

        services.AddOpenApi(options =>
        {
            const string schemeId = JwtBearerDefaults.AuthenticationScheme;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[schemeId] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(schemeId, context.Document)] = []
                    });
                }
                return Task.CompletedTask;
            });
        });

        return services;
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
