using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTicket.Domain.Entities;

namespace OpenTicket.Infrastructure.Persistence.Configurations;

public class EventSeatConfiguration : IEntityTypeConfiguration<EventSeat>
{
    public void Configure(EntityTypeBuilder<EventSeat> builder)
    {
        builder.HasKey(es => es.Id);
        builder.HasIndex(es => new { es.EventId, es.SeatId }).IsUnique();
        builder.HasIndex(es => new { es.Status, es.HoldExpiresAtUtc });

        builder.Property(es => es.Version)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasOne(es => es.Event)
            .WithMany(e => e.EventSeats)
            .HasForeignKey(es => es.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(es => es.Seat)
            .WithMany()
            .HasForeignKey(es => es.SeatId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
