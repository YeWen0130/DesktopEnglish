using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.ViewModels;

/// <summary>
/// Drives one study round: shows the word first, reveals the definition on demand, records each answer and
/// reports a summary at the end.
/// </summary>
public sealed partial class StudyViewModel(
    IStudyBatchService batchService,
    IStudySessionService studyService,
    ISettingsService settings,
    ILogger<StudyViewModel> logger) : ObservableObject, IAsyncInitializable
{
    private IReadOnlyList<WordReminderItem> _batch = [];

    [ObservableProperty]
    private string _wordText = string.Empty;

    [ObservableProperty]
    private string _phonetic = string.Empty;

    [ObservableProperty]
    private string _partOfSpeech = string.Empty;

    [ObservableProperty]
    private string _meaning = string.Empty;

    [ObservableProperty]
    private string _example = string.Empty;

    [ObservableProperty]
    private bool _isAnswerRevealed;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private int _totalInBatch;

    [ObservableProperty]
    private int _correctCount;

    [ObservableProperty]
    private int _wrongCount;

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Human-readable progress, e.g. <c>3 / 5</c>.</summary>
    public string ProgressText => TotalInBatch == 0
        ? "暂无单词"
        : $"{Math.Min(CurrentIndex + 1, TotalInBatch)} / {TotalInBatch}";

    public Task InitializeAsync(CancellationToken cancellationToken) => LoadBatchAsync(manual: false, cancellationToken);

    /// <summary>Bound to the study page's "再来一组" button: an explicit request, so the daily limit does not apply.</summary>
    [RelayCommand]
    public Task LoadBatchManuallyAsync(CancellationToken cancellationToken) => LoadBatchAsync(manual: true, cancellationToken);

    /// <summary>Loads the next scheduled batch, respecting the daily limit.</summary>
    [RelayCommand]
    public Task LoadBatchAsync(CancellationToken cancellationToken) => LoadBatchAsync(manual: false, cancellationToken);

    /// <summary>
    /// Loads a fresh batch. Manual sessions ignore the daily limit because the user asked for them explicitly.
    /// </summary>
    private async Task LoadBatchAsync(bool manual, CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var size = Math.Max(1, settings.Current.WordsPerReminder);
            var items = await batchService.BuildBatchAsync(size, respectDailyLimit: !manual, cancellationToken);

            _batch = items;
            CurrentIndex = 0;
            TotalInBatch = items.Count;
            CorrectCount = 0;
            WrongCount = 0;
            IsFinished = items.Count == 0;

            if (items.Count == 0)
            {
                StatusMessage = "当前没有需要学习的单词。请先导入词库或稍后再来。";
                ClearCurrent();
            }
            else
            {
                StatusMessage = manual ? "手动学习（不计入提醒限额）" : string.Empty;
                ShowCurrent();
            }

            OnPropertyChanged(nameof(ProgressText));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build a study batch.");
            StatusMessage = "加载学习内容失败，请查看日志。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Reveal() => IsAnswerRevealed = true;

    [RelayCommand]
    private Task KnownAsync(CancellationToken cancellationToken) => AnswerAsync(StudyResult.Known, cancellationToken);

    [RelayCommand]
    private Task FuzzyAsync(CancellationToken cancellationToken) => AnswerAsync(StudyResult.Fuzzy, cancellationToken);

    [RelayCommand]
    private Task UnknownAsync(CancellationToken cancellationToken) => AnswerAsync(StudyResult.Unknown, cancellationToken);

    private async Task AnswerAsync(StudyResult result, CancellationToken cancellationToken)
    {
        if (_batch.Count == 0 || CurrentIndex >= _batch.Count)
        {
            return;
        }

        var current = _batch[CurrentIndex];

        try
        {
            await studyService.RecordResultAsync(current.WordId, result, cancellationToken);

            if (result == StudyResult.Unknown)
            {
                WrongCount++;
            }
            else
            {
                CorrectCount++;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the study result for word {WordId}.", current.WordId);
            StatusMessage = "保存学习记录失败，请查看日志。";
            return;
        }

        CurrentIndex++;

        if (CurrentIndex >= _batch.Count)
        {
            FinishRound();
            return;
        }

        ShowCurrent();
    }

    private void FinishRound()
    {
        IsFinished = true;
        IsAnswerRevealed = false;
        StatusMessage = $"本轮完成：认识 {CorrectCount}，不认识 {WrongCount}";
        OnPropertyChanged(nameof(ProgressText));
    }

    private void ShowCurrent()
    {
        var item = _batch[CurrentIndex];

        WordText = item.WordText;
        Phonetic = item.Phonetic;
        PartOfSpeech = item.PartOfSpeech;
        Meaning = item.Meaning;
        Example = item.Example;
        IsAnswerRevealed = false;

        OnPropertyChanged(nameof(ProgressText));
    }

    private void ClearCurrent()
    {
        WordText = string.Empty;
        Phonetic = string.Empty;
        PartOfSpeech = string.Empty;
        Meaning = string.Empty;
        Example = string.Empty;
        IsAnswerRevealed = false;
        OnPropertyChanged(nameof(ProgressText));
    }
}
