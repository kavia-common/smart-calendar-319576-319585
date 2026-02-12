namespace CalendarBackend.Models;

public sealed class CalendarEvent
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public string Timezone { get; set; } = "UTC";

    public bool AllDay { get; set; }

    public string Location { get; set; } = "";

    public string Color { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
