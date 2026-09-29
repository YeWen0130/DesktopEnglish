using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;

namespace DesktopEnglish.Data;

/// <summary>
/// EF Core context backing every persisted artefact of the application.
/// Registered through <c>AddDbContextFactory</c> so short-lived instances can be used from both the UI
/// thread and background services without sharing change trackers.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Word> Words => Set<Word>();

    public DbSet<LearningRecord> LearningRecords => Set<LearningRecord>();

    public DbSet<WordReviewState> WordReviewStates => Set<WordReviewState>();

    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Word>(entity =>
        {
            entity.ToTable("Words");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.WordText).IsRequired().HasMaxLength(128);
            entity.HasIndex(w => w.WordText).IsUnique();
            entity.Property(w => w.Phonetic).HasMaxLength(256);
            entity.Property(w => w.Meaning).HasMaxLength(2048);
            entity.Property(w => w.PartOfSpeech).HasMaxLength(128);
            entity.Property(w => w.Example).HasMaxLength(1024);
            entity.Property(w => w.Category).HasMaxLength(128);
            entity.HasIndex(w => w.Category);
            entity.HasIndex(w => w.Level);
        });

        modelBuilder.Entity<LearningRecord>(entity =>
        {
            entity.ToTable("LearningRecords");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Result).HasConversion<int>();
            entity.Property(r => r.Algorithm).HasConversion<int>();
            entity.HasOne(r => r.Word)
                  .WithMany()
                  .HasForeignKey(r => r.WordId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(r => r.ReviewTime);
            entity.HasIndex(r => r.WordId);
        });

        modelBuilder.Entity<WordReviewState>(entity =>
        {
            entity.ToTable("WordReviewStates");
            entity.HasKey(s => s.WordId);
            entity.Property(s => s.Algorithm).HasConversion<int>();
            entity.Property(s => s.AlgorithmVersion).HasDefaultValue(1);
            entity.Property(s => s.Sm2EaseFactor).HasDefaultValue(2.5);
            entity.HasOne(s => s.Word)
                  .WithMany()
                  .HasForeignKey(s => s.WordId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => s.NextReviewTime);
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.ToTable("UserSettings");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ReviewAlgorithm).HasConversion<int>();
            entity.Property(s => s.FsrsDesiredRetention).HasDefaultValue(0.9);
        });
    }
}
