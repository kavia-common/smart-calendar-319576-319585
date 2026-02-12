using System.Text.Json;
using CalendarBackend.Data;
using CalendarBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace CalendarBackend.Services;

public interface IEventNotificationScheduler
{
    Task RescheduleForEventAsync(CalendarEvent ev, CancellationToken ct);
    Task CancelForEventAsync(Guid eventId, CancellationToken ct);
}

/// <summary>
/// DB-backed scheduler that maintains notification_outbox rows per event.
/// </summary>
public sealed class EventNotificationScheduler : IEventNotificationScheduler
{
    private readonly AppDbContext _db;
    private readonly ILogger<EventNotificationScheduler> _logger;

    public EventNotificationScheduler(AppDbContext db, ILogger<EventNotificationScheduler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RescheduleForEventAsync(CalendarEvent ev, CancellationToken ct)
    {
        // For MVP: single notification at (start - 5 minutes). If past, schedule for now.
        var scheduledFor = ev.StartAt - TimeSpan.FromMinutes(5);
        if (scheduledFor < DateTimeOffset.UtcNow)
        {
            scheduledFor = DateTimeOffset.UtcNow;
        }

        // Cancel existing pending/failed notifications for this event, then create a new pending one.
        var existing = await _db.NotificationOutbox
            .Where(n => n.EventId == ev.Id && (n.Status == "pending" || n.Status == "failed" || n.Status == "processing"))
            .ToListAsync(ct);

        foreach (var n in existing)
        {
            n.Status = "cancelled";
        }

        var payload = JsonSerializer.Serialize(new
        {
            eventId = ev.Id,
            title = ev.Title,
            startAt = ev.StartAt,
            timezone = ev.Timezone,
            allDay = ev.AllDay
        });

        var outbox = new NotificationOutboxItem
        {
            EventId = ev.Id,
            ScheduledFor = scheduledFor,
            Channel = "in_app",
            PayloadJson = payload,
            Status = "pending",
            Attempts = 0,
            LastError = "",
            SentAt = null
        };

        await _db.NotificationOutbox.AddAsync(outbox, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Scheduled notification for event {EventId} at {ScheduledFor}", ev.Id, scheduledFor);
    }

    public async Task CancelForEventAsync(Guid eventId, CancellationToken ct)
    {
        var existing = await _db.NotificationOutbox
            .Where(n => n.EventId == eventId && (n.Status == "pending" || n.Status == "failed" || n.Status == "processing"))
            .ToListAsync(ct);

        if (existing.Count == 0)
        {
            return;
        }

        foreach (var n in existing)
        {
            n.Status = "cancelled";
        }

        await _db.SaveChangesAsync(ct);
    }
}
