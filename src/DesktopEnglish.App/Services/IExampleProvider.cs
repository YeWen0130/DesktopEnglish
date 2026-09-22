namespace DesktopEnglish.Services;

/// <summary>
/// Optional hook for enriching imported words with an English example sentence.
/// The default implementation returns nothing; supplying a real provider later requires no other changes.
/// </summary>
public interface IExampleProvider
{
    Task<string?> GetExampleAsync(string word, CancellationToken cancellationToken);
}

/// <summary>No-op provider: imported examples stay empty until edited by hand.</summary>
public sealed class NullExampleProvider : IExampleProvider
{
    public Task<string?> GetExampleAsync(string word, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
