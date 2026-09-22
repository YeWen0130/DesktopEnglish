using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using DesktopEnglish.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace DesktopEnglish.Views;

/// <summary>Word list with search, paging, editing and file import.</summary>
public partial class WordsPage : UserControl
{
    public WordsPage() => InitializeComponent();

    private WordsViewModel? ViewModel => DataContext as WordsViewModel;

    private async void OnAddClick(object sender, RoutedEventArgs e) => await EditWordAsync(null);

    private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e) =>
        await EditWordAsync(ViewModel?.SelectedWord);

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.DeleteCommand.CanExecute(null) == true)
        {
            ViewModel.DeleteCommand.Execute(null);
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入单词",
            Filter = "单词文件 (*.csv;*.json)|*.csv;*.json|CSV 文件 (*.csv)|*.csv|JSON 文件 (*.json)|*.json",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true || ViewModel is null)
        {
            return;
        }

        // Skip is the default so a repeated import never clobbers edited data.
        var duplicates = MessageBox.Show(
            "遇到已存在的单词时如何处理？\n\n“是”= 跳过（保留现有）\n“否”= 覆盖更新",
            "导入选项",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes
            ? DuplicatePolicy.Skip
            : DuplicatePolicy.Overwrite;

        await ViewModel.ImportAsync(dialog.FileName, duplicates, CancellationToken.None);
    }

    private async Task EditWordAsync(Word? word)
    {
        if (ViewModel is null)
        {
            return;
        }

        var isNew = word is null;
        var editing = isNew
            ? new Word { Category = "百词斩" }
            : new Word
            {
                Id = word!.Id,
                WordText = word.WordText,
                Phonetic = word.Phonetic,
                Meaning = word.Meaning,
                PartOfSpeech = word.PartOfSpeech,
                Example = word.Example,
                Level = word.Level,
                Category = word.Category,
                CreatedAt = word.CreatedAt,
                UpdatedAt = word.UpdatedAt,
            };

        var dialog = new WordEditDialog(editing, isNew) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
        {
            await ViewModel.SaveAsync(editing, CancellationToken.None);
        }
    }
}
