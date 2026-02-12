using CalendarBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace CalendarBackend.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<NotificationOutboxItem> NotificationOutbox => Set<NotificationOutboxItem>();
    public DbSet<SchedulerStateItem> SchedulerState => Set<SchedulerStateItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CalendarEvent>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.StartAt).HasColumnName("start_at");
            entity.Property(e => e.EndAt).HasColumnName("end_at");
            entity.Property(e => e.Timezone).HasColumnName("timezone");
            entity.Property(e => e.AllDay).HasColumnName("all_day");
            entity.Property(e => e.Location).HasColumnName("location");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.StartAt).HasDatabaseName("idx_events_start_at");
            entity.HasIndex(e => e.EndAt).HasDatabaseName("idx_events_end_at");
        });

        modelBuilder.Entity<NotificationOutboxItem>(entity =>
        {
            entity.ToTable("notification_outbox");
            entity.HasKey(n => n.Id);

            entity.Property(n => n.Id).HasColumnName("id");
            entity.Property(n => n.EventId).HasColumnName("event_id");
            entity.Property(n => n.ScheduledFor).HasColumnName("scheduled_for");
            entity.Property(n => n.Channel).HasColumnName("channel");
            entity.Property(n => n.PayloadJson).HasColumnName("payload");
            entity.Property(n => n.Status).HasColumnName("status");
            entity.Property(n => n.Attempts).HasColumnName("attempts");
            entity.Property(n => n.LastError).HasColumnName("last_error");
            entity.Property(n => n.CreatedAt).HasColumnName("created_at");
            entity.Property(n => n.UpdatedAt).HasColumnName("updated_at");
            entity.Property(n => n.SentAt).HasColumnName("sent_at");

            entity.HasIndex(n => new { n.Status, n.ScheduledFor }).HasDatabaseName("idx_notification_outbox_due");
        });

        modelBuilder.Entity<SchedulerStateItem>(entity =>
        {
            entity.ToTable("scheduler_state");
            entity.HasKey(s => s.Key);

            entity.Property(s => s.Key).HasColumnName("key");
            entity.Property(s => s.ValueJson).HasColumnName("value");
            entity.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
