using Microsoft.EntityFrameworkCore;
using OpenTicket.Api.Contracts;
using OpenTicket.Domain.Entities;
using OpenTicket.Infrastructure.Persistence;

namespace OpenTicket.Api.Endpoints;

public static class VenueEndpoints
{
    public static void MapVenueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/venues").WithTags("Venues");

        group.MapPost("", async (CreateVenueRequest request, AppDbContext db) =>
        {
            var venue = new Venue
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                City = request.City
            };

            db.Venues.Add(venue);
            await db.SaveChangesAsync();

            return Results.Created($"/api/venues/{venue.Id}", new VenueResponse(venue.Id, venue.Name, venue.City));
        });

        group.MapPost("/{id:guid}/seats/bulk", async (Guid id, BulkCreateSeatsRequest request, AppDbContext db) =>
        {
            var venueExists = await db.Venues.AnyAsync(v => v.Id == id);
            if (!venueExists)
            {
                return Results.NotFound($"Venue {id} not found.");
            }

            var seats = new List<Seat>();
            foreach (var row in request.Rows)
            {
                for (var seatNumber = row.SeatFrom; seatNumber <= row.SeatTo; seatNumber++)
                {
                    seats.Add(new Seat
                    {
                        Id = Guid.NewGuid(),
                        VenueId = id,
                        Section = row.Section,
                        RowLabel = row.RowLabel,
                        SeatNumber = seatNumber
                    });
                }
            }

            db.Seats.AddRange(seats);
            await db.SaveChangesAsync();

            return Results.Ok(seats.Select(s => new SeatResponse(s.Id, s.Section, s.RowLabel, s.SeatNumber)));
        });
    }
}
