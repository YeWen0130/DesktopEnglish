using System.Windows.Controls;
using System.Windows.Input;
using DesktopEnglish.ViewModels;

namespace DesktopEnglish.Views;

/// <summary>Shows learning statistics and expands a detail panel when a summary card is clicked.</summary>
public partial class DashboardPage : UserControl
{
    public DashboardPage() => InitializeComponent();

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private void OnStudiedClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.ToggleStudiedCommand.Execute(null);

    private void OnCorrectClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.ToggleCorrectCommand.Execute(null);

    private void OnUnknownClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.ToggleUnknownCommand.Execute(null);

    private void OnCalendarClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.ToggleCalendarCommand.Execute(null);
}
