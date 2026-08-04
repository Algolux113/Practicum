namespace PracticumApi.Models;

public class Event
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }
}
