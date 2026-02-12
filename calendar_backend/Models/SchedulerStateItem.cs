namespace CalendarBackend.Models;

public sealed class SchedulerStateItem
{
    public string Key { get; set; } = "";

    public string ValueJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}
