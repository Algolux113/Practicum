using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
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
        Assert.Equal(new[] { "Pending", "Confirmed", "Rejected", "Cancelled" },
            schemas["BookingStatus"].Enum!.Select(value => value!.GetValue<string>()));
        Assert.DoesNotContain("event", schemas["BookingResponse"].Properties!.Keys);
        Assert.DoesNotContain("isDeleted", schemas["EventResponse"].Properties!.Keys);
        Assert.DoesNotContain("bookings", schemas["EventResponse"].Properties!.Keys);
        Assert.DoesNotContain("Event", schemas.Keys);
        Assert.DoesNotContain("Booking", schemas.Keys);
        Assert.Equal("bearer", document.Components.SecuritySchemes!["Bearer"].Scheme);
        Assert.Single(document.Paths["/events"].Operations![HttpMethod.Post].Security!);
        Assert.Single(document.Paths["/events/{id}/book"].Operations![HttpMethod.Post].Security!);
        Assert.Null(document.Paths["/events"].Operations![HttpMethod.Get].Security);
        Assert.Null(document.Paths["/auth/login"].Operations![HttpMethod.Post].Security);
    }
}
