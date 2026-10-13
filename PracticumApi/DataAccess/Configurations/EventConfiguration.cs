using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PracticumApi.Models;

namespace PracticumApi.DataAccess.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.Title).HasColumnName("title").IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasColumnName("description").IsRequired(false).HasMaxLength(2000);
        builder.Property(e => e.StartAt).HasColumnName("start_at").IsRequired();
        builder.Property(e => e.EndAt).HasColumnName("end_at").IsRequired();
        builder.Property(e => e.TotalSeats).HasColumnName("total_seats").IsRequired();
        builder.Property(e => e.AvailableSeats).HasColumnName("available_seats").IsRequired();
        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();

        builder.HasMany(e => e.Bookings)
            .WithOne(b => b.Event)
            .HasForeignKey(b => b.EventId)
            .IsRequired();
    }
}
