using Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.DataAccess.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    internal const string LoginIndexName = "IX_users_login";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(user => user.Login).HasColumnName("login").IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(user => user.Role).HasColumnName("role").HasConversion<string>().IsRequired();
        builder.Property<Guid>("Version").HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(user => user.Login).HasDatabaseName(LoginIndexName).IsUnique();
    }
}
