using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Presentation;
using Presentation.Controllers;

namespace Tests;

public class OpenApiContractTests
{
    [Fact]
    public async Task OpenApi_ShouldDescribeStringStatusesAndHideInternalProperties()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BookingController).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.Services.AddPresentation();
        await using var app = builder.Build();
        app.MapControllers();

        // Act
        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync();
        var schemas = document.Components!.Schemas!;

        // Assert
        Assert.NotEmpty(document.Paths);
        Assert.Equal(new[] { "Pending", "Confirmed", "Rejected" },
            schemas["BookingStatus"].Enum!.Select(value => value!.GetValue<string>()));
        Assert.DoesNotContain("event", schemas["BookingResponse"].Properties!.Keys);
        Assert.DoesNotContain("isDeleted", schemas["EventResponse"].Properties!.Keys);
        Assert.DoesNotContain("bookings", schemas["EventResponse"].Properties!.Keys);
        Assert.DoesNotContain("Event", schemas.Keys);
        Assert.DoesNotContain("Booking", schemas.Keys);
    }
}
