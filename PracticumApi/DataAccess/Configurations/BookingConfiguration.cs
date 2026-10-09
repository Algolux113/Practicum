using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PracticumApi.Models;

namespace PracticumApi.DataAccess.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.EventId).IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(b => b.CreatedAt).IsRequired();
        builder.Property(b => b.ProcessedAt).IsRequired(false);

        builder.HasOne(b => b.Event)
            .WithMany(e => e.Bookings)
            .HasForeignKey(b => b.EventId)
            .IsRequired();
    }
}
