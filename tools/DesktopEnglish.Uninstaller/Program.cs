using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace DesktopEnglish.Uninstaller;

/// <summary>
/// Standalone uninstaller for 背单词 (DesktopEnglish). It ships next to the application as
/// <c>Uninstall.exe</c> and behaves like the uninstaller of any other Windows program: it removes the
/// registry and notification registrations, the per-user data and every file in its own folder,
/// leaving the (empty) directory behind.
/// </summary>
/// <remarks>
/// The folder cleanup is handed to a short-lived helper script because a process cannot delete the file
/// it is currently running from: the helper waits for this process to exit, then removes the uninstaller
/// itself and any legacy launcher scripts.
/// </remarks>
internal static class Program
{
    private const string ExecutableName = "DesktopEnglish";
    private const string DataFolderName = "DesktopEnglish";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopEnglish";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "DesktopEnglish";
    private const string ToastLoaderFolder = "ToastNotificationManagerCompat";
    private const string SelfName = "Uninstall.exe";

    // Everything the uninstaller removes after the folder has been emptied.
    private static readonly string[] UninstallerFileNames = { "Uninstall.exe", "uninstall.cmd", "uninstall.ps1", "Uninstall.ps1" };

    private static int Main(string[] args)
    {
        Try(() => Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var executable = Path.Combine(here, ExecutableName + ".exe");
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
        var silent = HasFlag(args, "y") || HasFlag(args, "yes") || HasFlag(args, "silent") || HasFlag(args, "quiet");

        Banner(here, data);

        if (IsUnsafeTarget(here))
        {
            Error("为了安全,不在这个位置执行卸载(看起来是磁盘根目录或系统 / 用户目录)。");
            return 3;
        }

        if (!silent)
        {
            if (LooksLikeSourceRepository(here))
            {
                Error("    [!] 这个目录看起来是源码仓库(含 .git / src / 解决方案文件),");
                Error("        继续会连源码一起删除,请先确认已经备份。");
                Console.WriteLine();
            }

            Warn("    注意:会删除本目录下的全部内容,只留下一个空目录。");
            Console.Write("确定要卸载整个软件吗?输入 Y 删除,其它输入取消: ");

            var answer = Console.ReadLine();
            if (answer is null || !answer.TrimStart().StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("已取消,没有做任何改动。");
                return 1;
            }
        }

        Console.WriteLine("[1/4] 停止正在运行的程序 ...");
        StopRunningInstances();

        Console.WriteLine("[2/4] 清理注册表(卸载项 / 开机启动 / 通知注册) ...");
        CleanRegistry(executable);

        Console.WriteLine("[3/4] 删除用户数据 ...");
        DeleteDirectory(data);

        Console.WriteLine("[4/4] 清空程序目录(只留目录本身) ...");
        var failed = new List<string>();
        foreach (var item in SafeEnumerate(here))
        {
            var name = Path.GetFileName(item);
            if (UninstallerFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (DeleteEntry(item))
            {
                Console.WriteLine("    [OK] 已删除: " + name);
            }
            else
            {
                failed.Add(name);
                Console.WriteLine("    [!] 未能删除: " + name);
            }
        }

        Console.WriteLine();

        if (failed.Count > 0)
        {
            Warn("部分内容未能删除,请关闭占用它的程序(例如资源管理器 / 杀毒软件)后重试:");
            foreach (var name in failed)
            {
                Warn("    " + name);
            }

            return 2;
        }

        Console.WriteLine("卸载完成,程序目录已清空,只留下目录本身。");
        Console.WriteLine("卸载程序(Uninstall.exe)会在本窗口结束后自动删除。");

        ScheduleSelfRemoval(here);
        return 0;
    }

    private static void Banner(string directory, string dataDirectory)
    {
        Console.WriteLine();
        Console.WriteLine("  ============================================");
        Console.WriteLine("    卸载「背单词」(DesktopEnglish)");
        Console.WriteLine("  ============================================");
        Console.WriteLine("    程序目录 : " + directory);
        Console.WriteLine("    用户数据 : " + dataDirectory);
        Console.WriteLine();
    }

    private static bool HasFlag(IEnumerable<string> args, string name) =>
        args.Any(argument => argument.TrimStart('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void StopRunningInstances()
    {
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(ExecutableName))
        {
            using (process)
            {
                if (process.Id == self)
                {
                    continue;
                }

                Try(() =>
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                });
            }
        }

        Thread.Sleep(800);
    }

    private static void CleanRegistry(string executablePath)
    {
        var aumid = executablePath.Replace('\\', '/');
        var classId = GetActivatorClassId(aumid);

        Try(() =>
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            run?.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        });

        Try(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false));
        Try(() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\" + aumid, throwOnMissingSubKey: false));
        Try(() => Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{{{classId:D}}}", throwOnMissingSubKey: false));

        var toastDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ToastLoaderFolder,
            "Apps",
            classId.ToString("D").ToUpperInvariant());
        DeleteDirectory(toastDirectory);
    }

    /// <summary>
    /// Reproduces the COM activator class id the notification library derives from an AUMID: the leading
    /// sixteen bytes of the SHA-1 of the AUMID's UTF-8 bytes, read as a canonical GUID.
    /// </summary>
    private static Guid GetActivatorClassId(string aumid)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(aumid));
        return Guid.ParseExact(Convert.ToHexString(hash.AsSpan(0, 16)), "N");
    }

    private static bool DeleteEntry(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Try(() =>
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            });

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Try(() => Directory.Delete(path, recursive: true));
            if (!Directory.Exists(path))
            {
                return;
            }

            Thread.Sleep(300);
        }
    }

    private static IEnumerable<string> SafeEnumerate(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Writes a hidden helper that waits for this process to exit, then deletes the uninstaller and any
    /// legacy launcher scripts that may sit next to it. It retries so brief file locks are tolerated.
    /// </summary>
    private static void ScheduleSelfRemoval(string directory)
    {
        var self = Path.Combine(directory, SelfName);
        var script = new StringBuilder();
        script.Append("@echo off\r\n");
        script.Append("ping -n 3 127.0.0.1 >nul\r\n");

        foreach (var name in UninstallerFileNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                script.Append("del /f /q \"").Append(path).Append("\" >nul 2>&1\r\n");
            }
        }

        script.Append("for /l %%i in (1,1,120) do (\r\n");
        script.Append("    if not exist \"").Append(self).Append("\" goto done\r\n");
        script.Append("    del /f /q \"").Append(self).Append("\" >nul 2>&1\r\n");
        script.Append("    ping -n 2 127.0.0.1 >nul\r\n");
        script.Append(")\r\n");
        script.Append(":done\r\n");
        script.Append("del /f /q \"%~f0\" >nul 2>&1\r\n");

        var helperPath = Path.Combine(Path.GetTempPath(), "DesktopEnglish-uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");

        Try(() =>
        {
            File.WriteAllText(helperPath, script.ToString(), Encoding.ASCII);
            Process.Start(new ProcessStartInfo
            {
                FileName = helperPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        });
    }

    private static bool LooksLikeSourceRepository(string directory) =>
        File.Exists(Path.Combine(directory, ExecutableName + ".sln"))
        || Directory.Exists(Path.Combine(directory, ".git"))
        || Directory.Exists(Path.Combine(directory, "src"));

    private static bool IsUnsafeTarget(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var folder in new[]
        {
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
        })
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (string.Equals(full, Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch
        {
        }
    }

    private static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}