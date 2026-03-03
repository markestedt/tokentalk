using Microsoft.EntityFrameworkCore;

namespace TokenTalk.Storage;

public class TokenTalkDbContext : DbContext
{
    private readonly string _dbPath;

    public TokenTalkDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public DbSet<Dictation> Dictations => Set<Dictation>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dictation>(entity =>
        {
            entity.ToTable("dictations");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(d => d.Timestamp).HasColumnName("timestamp").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(d => d.RecordingStartMs).HasColumnName("recording_start_ms");
            entity.Property(d => d.RecordingDurationMs).HasColumnName("recording_duration_ms");
            entity.Property(d => d.TranscriptionLatencyMs).HasColumnName("transcription_latency_ms");
            entity.Property(d => d.InjectionLatencyMs).HasColumnName("injection_latency_ms");
            entity.Property(d => d.TotalLatencyMs).HasColumnName("total_latency_ms");
            entity.Property(d => d.AudioSizeBytes).HasColumnName("audio_size_bytes");
            entity.Property(d => d.AudioSampleRate).HasColumnName("audio_sample_rate");
            entity.Property(d => d.Provider).HasColumnName("provider");
            entity.Property(d => d.Model).HasColumnName("model");
            entity.Property(d => d.Language).HasColumnName("language");
            entity.Property(d => d.TranscribedText).HasColumnName("transcribed_text");
            entity.Property(d => d.WordCount).HasColumnName("word_count");
            entity.Property(d => d.CharacterCount).HasColumnName("character_count");
            entity.Property(d => d.Success).HasColumnName("success");
            entity.Property(d => d.ErrorMessage).HasColumnName("error_message").IsRequired(false);

            entity.HasIndex(d => d.Timestamp).HasDatabaseName("idx_dictations_timestamp");
            entity.HasIndex(d => d.Provider).HasDatabaseName("idx_dictations_provider");
            entity.HasIndex(d => d.Success).HasDatabaseName("idx_dictations_success");
        });
    }

    public async Task InitializeAsync()
    {
        await Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        await Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON");
        await Database.EnsureCreatedAsync();
    }
}
