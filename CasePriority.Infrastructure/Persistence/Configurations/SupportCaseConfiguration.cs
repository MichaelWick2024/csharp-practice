using CasePriority.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CasePriority.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SupportCase"/> to the SupportCases table. EF materializes via
/// the parameterized constructor and private setters, so the domain needs no
/// persistence-only setters. Version is an application-managed concurrency
/// token; Priority is computed and not stored.
/// </summary>
public sealed class SupportCaseConfiguration : IEntityTypeConfiguration<SupportCase>
{
    public void Configure(EntityTypeBuilder<SupportCase> builder)
    {
        builder.ToTable("SupportCases", table =>
        {
            table.HasCheckConstraint("CK_SupportCases_Severity", "[Severity] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_SupportCases_Version", "[Version] >= 1");
        });

        builder.HasKey(supportCase => supportCase.CaseNumber);

        builder.Property(supportCase => supportCase.CaseNumber)
            .HasMaxLength(SupportCase.MaxCaseNumberLength)
            .IsRequired()
            .ValueGeneratedNever()
            // Case-insensitive collation preserves "ABC-1" == "abc-1" and allows
            // indexed comparisons without ToLower() in every query.
            .UseCollation("SQL_Latin1_General_CP1_CI_AS");

        builder.Property(supportCase => supportCase.Subject)
            .HasMaxLength(SupportCase.MaxSubjectLength)
            .IsRequired();

        builder.Property(supportCase => supportCase.Severity)
            .IsRequired();

        builder.Property(supportCase => supportCase.IsOpen)
            .IsRequired();

        builder.Property(supportCase => supportCase.IsExecutiveEscalation)
            .IsRequired();

        // Application-managed concurrency token. IsConcurrencyToken() puts the
        // original value in the UPDATE's WHERE; HasField reads/writes the private
        // _version backing field (the property has no public setter).
        builder.Property(supportCase => supportCase.Version)
            .HasField("_version")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("Version")
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // Computed from severity + escalation — not stored.
        builder.Ignore(supportCase => supportCase.Priority);
    }
}
