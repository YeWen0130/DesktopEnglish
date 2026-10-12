using System.IO;
using DesktopEnglish.Installation;
using Xunit;

namespace DesktopEnglish.Tests;

/// <summary>
/// Covers the install/uninstall behavior that can be checked without touching a real machine: the
/// registry values Windows needs, their removal order, and the paths derived from the executable.
/// </summary>
public class UninstallServiceTests
{
    [Fact]
    public void Register_writes_the_uninstall_key_windows_needs()
    {
        var registry = new RecordingRegistry();
        var executable = Path.GetFullPath(Environment.ProcessPath!);
        var expectedCommand = AppInstallInfo.FormatUninstallCommand(executable);

        CreateService(registry).Register();

        Assert.Equal(AppInstallInfo.DisplayName, registry.Get(AppInstallInfo.UninstallKeyPath, "DisplayName"));
        Assert.Equal(AppInstallInfo.Publisher, registry.Get(AppInstallInfo.UninstallKeyPath, "Publisher"));
        Assert.Equal(Path.GetDirectoryName(executable), registry.Get(AppInstallInfo.UninstallKeyPath, "InstallLocation"));
        Assert.Equal(expectedCommand, registry.Get(AppInstallInfo.UninstallKeyPath, "UninstallString"));
        Assert.Equal(expectedCommand, registry.Get(AppInstallInfo.UninstallKeyPath, "QuietUninstallString"));
        Assert.Contains(executable, registry.Get(AppInstallInfo.UninstallKeyPath, "DisplayIcon"));
        Assert.Equal("1", registry.Get(AppInstallInfo.UninstallKeyPath, "NoModify"));
        Assert.Equal("1", registry.Get(AppInstallInfo.UninstallKeyPath, "NoRepair"));
    }

    [Fact]
    public void The_registered_uninstall_command_switches_the_app_into_uninstall_mode()
    {
        var executable = @"C:\Apps\DesktopEnglish.exe";
        var command = AppInstallInfo.FormatUninstallCommand(executable);

        // Windows runs the uninstall command verbatim, so the switch it embeds must be recognised by
        // the same parser the entry point uses.
        var parsed = StartupCommandLine.Parse(new[] { AppInstallInfo.UninstallSwitch });

        Assert.Contains(executable, command);
        Assert.Contains(AppInstallInfo.UninstallSwitch, command);
        Assert.Equal(StartupMode.Uninstall, parsed);
    }

    [Fact]
    public void Uninstall_removes_autostart_before_the_uninstall_key()
    {
        var registry = new RecordingRegistry();
        var aumid = "C:/Apps/DesktopEnglish.exe";
        var environment = new FakeEnvironment(@"C:\Apps\DesktopEnglish.exe");

        CreateService(registry, environment).Uninstall();

        // Auto start must go first, otherwise Windows would try to launch a deleted executable. The
        // registry entries that remain are the two notification trees, keyed off the executable path.
        Assert.Equal(
            new[]
            {
                @"value:Software\Microsoft\Windows\CurrentVersion\Run\DesktopEnglish",
                $"tree:{AppInstallInfo.UninstallKeyPath}",
                $"tree:{ToastIdentity.GetAppUserModelIdRegistryPath(aumid)}",
                $"tree:{ToastIdentity.GetActivatorClassRegistryPath(aumid)}",
            },
            registry.Log.ToArray());
    }

    [Fact]
    public void Uninstall_clears_files_and_schedules_executable_deletion()
    {
        var environment = new FakeEnvironment(@"C:\Apps\DesktopEnglish.exe");

        var report = CreateService(new RecordingRegistry(), environment).Uninstall();

        Assert.True(environment.NotificationRegistrationRemoved);
        Assert.Contains(environment.DataDirectory, environment.DeletedDirectories);
        Assert.True(environment.ExecutableDeletionScheduled);
        Assert.True(report.ExecutableDeletionScheduled);
        Assert.Equal(environment.DataDirectory, report.DataDirectory);
    }

    [Fact]
    public void BuildPlan_derives_every_notification_path_from_the_executable()
    {
        var plan = UninstallService.BuildPlan(@"C:\Apps\BianC\DesktopEnglish.exe");

        Assert.Equal(@"C:\Apps\BianC\DesktopEnglish.exe", plan.ExecutablePath);
        Assert.Equal("C:/Apps/BianC/DesktopEnglish.exe", plan.Aumid);
        Assert.Equal(@"Software\Classes\AppUserModelId\C:/Apps/BianC/DesktopEnglish.exe", plan.AppUserModelIdRegistryPath);

        // The activator key and the icon folder are both keyed by the derived class id, and must agree.
        // Registry paths are case insensitive; the icon folder is stored upper case.
        var classId = ToastIdentity.GetActivatorClassId(plan.Aumid).ToString("D");
        Assert.Equal($@"Software\Classes\CLSID\{{{classId}}}", plan.ActivatorRegistryPath);
        Assert.EndsWith(classId.ToUpperInvariant(), plan.NotificationDataDirectory);
    }

    [Fact]
    public void Activator_class_id_is_the_library_guid_layout()
    {
        // Cross-checked against a real install: this AUMID produced {E9F6A0B6-2D38-F382-31A7-44137854F989}.
        var aumid = "D:/BianCheng/DesktopEnglish/publish/DesktopEnglish.exe";

        var classId = ToastIdentity.GetActivatorClassId(aumid);

        Assert.Equal(Guid.Parse("e9f6a0b6-2d38-f382-31a7-44137854f989"), classId);
    }

    [Theory]
    [InlineData("--uninstall", StartupMode.Uninstall)]
    [InlineData("/uninstall", StartupMode.Uninstall)]
    [InlineData("--UNINSTALL", StartupMode.Uninstall)]
    [InlineData("--uninstall-extra", StartupMode.Run)]
    [InlineData("something", StartupMode.Run)]
    public void Parse_recognises_the_uninstall_switch(string argument, StartupMode expected) =>
        Assert.Equal(expected, StartupCommandLine.Parse(new[] { argument }));

    private static UninstallService CreateService(
        IUninstallRegistry registry,
        IUninstallEnvironment? environment = null) =>
        new(
            registry,
            new NoOpStopper(),
            environment ?? new FakeEnvironment(Path.GetFullPath(Environment.ProcessPath!)),
            new NoOpLogger<UninstallService>());

    private sealed class NoOpStopper : IApplicationStopper
    {
        public void StopRunningInstances(TimeSpan timeout)
        {
        }
    }

    private sealed class FakeEnvironment(string executablePath) : IUninstallEnvironment
    {
        private readonly string _executablePath = executablePath;

        public string DataDirectory => @"C:\Users\Test\AppData\Local\DesktopEnglish";

        public List<string> DeletedDirectories { get; } = new();

        public bool NotificationRegistrationRemoved { get; private set; }

        public bool ExecutableDeletionScheduled { get; private set; }

        public string ResolveExecutablePath() => _executablePath;

        public void DeleteDirectory(string path) => DeletedDirectories.Add(path);

        public void RemoveNotificationRegistration() => NotificationRegistrationRemoved = true;

        public bool ScheduleExecutableDeletion(string executablePath)
        {
            ExecutableDeletionScheduled = true;
            return true;
        }
    }

    private sealed class RecordingRegistry : IUninstallRegistry
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Log { get; } = new();

        public void SetString(string subKey, string name, string value)
        {
            _values[$"{subKey}\\{name}"] = value;
            Log.Add($"set:{subKey}\\{name}");
        }

        public void DeleteValue(string subKey, string name)
        {
            _values.Remove($"{subKey}\\{name}");
            Log.Add($"value:{subKey}\\{name}");
        }

        public void DeleteTree(string subKey) => Log.Add($"tree:{subKey}");

        public string? Get(string subKey, string name) =>
            _values.TryGetValue($"{subKey}\\{name}", out var value) ? value : null;
    }
}