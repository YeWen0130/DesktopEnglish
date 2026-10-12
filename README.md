# 背单词 DesktopEnglish

一个常驻 Windows 系统托盘的桌面背单词工具:后台定时弹出单词通知,点开即可复习,所有学习数据本地留存。
基于 .NET 8 + WPF 构建,打包为单文件绿色程序,双击即用、免安装、免管理员权限。

## 功能特性

- **桌面提醒**:按设定间隔通过 Windows Toast 通知推送单词,通知里的「认识 / 不认识」按钮可直接作答并落库;Toast 不可用时自动回退为气泡提示。
- **常驻托盘**:关闭主窗口后提醒循环仍在后台运行,托盘菜单支持打开程序、立即学习、暂停/恢复提醒、打开设置、卸载、退出。
- **学习流程**:先复习到期单词,不够再由未学过的生词补齐;新词按难度从易到难选取,每日学习数量为硬上限。
- **实时统计**:学习概览展示今日学习/正确/错误、连续学习天数、累计次数、已掌握词数、待复习词数、词库总量,并提供今日明细、生词、错误词与日历视图,错误词还能就地改判为认识。
- **三套复习算法**:内置经典分级、SM-2 与 FSRS-6,可在设置中随时切换,切换后按历史记录重建学习状态。
- **词库管理**:内置 4400+ 词表(首次启动自动导入),支持单词增删改查,并可从 CSV / JSON 批量导入(自动识别重复项、解析词性与难度)。
- **热更新设置**:提醒间隔、每日数量、每次提醒词数、声音、开机自启等改动即时生效,无需重启。
- **数据可靠性**:SQLite 采用回滚日志 + `synchronous=FULL` 确保每次提交真正落盘;启动时先做数据库体检,发现损坏先备份到 `backup` 目录再尝试修复恢复,不会静默丢数据。
- **便携与自清理**:程序可放在任意位置运行,首次启动自动注册当前路径的卸载项;配套独立的 `Uninstall.exe` 可干净卸载。

## 技术栈

- **运行时**:.NET 8(`net8.0-windows10.0.19041.0`)、WPF、C# 12,可空引用与隐式 using 全局开启。
- **UI / MVVM**:[CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)、[H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon)(托盘图标)。
- **通知**:CommunityToolkit.WinUI.Notifications + WinRT Toast,托盘气泡兜底。
- **数据**:EF Core 8 + SQLite,代码迁移(migration)管理表结构。
- **宿主与日志**:Microsoft.Extensions.Hosting 承载后台提醒循环,Serilog 输出到滚动日志文件与控制台。
- **导入**:CsvHelper。
- **测试**:xUnit。

## 项目结构

```
DesktopEnglish.sln            解决方案(应用 / 测试 / 卸载器三个项目)
Directory.Build.props         全局编译属性(版本、语言级别等)
global.json                    固定的 .NET SDK 版本
build.ps1                      构建 + 测试脚本
publish.ps1                    发布单文件 exe(应用 + 卸载器)
百词斩单词.csv                 内置种子词表(编译进程序,单一来源)

src/DesktopEnglish.App/        主程序(WPF)
  App.xaml(.cs)                入口:配置→日志→数据库体检→后台提醒→托盘
  ServiceRegistration.cs       DI 容器装配
  TrayHost.cs                  托盘图标与右键菜单
  appsettings.json             默认配置(内嵌,首次运行时的默认值)
  Domain/                      领域模型(Word / WordReviewState / LearningRecord 等)
  Data/                        DbContext、连接与耐久性配置、数据库体检与修复、迁移
  Services/                    提醒调度、选词、学习会话、统计、导入、设置、单实例等
  ViewModels/                  各页面视图模型
  Views/                       MainWindow、设置/学习/概览/词库页面、单词编辑对话框

tests/DesktopEnglish.Tests/    xUnit 单元与集成测试
tools/DesktopEnglish.Uninstaller/  独立卸载器(编译为 Uninstall.exe)

docs/                          预留文档目录
```

## 环境要求

- Windows 10 1607(内部版本 14393)及以上,`net8.0-windows10.0.19041.0` 目标框架需要 10.0.19041 及以上版本或相应 SDK 支持。
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)(构建用)。最终用户无需安装 .NET,发布产物为自包含单文件。

## 快速开始

构建并测试仓库(脚本会自动定位用户目录下的本地 SDK,不改动全局 PATH):

```powershell
./build.ps1                 # Debug 构建 + 跑测试
./build.ps1 -Configuration Release
```

或直接使用 dotnet CLI:

```powershell
dotnet build DesktopEnglish.sln -c Debug
dotnet test tests/DesktopEnglish.Tests/DesktopEnglish.Tests.csproj
dotnet run --project src/DesktopEnglish.App/DesktopEnglish.App.csproj
```

### 发布单文件程序

```powershell
./publish.ps1               # 输出到仓库根目录:DesktopEnglish.exe 与 Uninstall.exe
```

发布参数:`-Configuration`、`-Runtime`(默认 `win-x64`)、`-StageDir`、`-UninstallerStageDir`、`-OutputDir`。
应用因 WPF / EF Core 不可裁剪,发布时关闭 trimming,并把原生 SQLite 库打包进单文件。

### 数据库迁移

仓库使用本地工具 `dotnet-ef`(见 `.config/dotnet-tools.json`):

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/DesktopEnglish.App/DesktopEnglish.App.csproj
dotnet ef database update --project src/DesktopEnglish.App/DesktopEnglish.App.csproj
```

## 使用说明

1. 运行 `DesktopEnglish.exe`,程序驻留系统托盘(启动时不打开主窗口),并创建托盘图标。
2. 首次启动会自动建库、导入内置词表并注册卸载项;数据写入 `%LOCALAPPDATA%\DesktopEnglish`。
3. 到提醒间隔后会弹出单词通知,点击按钮即可作答;也可通过托盘菜单「立即学习」进入主窗口学习页。
4. 主窗口包含学习概览、学习、词库、设置四个页面;设置保存后立即生效。

## 数据与存储位置

| 内容 | 位置 |
| --- | --- |
| 数据库 | `%LOCALAPPDATA%\DesktopEnglish\desktopenglish.db` |
| 日志 | `%LOCALAPPDATA%\DesktopEnglish\logs` |
| 损坏备份 | `%LOCALAPPDATA%\DesktopEnglish\backup` |
| 用户配置覆盖 | `%LOCALAPPDATA%\DesktopEnglish\appsettings.json`(可选) |
| 卸载项 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopEnglish` |
| 开机自启 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |

程序本身可执行文件所在目录不写入任何数据,便于只读部署或整体搬迁。

## 复习算法

在设置页可切换以下算法,目标保持率(FSRS)支持 80%–97%:

- **经典(默认)**:按复习阶段查间隔表 `[1, 2, 4, 7, 15, 30, 60]` 天。「认识」前进一级,「不认识」回退一级,「模糊」保持当前间隔但仍计为一次正确;掌握等级取阶段与阈值 5 的较小值。
- **SM-2**:维护连续成功次数、难度系数与间隔;答错则重置连续次数,并在次日重新安排复习。
- **FSRS-6**:按官方参考实现接入,使用固定默认参数,暂不做个人参数训练;答「Again」安排 10 分钟后重学。非经典算法会用计算出的间隔映射掌握等级:`<2 天为 0`,`2 / 4 / 7 / 15 / 30 天` 分别对应 `1 / 2 / 3 / 4 / 5`;答「不认识」时等级归零。

无论哪种算法,「到期」只表示可进入学习队列,仍受提醒开关与每日上限约束。

## 词库导入

- 种子词表 `百词斩单词.csv`(`topic_id, 单词, 音标, 中文释义, 词频`)在编译期作为内嵌资源打包,首次启动自动导入。
- 词库页支持从 CSV / JSON 文件导入,可选重复项策略(跳过等);导入时按词频分值换算 1–10 难度,并从中文释义前缀提取词性(如 `n. / vt. / v.`)。

## 卸载

- 托盘菜单「卸载程序」,或直接双击程序目录下的 `Uninstall.exe`:
  - 双击运行:控制台界面 + 确认提示,输入 `Y` 才继续。
  - 静默卸载:`Uninstall.exe /y`。
- 卸载流程:结束正在运行的实例 → 删除卸载项、开机启动与 Toast 通知登记 → 删除 `%LOCALAPPDATA%\DesktopEnglish` → 清空程序目录全部文件并自删除,仅留下空目录。
- 安全兜底:磁盘根目录、用户目录、桌面等位置会直接拒绝执行;看起来像源码仓库(含 `.git` / `src` / `.sln`)的目录会先给出警告。

## 测试

```powershell
dotnet test tests/DesktopEnglish.Tests/DesktopEnglish.Tests.csproj
```

覆盖复习算法(经典 / SM-2 / FSRS 及切换集成)、提醒状态、选词与每日配额、统计、导入映射、数据库耐久性以及卸载服务等。

## 配置项

默认值在内嵌的 `appsettings.json` 中,首次运行后由用户设置持久化接管:

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `ReminderInterval` | 30 | 提醒间隔(分钟) |
| `DailyWordCount` | 20 | 每日学习数量上限 |
| `WordsPerReminder` | 1 | 每次提醒的单词数 |
| `NotificationEnabled` | true | 是否开启桌面提醒 |
| `SoundEnabled` | true | 提醒是否播放声音 |
| `AutoStart` | false | 是否开机自启 |
| `ReviewAlgorithm` | Classic | 复习算法(Classic / Sm2 / Fsrs) |
| `FsrsDesiredRetention` | 0.9 | FSRS 目标保持率 |
| `Category` | 百词斩 | 导入单词的默认词库分类 |
| `ImportBatchSize` | 500 | 批量导入批大小 |
| `Logging.MinimumLevel` | Information | 最低日志级别 |
| `Logging.RetainedFileCount` | 7 | 滚动日志保留文件数 |
| `Logging.WriteToConsole` | true | 是否同时输出到控制台 |
