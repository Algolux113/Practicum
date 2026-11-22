using System.Security.Claims;
using Application.DTOs;
using Application.Interfaces;
using Domain.Models;
using Domain.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Presentation.Controllers;

namespace Tests;

public class ControllerResponseTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Register_ShouldRejectAdminOutsideDevelopment(string environmentName)
    {
        var users = new Mock<IUserService>(MockBehavior.Strict);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(host => host.EnvironmentName).Returns(environmentName);
        var controller = new AuthController(users.Object, environment.Object);

        await Assert.ThrowsAsync<ForbiddenOperationException>(() =>
            controller.Register(new("test-admin", "test-password", UserRole.Admin), CancellationToken.None));
    }

    [Fact]
    public async Task Register_ShouldAllowTestAdminInDevelopment()
    {
        var users = new Mock<IUserService>(MockBehavior.Strict);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(host => host.EnvironmentName).Returns("Development");
        var controller = new AuthController(users.Object, environment.Object);
        users.Setup(service => service.RegisterAsync("test-admin", "test-password", UserRole.Admin, CancellationToken.None))
            .ReturnsAsync(Guid.NewGuid());

        var response = await controller.Register(new("test-admin", "test-password", UserRole.Admin), CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }

    [Fact]
    public async Task EventEndpoints_ShouldMapEntitiesAndPreservePaginationAndLocation()
    {
        var eventItem = Event.Create("Событие", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 3);
        var service = new Mock<IEventService>();
        service.Setup(s => s.GetAsync(eventItem.Id, It.IsAny<CancellationToken>())).ReturnsAsync(eventItem);
        service.Setup(s => s.CreateAsync(It.IsAny<EventDTO>(), It.IsAny<CancellationToken>())).ReturnsAsync(eventItem);
        service.Setup(s => s.GetAllAsync(null, null, null, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResult<Event>([eventItem], 11, 2, 10));
        var controller = new EventController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var single = (await controller.Get(eventItem.Id)).Value!;
        var page = (await controller.GetAll(page: 2)).Value!;
        var created = Assert.IsType<CreatedAtActionResult>(await controller.Create(new EventDTO()));

        Assert.Equal(EventResponse.From(eventItem), single);
        Assert.Equal(single, Assert.Single(page.Items));
        Assert.Equal(11, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(single, Assert.IsType<EventResponse>(created.Value));
        Assert.Equal(eventItem.Id, created.RouteValues!["id"]);
        Assert.Equal(nameof(EventController.Get), created.ActionName);
    }

    [Fact]
    public async Task BookingEndpoints_ShouldMapEntityAndPreserveAcceptedLocation()
    {
        var booking = Booking.Create(Guid.NewGuid(), Guid.NewGuid());
        var service = new Mock<IBookingService>();
        service.Setup(s => s.GetAsync(booking.Id, booking.UserId, UserRole.User, It.IsAny<CancellationToken>())).ReturnsAsync(booking);
        service.Setup(s => s.CreateBookingAsync(booking.EventId, booking.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(booking);
        var controller = new BookingController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", booking.UserId.ToString())], "Bearer"));

        var fetched = Assert.IsType<OkObjectResult>((await controller.Get(booking.Id)).Result);
        var accepted = Assert.IsType<AcceptedResult>(await controller.Book(booking.EventId));

        Assert.Equal(BookingResponse.From(booking), Assert.IsType<BookingResponse>(fetched.Value));
        Assert.Equal(BookingResponse.From(booking), Assert.IsType<BookingResponse>(accepted.Value));
        Assert.Equal($"/bookings/{booking.Id}", accepted.Location);
    }
}
