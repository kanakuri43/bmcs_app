using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class SlipNumberSequenceConfiguration : IEntityTypeConfiguration<SlipNumberSequence>
{
    public void Configure(EntityTypeBuilder<SlipNumberSequence> builder)
    {
        builder.ToTable("slip_number_sequences");

        builder.HasKey(e => e.SequenceKey);

        builder.Property(e => e.SequenceKey).HasMaxLength(30).IsUnicode(false);

        builder.ConfigureTrackedColumns();
    }
}
