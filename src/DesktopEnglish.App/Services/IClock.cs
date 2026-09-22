namespace DesktopEnglish.Services;

/// <summary>
/// Abstracts the current time so scheduling and statistics logic stays deterministic in tests.
/// All stored timestamps are UTC; local time is used only for user-facing day boundaries.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    DateTime LocalNow { get; }

    DateTime ToUtc(DateTime local);

    DateTime ToLocal(DateTime utc);
}

/// <summary>System clock implementation.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime LocalNow => DateTime.Now;

    public DateTime ToUtc(DateTime local) =>
        local.Kind switch
        {
            DateTimeKind.Utc => local,
            DateTimeKind.Local => local.ToUniversalTime(),
            _ => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(),
        };

    public DateTime ToLocal(DateTime utc) =>
        utc.Kind switch
        {
            DateTimeKind.Local => utc,
            DateTimeKind.Utc => utc.ToLocalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime(),
        };
}
