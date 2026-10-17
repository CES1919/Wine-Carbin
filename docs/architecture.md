# Architecture

Wine Cabinet 是一个 `net10.0-windows` 的 WinForms 应用，不引用任何第三方 NuGet 包。图像解码、GIF 动画推进和缩放使用 Windows/.NET 自带的 `System.Drawing` 与 `ImageAnimator`；Windows Shell 交互通过 P/Invoke 直接调用 `shell32.dll` 与 `user32.dll`。代码按 `Core`（业务规则与持久化）、`UI`（窗口与绘制）、`Platform`（系统集成）分层，`Core` 不创建任何窗口。

## Components

- `src/WineCabinet/Program.cs`：取得单实例互斥锁、设置 DPI 与视觉样式（视觉样式不可用时降级为无主题外观）、注册全局异常提示、根据启动参数处理启动项登记，然后运行 `LauncherForm`；启动阶段的异常写入 `startup-error.log` 并弹窗提示，不再静默退出。
- `src/WineCabinet/UI/LauncherForm.cs`：96×96 桌面入口窗口，负责绘制动画图标、左键点击切换面板展开与收起、接收桌面拖放、承载入口右键菜单与系统托盘图标，启动时恢复上次位置、在移动结束与退出时持久化位置，并把自己的移动同步给面板。
- `src/WineCabinet/UI/FlyoutForm.cs`：图标面板窗口，负责 3×3/4×4 网格、分页与页码角标、展开收起动画、项目菜单、内部拖放交换与边界翻页。
- `src/WineCabinet/UI/AppMenu.cs`：入口、中央应用格及降级菜单的圆角外观与渲染器。
- `src/WineCabinet/UI/RenameItemDialog.cs`：原生重命名命令的宿主输入窗口，文件默认只选中名称部分，保留扩展名。
- `src/WineCabinet/UI/ArtworkRenderer.cs`：contain 布局计算与高质量等比绘制。
- `src/WineCabinet/UI/TrayIconFactory.cs`：把入口图案按通知区域的小图标尺寸绘制成 `Icon`，无图案时使用纯色回退；句柄克隆后立即释放临时 HICON。
- `src/WineCabinet/UI/EmbeddedImage.cs`：从程序集资源加载内嵌图标，并在对象生命周期内保持资源流存活。
- `src/WineCabinet/Core/ShortcutLibrary.cs`：收纳清单模型与持久化，提供添加、交换、移动、启动计数、恢复与移除。
- `src/WineCabinet/Core/AppStorage.cs`：提供应用数据目录 `%LOCALAPPDATA%\WineCabinet` 的单一来源。
- `src/WineCabinet/Core/GridLayoutRules.cs`：每页容量与槽位映射的纯函数。
- `src/WineCabinet/Core/GridLayoutSettings.cs`：`settings.json` 布局选择的读取、默认值与写入。
- `src/WineCabinet/Core/LauncherPlacement.cs`：`position.json` 中入口位置的读取、默认值与写入，坐标超出合理范围或文件损坏时视为无记录。
- `src/WineCabinet/Core/LauncherPlacementRules.cs`：入口默认位置与恢复位置的纯函数：按各屏幕工作区判断保存的位置是否可见，可见时钳制回工作区，否则要求回退到默认位置。
- `src/WineCabinet/Core/DragNavigation.cs`：拖拽越界决策（上一页/下一页/移出）。
- `src/WineCabinet/Core/InputRules.cs`：启动按键规则（仅左键）。
- `src/WineCabinet/Platform/ShellDropHelper.cs`：解析 `Shell IDList Array` 拖放数据，并取回 Shell 显示名与图标。
- `src/WineCabinet/Platform/ShellDesktopIconManager.cs`：通过当前用户注册表隐藏或恢复桌面 Shell 图标。
- `src/WineCabinet/Platform/ShellContextMenu.cs`：解析 PIDL、绑定父 Shell 文件夹并取得 `IContextMenu`，显示原生菜单与调用系统命令；转发 `IContextMenu2/3` 消息以支持动态子菜单和自绘菜单扩展，释放 COM、PIDL 和菜单句柄。
- `src/WineCabinet/Platform/LibraryFileWatcher.cs`：监控收纳项目所在目录，将原生/外部重命名及删除、剪切完成后的路径变化调度到 UI 线程，同步清单。
- `src/WineCabinet/Platform/ShellActions.cs`：降级菜单调用系统“属性”对话框。
- `src/WineCabinet/Platform/StartupManager.cs`：当前用户 `Run` 键的开机启动登记。`--startup` 模式不访问注册表，手动启动先只读核对，命令变化时才写入；优先使用已安装副本的路径，向调用方返回登记错误。
- `assets/source/launcher.gif`：嵌入程序集的源图标资源。
- `tests/WineCabinet.Tests`：无第三方测试框架的可执行回归测试。
- `scripts/publish.ps1` 与 `src/WineCabinet/Properties/PublishProfiles/*.pubxml`：自包含单文件发布及其输出校验。
- `scripts/install.ps1`：原样复制 EXE 到固定目录，校验文件内容并登记当前用户启动项，读回命令后再通过 Windows 提供程序独立核对；视图不一致时抛错，不继续启动。安装前要求旧实例退出，支持 `-WhatIf` 预览与可选启动。
- `scripts/diagnose-startup.ps1`：只读输出文件是否存在、系统侧启动命令、进程视图是否一致、参数与目标路径匹配情况、启动项状态以及运行实例的路径和命令行。
- `scripts/startup-registry.ps1`：使用内置 CIM 的 `StdRegProv` 读取当前用户 SID 对应的 `HKEY_USERS` 启动项，独立于调用进程的注册表视图；提供程序不可用时返回明确状态，由调用方提示。
- `Start Wine Cabinet.bat`：SDK 检查与安装、架构选择、自包含构建、固定目录安装与启动的一体化入口。
- `.github/workflows/build.yml`：在 Windows 上执行还原、构建、测试与双架构发布验证。
- `.github/workflows/release.yml`：由 `v*` 标签触发，按标签版本号构建并发布 GitHub Release 附件。

## Data flow

`LauncherForm` 接收桌面拖放后交给 `ShortcutLibrary` 持久化；`FlyoutForm` 只通过库的公开方法读取、交换、恢复和记录启动，并订阅 `ShortcutLibrary.Changed` 在清单变化时重建网格。布局选择由 `GridLayoutSettings` 单独存放，切换布局不会改动 `ShortcutLibrary` 中的项目顺序；入口位置由 `LauncherPlacement` 单独存放，移动入口不会影响清单与布局。Windows 专用操作集中在 `Platform`，业务规则不直接调用注册表或 Shell API。

## 窗口几何与交互参数

- 桌面入口固定 96×96：`MinimumSize`/`MaximumSize` 与重写的 `SetBoundsCore` 共同锁定尺寸，`AutoScaleMode.None` 配合清单中的 PerMonitorV2 DPI 模式。窗口圆角半径 13，绘制时的裁剪圆角半径 11，背景色 `(250,250,252)`。
- 面板展开尺寸：九宫格 354×354、16 宫格 464×464，都等于 `12 × 2 + 维度 × 110`，即 12px 内边距加上每格 110px（格内再留 5px 外边距，实际内容 100×100）；面板背景色 `(248,248,250)`，窗口不透明度 0.98，并启用 `CS_DROPSHADOW` 投影。
- 锚点 `FlyoutForm.AnchorOffset = 177`：面板左上角始终位于入口图标中心左上方 177px，即九宫格边长的一半，也就是 (1,1) 格的中心。九宫格围绕图标对称展开，16 宫格向右下扩展，中央应用格始终停在入口图标附近。`ExpandedBounds` 按指针所在屏幕的工作区留 8px 边距钳制；初始位置让面板展开后右边缘距工作区右缘 18px、入口垂直居中。
- 入口位置：没有保存记录时位于主屏工作区右侧、垂直居中，水平坐标为 `工作区右缘 - (面板宽度 - 177) - 48 - 18`（九宫格距右缘 243px，16 宫格 353px），由纯函数 `LauncherPlacementRules.DefaultLocation` 计算。启动时若 `position.json` 记录的坐标在任一屏幕工作区内的可见部分宽高都不少于窗口的一半，则恢复该坐标并钳制回该工作区；没有任何屏幕能容纳时使用默认位置。位置只在拖动结束（`EndExternalMove`）和窗口关闭（`FormClosed`）时写入，坐标未变化则不重复写文件。
- 展开与收起：190ms 定时器按矩形插值并同步不透明度，展开使用 ease-out 三次曲线（0.25→0.98），收起使用二次曲线（0.98→0.25）后隐藏。
- 点击切换：左键按下时记录按下点，松开时比较 `SystemInformation.DragSize` 判定是单击还是拖动。指针移动未超过阈值就松开视为单击：面板未显示时调用 `LauncherForm.ShowFlyout` 展开，已显示时调用 `FlyoutForm.Collapse` 收起。移动超过阈值则视为拖动，只移动入口，松手不会展开面板。入口与托盘图标的左键都使用同一套切换逻辑，不再有悬停展开。鼠标离开面板与入口后 420ms 收起，右键菜单打开或正在拖拽期间不收起，窗口失去激活同样触发延迟收起。
- 页码角标：面板右下角内缩 13px 的 48×20 圆角标签，文本为 `当前页 / 总页数`，窗口尺寸过小时隐藏。
- 系统托盘：`NotifyIcon` 使用入口 GIF 当前帧按 `SystemInformation.SmallIconSize`（96 DPI 下 16×16）缩放的图标，提示文本为 `Wine Cabinet`。左键点击与入口图标一样切换面板的展开与收起，右键打开与入口相同的 `AppMenu`（打开面板、切换九宫格/16 宫格、退出），菜单打开期间面板不自动收起。托盘图标创建失败时只跳过图标，不影响入口与面板；窗口关闭时释放托盘图标与 `Icon`。
- 两个窗口都不是置顶窗口；面板以入口为所有者显示，因此保持在入口之上，但可能被其他应用窗口覆盖。程序不显示任务栏按钮，运行状态通过系统托盘图标体现。

## 布局规则

`GridLayoutRules` 以网格维度（3 或 4）为唯一输入，槽位索引按 `行 × 维度 + 列` 计算：

- 每页容量：`ItemsPerPage(3) = 8`，`ItemsPerPage(4) = 15`。
- 中央应用格：`CenterSlot(3) = 4`，`CenterSlot(4) = 5`；两者都是行列 (1,1)，因此在两种布局中应用图标位于同一物理位置。
- 槽位顺序：3×3 为 `0, 1, 2, 3, 5, 6, 7, 8`；4×4 为 `0, 1, 2, 4, 6, 8, 9, 10, 3, 7, 11, 12, 13, 14, 15`，其中前八项与 3×3 的槽位一一对应，其余七项位于最右列与最下行。
- `FlyoutForm.Rebuild` 遍历全部 `维度²` 个格位并逐个创建控件：中央格绘制应用图标，其余格位按 `Array.IndexOf(ItemSlots, slot)` 取当前页项目，取不到就创建空白格。空白格是白底加 `(218,218,223)` 圆角边框，因此 4×4 的 16 个格位始终全部可见。项目格背景 `(242,242,247)`、悬停 `(229,229,234)`，图标 44×44，名称标签 92×28 且超出时以省略号截断。
- 总页数为 `max(0, (项目数 - 1) / 每页容量) + 1`；重建网格或切换布局都会把当前页钳制回合法范围。

## 数据与持久化

数据目录固定为 `%LOCALAPPDATA%\WineCabinet`，由 `AppStorage` 提供，`ShortcutLibrary`、`GridLayoutSettings` 与 `LauncherPlacement` 共同使用其中的文件：

- `library.json`：`ShortcutItem` 数组，字段为 `Name`、`Path`、`OriginalPath`、`IsShellItem`、`LaunchCount`、`LastLaunchedAt`、`AddedAt`，以缩进 JSON 写出。清单与模型类型都是 `internal`，测试项目通过 `Properties/AssemblyInfo.cs` 中的 `InternalsVisibleTo("WineCabinet.Tests")` 与 `CreateForTesting` 在临时目录直接构造清单。
- `Items\`：从桌面（含公共桌面）移入的文件与文件夹。
- `settings.json`：`{ "GridDimension": 3 | 4 }`。
- `position.json`：`{ "X": <int>, "Y": <int> }`，桌面入口左上角的屏幕坐标。
- `startup-error.log`：启动失败的时间、上下文与完整异常文本，仅在启动阶段出错时写入。

行为约定：

- 只有位于桌面或公共桌面的文件与文件夹会被移动，并记录 `OriginalPath`；其他路径原地登记，`OriginalPath` 为 `null`，恢复时只从面板移除并提示原文件未移动。
- 目录始终受支持；文件扩展名为 `.tmp`、`.ini`、`.db` 时不收纳，被跳过的文件名在拖放结束后汇总提示，扩展名比较不区分大小写。
- 添加时按 `Path` 与 `OriginalPath` 双重去重，避免重复收纳同一目标。
- 收纳与恢复都通过 `UniquePath` 规避重名冲突，依次尝试 ` (2)`、` (3)` 等后缀，不覆盖已存在的文件或目录。
- 加载时过滤掉源文件已不存在的普通项目，并按解析名重新隐藏 Shell 项目；过滤结果只存在于内存中，直到下一次保存才写回磁盘。
- `library.json` 解析失败时以空清单继续运行，不阻塞启动。
- 排序语义：`Reorder`、`MoveBefore`、`Swap` 会清零全部项目的点击计数；`RecordLaunch` 只在项目被提升时清零该项目自身的计数。
- 提升规则：项目索引大于等于当前布局每页容量且 `LaunchCount` 达到 3 时，项目移动到第一页末位并清零计数；第一页内的项目只累加计数、不改变位置。

## 拖放与 Shell 集成

- 桌面 → 面板：`LauncherForm` 同时接受 `FileDrop` 与 `Shell IDList Array`。文件路径直接交给 `ShortcutLibrary.AddFiles`；`ShellDropHelper` 解析 CIDA 结构（`ILCombine` 与 `SHGetNameFromIDList`）得到解析名，能对应真实路径的仍走 `AddFiles`，其余（例如“此电脑”）作为 Shell 项目登记并隐藏桌面图标。单个载荷最多解析 256 项，越界或结构不符的项被跳过。
- 面板内交换：拖动时写入自定义数据格式 `WineCabinet.ShortcutItem`（值为项目名），目标格在 `DragDrop` 中记录待交换对象；`DoDragDrop` 返回后由发起方执行 `Swap`，因此跨页交换与同页交换使用同一路径。空白格不注册放置目标，放到空白格上不会改变顺序。
- 边界翻页：拖拽期间每 80ms 用 `DragNavigation.Decide` 判断一次越界方向，越界翻页一次后必须先回到面板内部才会再次触发（`_edgePageArmed`）；左右越界、第一页向上越界、最后一页向下越界都判定为移出。拖动结束后按释放位置再判定一次，决定是交换还是移出。
- 拖动阈值使用 `SystemInformation.DragSize` 的一半；拖动结束后会抑制紧随其后的一次点击，避免把拖拽误判为启动。
- Shell 桌面图标：`ShellDesktopIconManager` 从解析名中提取 CLSID，同时写入当前用户 `HideDesktopIcons\NewStartPanel` 与 `ClassicStartMenu`，再广播 `SHCNE_ASSOCCHANGED` 与 `WM_SETTINGCHANGE`，无需重启资源管理器。
- 开机启动：登记命令为 `"程序完整路径" --startup`，`StartupManager` 在登录自启动模式下不访问启动项注册表。手动运行先以只读方式打开 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，命令一致时直接返回，仅缺失或变化时以写权限打开并更新 `Wine Cabinet` 值。默认安装目录中的 EXE 存在时优先登记它，否则登记当前 EXE。旧式无参数启动项在安装或手动运行时更新。登记失败由 `Program` 提示，用户禁用启动项的状态不被重置。
- 左键启动项目：Shell 项目通过 `explorer.exe shell:<解析名>` 打开，其余使用 `UseShellExecute` 直接打开。
- 项目右键菜单：`SHParseDisplayName` → `SHBindToParent` → `IShellFolder.GetUIObjectOf(IContextMenu)` → `QueryContextMenu` → `TrackPopupMenuEx` → `InvokeCommand`。使用 Windows 经典菜单外观；Shift 增加 `CMF_EXTENDEDVERBS`，命令按本次菜单 ID 的偏移执行，不按本地化文字猜测。`IContextMenu2/3` 接收子菜单、自绘及键盘选择消息。`Wine Cabinet` 子菜单的命令 ID 与 Shell 范围分离，提供恢复与排序。右键本身不启动项目，原生菜单打开期间暂停自动收起。
- 重命名：识别 Shell 的 `rename` 动词后，由宿主输入窗口收集名称。磁盘上的文件与目录改用 `File.Move`/`Directory.Move` 完成，因为部分 Windows 版本的 `IShellFolder.SetNameOf` 会把原扩展名再追加一次；名称按输入的完整文件名生效，空名称、路径分隔符、无效字符以及以空格或句点结尾的名称被拒绝。非文件系统项目仍通过 `IShellFolder.SetNameOf` 重命名。完成后同步清单与恢复文件名；目录监控处理异步重命名、删除和剪切粘贴后的路径失效，这些通知均调度到 UI 线程。
- 原生命令（含“此电脑”的管理/属性、“回收站”的清空/属性）交给对应 Shell 处理器；操作是否启用取决于系统状态、安装的扩展和权限。菜单加载失败时使用标明原因的基本操作菜单，执行失败时显示错误，用户取消 UAC 不视为失败。
- 单实例：`Program` 以 `Local\WineCabinet.SingleInstance` 命名互斥锁判定，第二个实例直接返回且不提示。

## Artwork pipeline

- 构建期：`WineCabinet.csproj` 把 `$(WineCabinetArtwork)`（默认为 `assets/source/launcher.gif`）原样声明为 `EmbeddedResource`，逻辑名固定为 `WineCabinet.Assets.launcher`；`ValidateLauncherArtwork` 目标在 `PrepareResources` 之前校验文件存在，缺失时直接报错终止构建。构建不重新编码、不缩放、不拆分动画帧，也不生成派生图像。
- 运行期：`EmbeddedImage.LoadLauncher` 把资源复制到 `MemoryStream` 并保持其存活（`Image.FromStream` 要求底层流可用），两个窗口各自调用 `ImageAnimator.Animate` 并在回调中触发重绘与 `UpdateFrames` 推进帧，窗口关闭时 `StopAnimate` 并释放图像与流。
- 绘制：`ArtworkRenderer.CalculateContainRectangle` 取宽高比更小的一方做等比缩放并居中（contain），超宽或超高的图像在两侧或上下留白，不裁剪也不拉伸；`DrawContained` 使用高质量双三次插值与高质量像素偏移。入口与面板中央格共用该方法，分别配合半径 11 与 12 的圆角裁剪。
- 项目图标：优先取 Shell 图标（`SHParseDisplayName` 与 `SHGetFileInfo`，句柄克隆后 `DestroyIcon`），失败则用 `Icon.ExtractAssociatedIcon`，都失败时回退为 44×44 的蓝色渐变圆形占位图。
- 可解码格式由 `System.Drawing` 决定（GIF、PNG、JPEG、BMP、TIFF 等），不引入第三方图像库。

## 构建、发布与 CI

- 根目录 `Directory.Build.props` 统一 `ImplicitUsings`、`Nullable`、确定性构建和语言版本；`.editorconfig` 规定 UTF-8、CRLF、C# 4 空格、JSON/Markdown/YAML 2 空格，并要求文件作用域命名空间。
- `global.json` 指定 SDK `10.0.100` 且 `rollForward: latestFeature`，同一特性带内的更高补丁可直接使用。
- 应用清单 `src/WineCabinet/Properties/app.manifest` 声明 `asInvoker` 与 `longPathAware`。
- 发布配置 `win-x64.pubxml` 与 `win-arm64.pubxml` 启用自包含、单文件、`EnableCompressionInSingleFile`、`IncludeNativeLibrariesForSelfExtract`，关闭 `PublishTrimmed`，调试符号嵌入且不生成独立 PDB。
- `scripts/publish.ps1` 默认清理并重建 `artifacts\publish\<RID>`（清理前校验目标位于 `artifacts` 之下），调用对应发布配置，最后断言 `Wine Cabinet.exe` 存在且输出目录中没有其他文件；通过 `-OutputDirectory` 指定自定义目录时该目录必须为空。
- `Start Wine Cabinet.bat` 按 `PROCESSOR_ARCHITECTURE` 与 `PROCESSOR_ARCHITEW6432` 选择 RID，按 `where dotnet`、`%ProgramFiles%\dotnet`、`%LOCALAPPDATA%\Microsoft\dotnet` 的顺序探测 `10.x` SDK，缺失时优先使用 WinGet，失败则用官方 `dotnet-install.ps1` 安装到 `%LOCALAPPDATA%\Microsoft\dotnet`，最后将本次成功生成的程序安装到固定目录并启动。
- `.github/workflows/build.yml` 在 `windows-latest` 上依次执行还原、构建、回归测试，并分别发布 `win-x64` 与 `win-arm64`、校验两个单文件可执行文件都存在；触发条件为分支推送、拉取请求和手动运行，标签推送交由发布工作流处理。
- `.github/workflows/release.yml` 由推送 `v<版本>` 标签触发，声明 `contents: write` 权限。版本号从标签去掉 `v` 前缀取得，并作为 `Version` 与 `InformationalVersion` 传入构建与发布，使程序集版本、文件版本与展示版本一致；标签名含连字符（如 `v0.2.0-beta.1`）时标记为预发布。工作流按 `build.yml` 的方式构建和测试，再用两个发布配置分别生成单文件可执行文件，断言输出目录中只有 `Wine Cabinet.exe`，打包为 `WineCabinet-<版本>-<RID>.zip`，生成 `SHA256SUMS.txt`，最后用 `gh release create --generate-notes` 创建 Release 并上传三个附件。
- 日常使用通过 `scripts/install.ps1` 将发布结果安装到 `%LOCALAPPDATA%\Programs\WineCabinet` 并登记该路径，避免构建输出清理后启动项失效。更新只替换程序文件，数据仍保存在独立的 `%LOCALAPPDATA%\WineCabinet` 目录。

## 失败处理与降级

非关键路径的失败不会阻止程序启动或使用：

- `library.json` 损坏或不可读 → 以空清单继续运行。
- `settings.json` 不可解析 → 使用九宫格；写入失败 → 本次会话生效但不持久化。
- `position.json` 缺失、不可解析或记录的坐标不在任何已连接屏幕的工作区内 → 入口回到默认的右侧居中位置；写入失败 → 本次会话仍保留移动后的位置，只是重启后不再记住。
- 视觉样式激活失败（单文件版本从 `%TEMP%` 创建激活清单失败，例如临时目录不可写）→ 记录原因后以无主题外观继续启动，不阻止入口窗口显示。
- 启动阶段的其他异常 → 写入 `startup-error.log` 并弹窗显示原因，然后以退出码 `1` 结束，不再静默崩溃。
- 托盘图标无法创建（通知区域不可用或 GDI 绘图失败）→ 跳过托盘图标，入口与面板照常工作。
- 注册表写入被策略阻止 → 启动项登记显示原因，桌面图标开关跳过该步骤，其余功能照常。
- 拖放数据不是受支持的 Shell 载荷，或单项解析失败 → 跳过该项，其余项继续处理。
- 启动项目失败 → 弹窗提示原因；恢复失败 → 弹窗提示原因。
- UI 线程未处理异常 → 通过 `Application.ThreadException` 弹窗提示，程序不退出。

## Testing strategy

`tests/WineCabinet.Tests` 是普通可执行项目（成功返回 `0`，失败写标准错误并返回 `1`），不依赖第三方测试框架，使用临时目录构造清单后覆盖：

- 交换只影响两个位置并清零全部计数；第一页项目点击 3 次不换位；第二页项目第 3 次点击进入第一页末位。
- 16 宫格下第一页可见项目保持位置、后续页项目第 3 次点击进入第 15 个位置。
- 每页容量与中央格索引、4×4 前 8 个槽位与 3×3 槽位对应、后 7 个槽位落在最右列与最下行、16 个格位全部被渲染。
- `settings.json` 缺失时使用九宫格、写入后可重新读出。
- 入口位置：`position.json` 缺失时无记录、写入后可重新读出、内容损坏时回退；默认位置公式、主屏与副屏坐标的精确恢复、断开屏幕后的回退、大部分不可见时的钳制。
- 入口窗口的位置接线：带保存位置的窗口显示后即位于该坐标，移动结束与关闭窗口都会写入当前坐标。
- 拖拽边界决策：上边界回退、下边界前进、左右边界以及首尾页越界移出；仅左键触发启动。
- 程序集内嵌资源存在、可解码且保留多个动画帧；托盘图标按请求尺寸生成，图案缺失时仍有可用的回退图标。
- contain 布局对横版、竖版、正方形图像的矩形计算。
- 启动登记：已有正确命令时不申请写权限，登录自启动在读写均被拒绝时仍不访问注册表；首次与旧式登记使用带引号的路径和 `--startup` 参数，开发版本保留固定安装路径。
- 受管文件恢复：原位置出现文件、收纳位置清空、清单移除对应项目。
- 查询“此电脑”“回收站”和临时文件的真实 Shell 菜单；对临时文件执行 Shell 重命名后验证内容、路径及恢复名称，验证删除/移动事件仅移除已失效的文件项目。系统菜单测试不执行清空回收站或删除系统项目。

窗口动画、圆角与投影、右键菜单与提权、OLE 拖放与跨页交换、Shell 图标隐藏与恢复、开机启动、单实例、真实多显示器环境下的位置恢复、托盘图标的点击与右键菜单、左键单击展开与再次单击收起、拖动结束不误触展开、视觉样式降级与启动失败提示，以及发布后可执行文件的首次运行依赖真实 Windows 桌面，需要在发布前手动冒烟测试。

## Repository hygiene

仓库只包含源码与源资源：`bin`、`obj`、`artifacts`、测试结果和编译生成的二进制输出由 `.gitignore` 排除，不得提交。图标源文件只放在 `assets/source`。文档使用仓库相对路径或环境变量表示位置，不记录本机路径、本机构建结果或测试输出。
