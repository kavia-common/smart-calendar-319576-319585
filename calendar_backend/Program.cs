using CalendarBackend.Api;
using CalendarBackend.Data;
using CalendarBackend.Models;
using CalendarBackend.Services;
using Microsoft.EntityFrameworkCore;
using NSwag.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "Smart Calendar Backend API";
    config.Version = "1.0.0";
    config.Description = "Event CRUD + date-range query APIs with Postgres persistence and in-process notification scheduling.";
});

// Database (Postgres)
// Env vars provided by the 'database' container: POSTGRES_URL, POSTGRES_USER, POSTGRES_PASSWORD, POSTGRES_DB, POSTGRES_PORT.
var pgUrl = Environment.GetEnvironmentVariable("POSTGRES_URL");
var pgUser = Environment.GetEnvironmentVariable("POSTGRES_USER");
var pgPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
var pgDb = Environment.GetEnvironmentVariable("POSTGRES_DB");

if (!string.IsNullOrWhiteSpace(pgUrl) && !string.IsNullOrWhiteSpace(pgUser) && !string.IsNullOrWhiteSpace(pgPassword) && !string.IsNullOrWhiteSpace(pgDb))
{
    // POSTGRES_URL example: postgresql://localhost:5000/myapp
    // We build a standard Npgsql connection string.
    var uri = new Uri(pgUrl);
    var host = uri.Host;
    var port = uri.Port > 0 ? uri.Port : 5432;
    var database = uri.AbsolutePath.TrimStart('/');
    if (string.IsNullOrWhiteSpace(database))
    {
        database = pgDb;
    }

    var connectionString = $"Host={host};Port={port};Database={database};Username={pgUser};Password={pgPassword};Pooling=true;";

    builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));
}
else
{
    // If not present, app still starts, but endpoints will fail with a clear error.
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseNpgsql("Host=localhost;Port=5432;Database=missing_env;Username=missing;Password=missing;"));
}

builder.Services.AddScoped<IEventNotificationScheduler, EventNotificationScheduler>();
builder.Services.AddHostedService<NotificationDispatcherService>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        // Prefer explicit origins from env (ALLOWED_ORIGINS) when present, else allow all.
        var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(_ => true)
                .AllowCredentials()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Use CORS
app.UseCors("AllowAll");

// Configure OpenAPI/Swagger
app.UseOpenApi();
app.UseSwaggerUi(config =>
{
    config.Path = "/docs";
});

// Best-effort: also persist the OpenAPI document to a local file for tooling that expects it.
try
{
    var doc = await app.Services.GetRequiredService<NSwag.AspNetCore.OpenApiDocumentProvider>()
        .GenerateAsync(app.Services);
    var json = doc.ToJson();
    var outPath = Path.Combine(AppContext.BaseDirectory, "openapi.json");
    await File.WriteAllTextAsync(outPath, json);
}
catch
{
    // Non-fatal: runtime swagger still works at /swagger/v1/swagger.json (NSwag pipeline) and /docs UI.
}

// Health check endpoint
app.MapGet("/", () => new { message = "Healthy" })
    .WithSummary("Health check")
    .WithDescription("Basic health check endpoint.")
    .WithTags("System");

// Helper to ensure DB env is configured (friendly error for callers)
static IResult DbNotConfigured()
    => Results.Problem(
        title: "Database not configured",
        detail: "Missing required Postgres environment variables: POSTGRES_URL, POSTGRES_USER, POSTGRES_PASSWORD, POSTGRES_DB.",
        statusCode: StatusCodes.Status500InternalServerError);

// Events routes (primary base: /api/events)
var eventsGroup = app.MapGroup("/api/events").WithTags("Events");

// Compatibility alias to support frontend fallback: /events
var eventsGroupAlias = app.MapGroup("/events").WithTags("Events");

// GET /api/events?start=...&end=...
async Task<IResult> ListEventsInRange(HttpRequest req, AppDbContext db, CancellationToken ct)
{
    if (db.Database.GetDbConnection().Database == "missing_env")
    {
        return DbNotConfigured();
    }

    // start/end are optional ISO-8601 strings.
    DateTimeOffset? start = null;
    DateTimeOffset? end = null;

    if (req.Query.TryGetValue("start", out var startRaw) && !string.IsNullOrWhiteSpace(startRaw))
    {
        if (!DateTimeOffset.TryParse(startRaw!, out var parsed))
        {
            return Results.BadRequest(new ErrorResponse("Invalid 'start' query param. Use ISO-8601 date-time."));
        }
        start = parsed;
    }

    if (req.Query.TryGetValue("end", out var endRaw) && !string.IsNullOrWhiteSpace(endRaw))
    {
        if (!DateTimeOffset.TryParse(endRaw!, out var parsed))
        {
            return Results.BadRequest(new ErrorResponse("Invalid 'end' query param. Use ISO-8601 date-time."));
        }
        end = parsed;
    }

    // If both present, validate range
    if (start.HasValue && end.HasValue && end.Value < start.Value)
    {
        return Results.BadRequest(new ErrorResponse("'end' must be >= 'start'."));
    }

    var query = db.Events.AsNoTracking().AsQueryable();

    // "Overlap" filtering: return events that overlap [start, end]
    if (start.HasValue)
    {
        query = query.Where(e => e.EndAt >= start.Value);
    }

    if (end.HasValue)
    {
        query = query.Where(e => e.StartAt <= end.Value);
    }

    var events = await query
        .OrderBy(e => e.StartAt)
        .Take(2000)
        .ToListAsync(ct);

    var result = events.Select(e => new EventDto(
        e.Id,
        e.Title,
        e.Description,
        e.StartAt,
        e.EndAt,
        e.Timezone,
        e.AllDay,
        e.Location,
        e.Color,
        e.CreatedAt,
        e.UpdatedAt
    ));

    return Results.Ok(result);
}

// POST /api/events
async Task<IResult> CreateEvent(CreateEventRequest request, AppDbContext db, IEventNotificationScheduler scheduler, CancellationToken ct)
{
    if (db.Database.GetDbConnection().Database == "missing_env")
    {
        return DbNotConfigured();
    }

    var (ok, error) = EventValidation.Validate(request.Title, request.StartAt, request.EndAt);
    if (!ok)
    {
        return Results.BadRequest(new ErrorResponse(error!));
    }

    var ev = new CalendarEvent
    {
        Title = request.Title.Trim(),
        Description = request.Description?.Trim() ?? "",
        StartAt = request.StartAt,
        EndAt = request.EndAt,
        Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim(),
        AllDay = request.AllDay,
        Location = request.Location?.Trim() ?? "",
        Color = request.Color?.Trim() ?? ""
    };

    db.Events.Add(ev);
    await db.SaveChangesAsync(ct);

    // Schedule notification(s)
    await scheduler.RescheduleForEventAsync(ev, ct);

    var dto = new EventDto(
        ev.Id,
        ev.Title,
        ev.Description,
        ev.StartAt,
        ev.EndAt,
        ev.Timezone,
        ev.AllDay,
        ev.Location,
        ev.Color,
        ev.CreatedAt,
        ev.UpdatedAt
    );

    return Results.Ok(dto);
}

// PUT /api/events/{id}
async Task<IResult> UpdateEvent(string id, UpdateEventRequest request, AppDbContext db, IEventNotificationScheduler scheduler, CancellationToken ct)
{
    if (db.Database.GetDbConnection().Database == "missing_env")
    {
        return DbNotConfigured();
    }

    if (!Guid.TryParse(id, out var guid))
    {
        return Results.BadRequest(new ErrorResponse("Invalid event id (must be UUID)."));
    }

    var (ok, error) = EventValidation.Validate(request.Title, request.StartAt, request.EndAt);
    if (!ok)
    {
        return Results.BadRequest(new ErrorResponse(error!));
    }

    var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == guid, ct);
    if (ev is null)
    {
        return Results.NotFound(new ErrorResponse("Event not found."));
    }

    ev.Title = request.Title.Trim();
    ev.Description = request.Description?.Trim() ?? "";
    ev.StartAt = request.StartAt;
    ev.EndAt = request.EndAt;
    ev.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim();
    ev.AllDay = request.AllDay;
    ev.Location = request.Location?.Trim() ?? "";
    ev.Color = request.Color?.Trim() ?? "";

    await db.SaveChangesAsync(ct);

    await scheduler.RescheduleForEventAsync(ev, ct);

    var dto = new EventDto(
        ev.Id,
        ev.Title,
        ev.Description,
        ev.StartAt,
        ev.EndAt,
        ev.Timezone,
        ev.AllDay,
        ev.Location,
        ev.Color,
        ev.CreatedAt,
        ev.UpdatedAt
    );

    return Results.Ok(dto);
}

// DELETE /api/events/{id}
async Task<IResult> DeleteEvent(string id, AppDbContext db, IEventNotificationScheduler scheduler, CancellationToken ct)
{
    if (db.Database.GetDbConnection().Database == "missing_env")
    {
        return DbNotConfigured();
    }

    if (!Guid.TryParse(id, out var guid))
    {
        return Results.BadRequest(new ErrorResponse("Invalid event id (must be UUID)."));
    }

    var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == guid, ct);
    if (ev is null)
    {
        return Results.NotFound(new ErrorResponse("Event not found."));
    }

    db.Events.Remove(ev);
    await db.SaveChangesAsync(ct);

    await scheduler.CancelForEventAsync(guid, ct);

    return Results.Ok(new { deleted = true });
}

eventsGroup.MapGet("", ListEventsInRange)
    .WithSummary("List events (optionally within a date range)")
    .WithDescription("Returns events; when start/end are provided, returns events overlapping the range. Query params: start, end (ISO-8601).")
    .Produces<IEnumerable<EventDto>>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

eventsGroup.MapPost("", CreateEvent)
    .WithSummary("Create an event")
    .WithDescription("Creates an event and schedules a notification (MVP: 5 minutes before start).")
    .Produces<EventDto>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

eventsGroup.MapPut("/{id}", UpdateEvent)
    .WithSummary("Update an event")
    .WithDescription("Updates an existing event and reschedules notifications.")
    .Produces<EventDto>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

eventsGroup.MapDelete("/{id}", DeleteEvent)
    .WithSummary("Delete an event")
    .WithDescription("Deletes an event and cancels pending notifications.")
    .Produces(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

// Register the alias routes pointing to the same handlers.
eventsGroupAlias.MapGet("", ListEventsInRange);
eventsGroupAlias.MapPost("", CreateEvent);
eventsGroupAlias.MapPut("/{id}", UpdateEvent);
eventsGroupAlias.MapDelete("/{id}", DeleteEvent);

app.Run();
