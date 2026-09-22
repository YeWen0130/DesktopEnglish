using DesktopEnglish.Services;

namespace DesktopEnglish.Tests;

/// <summary>
/// A clock whose time is advanced explicitly, so day boundaries are deterministic in tests.
/// "Local" is treated as identical to UTC to keep the arithmetic easy to reason about.
/// </summary>
internal sealed class TestClock : IClock
{
    public DateTime UtcNow { get; private set; }

    public DateTime LocalNow => UtcNow;

    public DateTime ToUtc(DateTime local) =>
        local.Kind switch
        {
            DateTimeKind.Utc => local,
            DateTimeKind.Local => local.ToUniversalTime(),
            _ => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(),
        };

    public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc);

    public void SetUtc(DateTime value) => UtcNow = value;
}
