using System.ComponentModel.DataAnnotations;

namespace PracticumApi.Models;

public class EventDTO : IValidatableObject
{
    [Required]
    public string? Title { get; set; }

    public string? Description { get; set; }

    [Required]
    public DateTime? StartAt { get; set; }

    [Required]
    public DateTime? EndAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndAt <= StartAt)
        {
            yield return new ValidationResult(
                "Дата окончания должна быть больше даты начала.",
                [nameof(EndAt)]
            );
        }
    }
}
