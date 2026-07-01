using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTicket.Domain.Entities;

namespace OpenTicket.Infrastructure.Persistence.Configurations;

public class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.VenueId, s.Section, s.RowLabel, s.SeatNumber }).IsUnique();
        builder.Property(s => s.Section).HasMaxLength(50).IsRequired();
        builder.Property(s => s.RowLabel).HasMaxLength(10).IsRequired();

        builder.HasOne(s => s.Venue)
            .WithMany(v => v.Seats)
            .HasForeignKey(s => s.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
