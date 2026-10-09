using System.ComponentModel.DataAnnotations;
using PracticumApi.Models;

namespace PracticumTests;

public class EventDtoValidationTests
{
    [Theory]
    [InlineData(200, 2000, true)]
    [InlineData(201, 2000, false)]
    [InlineData(200, 2001, false)]
    public void Validate_ShouldEnforceDatabaseStringLimits(int titleLength, int descriptionLength, bool valid)
    {
        var dto = new EventDTO
        {
            Title = new string('a', titleLength),
            Description = new string('b', descriptionLength),
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddHours(1),
            TotalSeats = 1
        };
        var errors = new List<ValidationResult>();
        Assert.Equal(valid, Validator.TryValidateObject(dto, new ValidationContext(dto), errors, true));
    }
}
