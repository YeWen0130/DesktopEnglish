using System.Windows;
using DesktopEnglish.ViewModels;

namespace DesktopEnglish.Views;

/// <summary>Shell window hosting the navigable pages.</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnDashboardClick(object sender, RoutedEventArgs e) => ViewModel?.Navigate(AppPage.Dashboard);

    private void OnWordsClick(object sender, RoutedEventArgs e) => ViewModel?.Navigate(AppPage.Words);

    private void OnStudyClick(object sender, RoutedEventArgs e) => ViewModel?.StartStudy();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ViewModel?.Navigate(AppPage.Settings);
}
