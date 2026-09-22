using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.ViewModels;

/// <summary>Search, edit, import and delete words. All persistence goes through <see cref="IWordService"/>.</summary>
public sealed partial class WordsViewModel(
    IWordService words,
    IWordImportService importService,
    AppOptions options,
    ILogger<WordsViewModel> logger) : ObservableObject, IAsyncInitializable
{
    [ObservableProperty]
    private ObservableCollection<Word> _items = [];

    [ObservableProperty]
    private Word? _selectedWord;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _categoryFilter = string.Empty;

    [ObservableProperty]
    private int _page = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public async Task InitializeAsync(CancellationToken cancellationToken) => await SearchAsync(cancellationToken);

    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;

        try
        {
            var result = await words.QueryAsync(
                new WordQuery(Search: SearchText, Page: Page, PageSize: 50),
                cancellationToken);

            Items = new ObservableCollection<Word>(result.Items);
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            StatusMessage = $"共 {result.TotalCount} 个单词";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to query words.");
            StatusMessage = "查询失败，请查看日志。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NextPageAsync(CancellationToken cancellationToken)
    {
        if (Page >= TotalPages)
        {
            return;
        }

        Page++;
        await SearchAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task PreviousPageAsync(CancellationToken cancellationToken)
    {
        if (Page <= 1)
        {
            return;
        }

        Page--;
        await SearchAsync(cancellationToken);
    }

    /// <summary>Persists the edited word. Returns true when the caller should close the dialog.</summary>
    public async Task<bool> SaveAsync(Word word, CancellationToken cancellationToken)
    {
        try
        {
            if (word.Id == 0)
            {
                await words.AddAsync(word, cancellationToken);
            }
            else
            {
                await words.UpdateAsync(word, cancellationToken);
            }

            await SearchAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save word {Word}.", word.WordText);
            StatusMessage = "保存失败：单词可能已存在。";
            return false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var target = SelectedWord;
        if (target is null)
        {
            return;
        }

        try
        {
            await words.DeleteAsync(target.Id, cancellationToken);
            StatusMessage = $"已删除 {target.WordText}";
            SelectedWord = null;
            await SearchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete word {WordId}.", target.Id);
            StatusMessage = "删除失败，请查看日志。";
        }
    }

    /// <summary>Imports a CSV or JSON file, choosing the format from its extension.</summary>
    public async Task ImportAsync(string filePath, DuplicatePolicy duplicates, CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var format = Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? ImportFormat.Json
                : ImportFormat.Csv;

            await using var stream = File.OpenRead(filePath);

            var importOptions = new ImportOptions(
                Format: format,
                Duplicates: duplicates,
                Category: string.IsNullOrWhiteSpace(CategoryFilter) ? options.Category : CategoryFilter);

            var result = await importService.ImportAsync(stream, importOptions, cancellationToken);

            StatusMessage = $"导入完成：新增 {result.Added}，更新 {result.Updated}，跳过 {result.Skipped}";
            await SearchAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import words from {Path}.", filePath);
            StatusMessage = "导入失败，请检查文件格式。";
        }
        finally
        {
            IsBusy = false;
        }
    }
}





