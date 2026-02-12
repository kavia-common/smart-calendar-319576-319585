namespace CalendarBackend.Api;

public sealed record EventDto(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Timezone,
    bool AllDay,
    string Location,
    string Color,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateEventRequest(
    string Title,
    string? Description,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Timezone,
    bool AllDay,
    string? Location,
    string? Color
);

public sealed record UpdateEventRequest(
    string Title,
    string? Description,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Timezone,
    bool AllDay,
    string? Location,
    string? Color
);

public static class EventValidation
{
    public static (bool ok, string? error) Validate(string title, DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (false, "Title is required.");
        }

        if (endAt < startAt)
        {
            return (false, "endAt must be greater than or equal to startAt.");
        }

        return (true, null);
    }
}
