using FarmIQ.Core.Entities;
using FarmIQ.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FarmIQ.Infrastructure.Persistence;

public sealed class FarmIQDbContext(DbContextOptions<FarmIQDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<FarmerProfile> FarmerProfiles => Set<FarmerProfile>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<InboundMessage> InboundMessages => Set<InboundMessage>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<CropAdvisory> CropAdvisories => Set<CropAdvisory>();
    public DbSet<AdvisoryDiagnosis> AdvisoryDiagnoses => Set<AdvisoryDiagnosis>();
    public DbSet<WeatherSnapshot> WeatherSnapshots => Set<WeatherSnapshot>();
    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();
    public DbSet<ChannelConnection> ChannelConnections => Set<ChannelConnection>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict<Guid>();

        builder.Entity<FarmerProfile>(entity =>
        {
            entity.HasIndex(x => x.ExternalFarmerId).IsUnique();
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.PreferredLanguage).HasMaxLength(16);
        });

        builder.Entity<Conversation>(entity =>
        {
            entity.HasIndex(x => new { x.ChannelType, x.ExternalConversationId }).IsUnique();
            entity.Property(x => x.ChannelType).HasConversion<int>();
        });

        builder.Entity<InboundMessage>(entity =>
        {
            entity.HasIndex(x => new { x.ChannelType, x.ExternalMessageId }).IsUnique();
            entity.Property(x => x.ChannelType).HasConversion<int>();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.RawPayloadJson).HasColumnType("text");
            entity.Property(x => x.NormalizedMetadataJson).HasColumnType("text");
            entity.Property(x => x.DuplicateOfMessageId).HasMaxLength(128);
        });

        builder.Entity<MediaAsset>(entity =>
        {
            entity.Property(x => x.MediaType).HasConversion<int>();
            entity.Property(x => x.SourceUrl).HasColumnType("text");
        });

        builder.Entity<CropAdvisory>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<int>();
            entity.HasOne(x => x.Diagnosis)
                .WithOne(x => x.CropAdvisory)
                .HasForeignKey<AdvisoryDiagnosis>(x => x.CropAdvisoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.WeatherSnapshot)
                .WithOne(x => x.CropAdvisory)
                .HasForeignKey<WeatherSnapshot>(x => x.CropAdvisoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OutboundMessage>(entity =>
        {
            entity.Property(x => x.ChannelType).HasConversion<int>();
            entity.Property(x => x.DeliveryStatus).HasConversion<int>();
        });

        builder.Entity<ChannelConnection>(entity =>
        {
            entity.Property(x => x.ChannelType).HasConversion<int>();
        });

        builder.Entity<ProcessingJob>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<int>();
            entity.HasIndex(x => new { x.Status, x.NextAttemptUtc });
            entity.HasIndex(x => x.LeaseExpiresUtc);
        });

        builder.Entity<WebhookDelivery>(entity =>
        {
            entity.Property(x => x.ChannelType).HasConversion<int>();
            entity.HasIndex(x => x.DeliveryKey).IsUnique();
            entity.Property(x => x.RawPayloadJson).HasColumnType("text");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not Core.Common.BaseEntity entity)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entity.CreatedUtc = now;
                entity.UpdatedUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entity.UpdatedUtc = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
