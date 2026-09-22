using DesktopEnglish.Services;

namespace DesktopEnglish.Tests;

/// <summary>Clock pinned to a fixed instant, for tests that must not depend on wall-clock time.</summary>
internal sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; } = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    public DateTime LocalNow => UtcNow;

    public DateTime ToUtc(DateTime local) =>
        local.Kind switch
        {
            DateTimeKind.Utc => local,
            DateTimeKind.Local => local.ToUniversalTime(),
            _ => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(),
        };

    public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc);
}
