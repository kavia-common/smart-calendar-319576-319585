namespace CalendarBackend.Models;

public sealed class NotificationOutboxItem
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public DateTimeOffset ScheduledFor { get; set; }

    public string Channel { get; set; } = "in_app";

    /// <summary>
    /// Stored as JSONB in Postgres. We keep it as a raw JSON string for simplicity and flexibility.
    /// </summary>
    public string PayloadJson { get; set; } = "{}";

    public string Status { get; set; } = "pending";

    public int Attempts { get; set; }

    public string LastError { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
