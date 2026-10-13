using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PracticumApi.Models;

namespace PracticumApi.DataAccess.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(b => b.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(b => b.Status).HasColumnName("status").HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.ProcessedAt).HasColumnName("processed_at").IsRequired(false);

        builder.HasOne(b => b.Event)
            .WithMany(e => e.Bookings)
            .HasForeignKey(b => b.EventId)
            .IsRequired();
    }
}
