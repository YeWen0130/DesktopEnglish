using System.IO;
using System.Reflection;
using DesktopEnglish.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Populates the dictionary on first run from the CSV embedded in the assembly, so a fresh install is
/// immediately usable without any manual import step.
/// </summary>
public sealed class SeedDataService(
    IDbContextFactory<AppDbContext> contextFactory,
    IWordImportService importService,
    AppOptions options,
    ILogger<SeedDataService> logger)
{
    public const string SeedResourceName = "DesktopEnglish.Seed.words.csv";

    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken)
    {
        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (await context.Words.AnyAsync(cancellationToken))
            {
                logger.LogDebug("Word table already populated; skipping seed.");
                return;
            }
        }

        logger.LogInformation("Word table is empty; seeding from embedded word list.");
        await EnsureWordListCompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Imports the embedded word list with duplicate-skipping, whether or not the table already holds rows.
    /// A database recovery uses this to bring back words that lived on the lost pages, while the words the
    /// user has already studied keep their review state.
    /// </summary>
    public async Task EnsureWordListCompleteAsync(CancellationToken cancellationToken)
    {
        await using var stream = OpenSeedStream();
        if (stream is null)
        {
            logger.LogWarning("Embedded seed word list '{Resource}' was not found; starting with an empty dictionary.",
                SeedResourceName);
            return;
        }

        var importOptions = new ImportOptions(
            Format: ImportFormat.Csv,
            Duplicates: DuplicatePolicy.Skip,
            Category: options.Category,
            BatchSize: options.ImportBatchSize);

        var result = await importService.ImportAsync(stream, importOptions, cancellationToken);

        logger.LogInformation(
            "Word list import complete: added={Added} updated={Updated} skipped={Skipped} invalid={Invalid}",
            result.Added, result.Updated, result.Skipped, result.Invalid);
    }

    private static Stream? OpenSeedStream() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(SeedResourceName);
}

