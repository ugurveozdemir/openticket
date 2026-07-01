using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTicket.Domain.Entities;

namespace OpenTicket.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.Property(p => p.Provider).HasMaxLength(50).IsRequired();
    }
}
