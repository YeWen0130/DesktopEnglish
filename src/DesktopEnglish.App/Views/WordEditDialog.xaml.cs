using System.Globalization;
using System.Windows;
using DesktopEnglish.Domain;

namespace DesktopEnglish.Views;

/// <summary>Add or edit a single word. Validation happens here; persistence happens in the view model.</summary>
public partial class WordEditDialog : Window
{
    private readonly Word _word;

    public WordEditDialog(Word word, bool isNew)
    {
        InitializeComponent();

        _word = word;
        Title = isNew ? "新增单词" : "编辑单词";

        WordBox.Text = word.WordText;
        PhoneticBox.Text = word.Phonetic;
        PosBox.Text = word.PartOfSpeech;
        MeaningBox.Text = word.Meaning;
        ExampleBox.Text = word.Example;
        LevelBox.Text = word.Level.ToString(CultureInfo.InvariantCulture);
        CategoryBox.Text = word.Category;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var text = WordBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("单词不能为空。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(LevelBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
        {
            level = 1;
        }

        _word.WordText = text;
        _word.Phonetic = PhoneticBox.Text.Trim();
        _word.PartOfSpeech = PosBox.Text.Trim();
        _word.Meaning = MeaningBox.Text.Trim();
        _word.Example = ExampleBox.Text.Trim();
        _word.Level = Math.Clamp(level, 1, 10);
        _word.Category = CategoryBox.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
