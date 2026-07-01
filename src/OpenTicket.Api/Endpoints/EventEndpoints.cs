using Microsoft.EntityFrameworkCore;
using OpenTicket.Api.Contracts;
using OpenTicket.Domain.Entities;
using OpenTicket.Domain.Enums;
using OpenTicket.Infrastructure.Persistence;

namespace OpenTicket.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events").WithTags("Events");

        group.MapPost("", async (CreateEventRequest request, AppDbContext db) =>
        {
            var venue = await db.Venues.FindAsync(request.VenueId);
            if (venue is null)
            {
                return Results.NotFound($"Venue {request.VenueId} not found.");
            }

            var @event = new Event
            {
                Id = Guid.NewGuid(),
                VenueId = request.VenueId,
                Title = request.Title,
                StartsAtUtc = request.StartsAtUtc,
                Status = EventStatus.Draft
            };

            db.Events.Add(@event);
            await db.SaveChangesAsync();

            return Results.Created($"/api/events/{@event.Id}",
                new EventResponse(@event.Id, venue.Id, venue.Name, @event.Title, @event.StartsAtUtc, @event.Status.ToString()));
        });

        group.MapPost("/{id:guid}/publish", async (Guid id, PublishEventRequest request, AppDbContext db) =>
        {
            var @event = await db.Events.Include(e => e.Venue).FirstOrDefaultAsync(e => e.Id == id);
            if (@event is null)
            {
                return Results.NotFound();
            }

            if (@event.Status != EventStatus.Draft)
            {
                return Results.Conflict($"Event is already {@event.Status}.");
            }

            var seatIds = await db.Seats
                .Where(s => s.VenueId == @event.VenueId)
                .Select(s => s.Id)
                .ToListAsync();

            if (seatIds.Count == 0)
            {
                return Results.BadRequest("Venue has no seats to publish.");
            }

            var eventSeats = seatIds.Select(seatId => new EventSeat
            {
                Id = Guid.NewGuid(),
                EventId = @event.Id,
                SeatId = seatId,
                PriceCents = request.PriceCents,
                Status = SeatStatus.Available
            });

            db.EventSeats.AddRange(eventSeats);
            @event.Status = EventStatus.OnSale;

            await db.SaveChangesAsync();

            return Results.Ok(new EventResponse(@event.Id, @event.VenueId, @event.Venue!.Name, @event.Title, @event.StartsAtUtc, @event.Status.ToString()));
        });

        group.MapGet("", async (AppDbContext db) =>
        {
            var events = await db.Events
                .Include(e => e.Venue)
                .OrderBy(e => e.StartsAtUtc)
                .Select(e => new EventResponse(e.Id, e.VenueId, e.Venue!.Name, e.Title, e.StartsAtUtc, e.Status.ToString()))
                .ToListAsync();

            return Results.Ok(events);
        });

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var @event = await db.Events
                .Include(e => e.Venue)
                .Where(e => e.Id == id)
                .Select(e => new EventResponse(e.Id, e.VenueId, e.Venue!.Name, e.Title, e.StartsAtUtc, e.Status.ToString()))
                .FirstOrDefaultAsync();

            return @event is null ? Results.NotFound() : Results.Ok(@event);
        });

        group.MapGet("/{id:guid}/seats", async (Guid id, AppDbContext db) =>
        {
            var eventExists = await db.Events.AnyAsync(e => e.Id == id);
            if (!eventExists)
            {
                return Results.NotFound();
            }

            var seats = await db.EventSeats
                .Include(es => es.Seat)
                .Where(es => es.EventId == id)
                .OrderBy(es => es.Seat!.Section).ThenBy(es => es.Seat!.RowLabel).ThenBy(es => es.Seat!.SeatNumber)
                .Select(es => new EventSeatResponse(es.Id, es.SeatId, es.Seat!.Section, es.Seat!.RowLabel, es.Seat!.SeatNumber, es.PriceCents, es.Status.ToString()))
                .ToListAsync();

            return Results.Ok(seats);
        });
    }
}
