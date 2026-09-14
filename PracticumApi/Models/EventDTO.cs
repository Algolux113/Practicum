using System.ComponentModel.DataAnnotations;

namespace PracticumApi.Models;

public class EventDTO : IValidatableObject
{
    [Required(ErrorMessage = "Поле \"Title\" обязательно.")]
    public string? Title { get; set; }

    public string? Description { get; set; }

    [Required(ErrorMessage = "Поле \"StartAt\" обязательно.")]
    public DateTime? StartAt { get; set; }

    [Required(ErrorMessage = "Поле \"EndAt\" обязательно.")]
    public DateTime? EndAt { get; set; }

    [Required(ErrorMessage = "Поле \"TotalSeats\" обязательно.")]
    [Range(1, int.MaxValue, ErrorMessage = "Поле \"TotalSeats\" должно быть больше 0.")]
    public int? TotalSeats { get; set; }

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
