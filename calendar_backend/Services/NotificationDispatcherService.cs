using System.Text.Json;
using CalendarBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace CalendarBackend.Services;

/// <summary>
/// Background service that dispatches due notifications from the DB-backed outbox.
/// For this MVP, dispatch == mark as sent (simulating "in_app" notifications).
/// </summary>
public sealed class NotificationDispatcherService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationDispatcherService> _logger;

    public NotificationDispatcherService(IServiceProvider serviceProvider, ILogger<NotificationDispatcherService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Simple polling loop; avoids external infra.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueNotifications(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification dispatcher failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessDueNotifications(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;

        // Pull a small batch of due notifications. We use "pending" only for now.
        var due = await db.NotificationOutbox
            .Where(n => n.Status == "pending" && n.ScheduledFor <= now)
            .OrderBy(n => n.ScheduledFor)
            .Take(50)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var item in due)
        {
            // "processing" state is best-effort without explicit locking. It's OK for this in-process worker.
            item.Status = "processing";
        }

        await db.SaveChangesAsync(ct);

        foreach (var item in due)
        {
            try
            {
                // Simulate dispatch; keep payload parse just to validate JSON.
                _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(item.PayloadJson) ? "{}" : item.PayloadJson);

                item.Status = "sent";
                item.SentAt = DateTimeOffset.UtcNow;
                item.LastError = "";
            }
            catch (Exception ex)
            {
                item.Attempts += 1;
                item.Status = "failed";
                item.LastError = ex.Message;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
