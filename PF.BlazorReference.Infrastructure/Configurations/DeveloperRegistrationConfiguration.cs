using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Infrastructure.Configurations;

/// <summary>
/// Maps the Core entity onto the DeveloperRegistrations table in schema.sql. Hand-written, never
/// scaffolded. The unique email index lives in schema.sql, not here, because the schema owns it.
/// </summary>
public sealed class DeveloperRegistrationConfiguration : IEntityTypeConfiguration<DeveloperRegistration>
{
    public void Configure(EntityTypeBuilder<DeveloperRegistration> builder)
    {
        builder.ToTable("DeveloperRegistrations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(DeveloperRegistration.NameMaxLength);
        builder.Property(r => r.Email).IsRequired().HasMaxLength(DeveloperRegistration.EmailMaxLength);
        builder.Property(r => r.Status).HasConversion<string>().IsRequired();

        // SQLite stores DateTime as text and reads it back with Kind Unspecified; restore UTC.
        builder.Property(r => r.CreatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(r => r.LastUpdatedUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    }
}
