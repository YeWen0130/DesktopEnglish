using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.ViewModels;

/// <summary>Navigates between the pages hosted in the main window.</summary>
public enum AppPage
{
    Dashboard,
    Words,
    Study,
    Settings,
}

/// <summary>
/// Shell view model: owns navigation and exposes the active page view model to the window.
/// </summary>
public sealed partial class MainWindowViewModel(IServiceProvider services) : ObservableObject
{
    private readonly IServiceProvider _services = services;

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private AppPage _currentPageKind = AppPage.Dashboard;

    [ObservableProperty]
    private string _title = "背单词";

    /// <summary>Loads a page, reusing a fresh instance so each visit shows current data.</summary>
    public void Navigate(AppPage page)
    {
        CurrentPageKind = page;
        Title = page switch
        {
            AppPage.Dashboard => "学习概览",
            AppPage.Words => "单词管理",
            AppPage.Study => "学习",
            AppPage.Settings => "设置",
            _ => "背单词",
        };

        CurrentPage = page switch
        {
            AppPage.Dashboard => _services.GetRequiredService<DashboardViewModel>(),
            AppPage.Words => _services.GetRequiredService<WordsViewModel>(),
            AppPage.Study => _services.GetRequiredService<StudyViewModel>(),
            AppPage.Settings => _services.GetRequiredService<SettingsViewModel>(),
            _ => _services.GetRequiredService<DashboardViewModel>(),
        };

        if (CurrentPage is IAsyncInitializable initializable)
        {
            _ = initializable.InitializeAsync(CancellationToken.None);
        }
    }

    public void ShowDashboard() => Navigate(AppPage.Dashboard);

    /// <summary>Opens the study page and asks it to load a batch.</summary>
    public void StartStudy()
    {
        Navigate(AppPage.Study);

        if (CurrentPage is StudyViewModel study)
        {
            _ = study.LoadBatchManuallyAsync(CancellationToken.None);
        }
    }
}

/// <summary>Implemented by page view models that load data when shown.</summary>
public interface IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
