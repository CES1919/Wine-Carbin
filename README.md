# Wine Cabinet

Wine Cabinet 是一个轻量的 Windows 桌面收纳工具，使用 WinForms 构建，目标框架 `net10.0-windows`，不引用任何第三方包。把桌面快捷方式、文件或文件夹拖到屏幕右侧 96×96 的动画图标上即可收入图标面板；左键单击图标展开面板，再次单击收起，项目可以随时启动或恢复到桌面。图标面板也可以通过系统托盘图标随时打开。

## 功能

- 收纳桌面快捷方式、文件与文件夹，原文件不会被删除，可随时恢复。
- 支持“此电脑”“回收站”等 Windows Shell 图标，收纳后隐藏桌面上的对应图标。
- 九宫格与 16 宫格两种布局，支持滚轮翻页、跨页拖动交换和拖出面板移出项目。
- 常用项目在多次打开后自动进入第一页末位。
- 收纳项目使用 Windows 原生右键菜单，支持快捷方式、文件、文件夹及系统图标自身的操作；面板排序与恢复操作放在菜单末尾的 `Wine Cabinet` 子菜单中。
- 圆角界面与程序集内嵌的动画 GIF 图标，单实例运行。
- 常驻系统托盘：左键点击托盘图标展开或收起图标面板，右键打开设置菜单。
- 记住图标位置，关机重启后仍回到上次放置的位置。
- 以当前用户身份开机启动，不需要管理员权限。

## 系统要求

- Windows 10 或 Windows 11（x64 / ARM64）
- 使用自包含发布版：无需预装 .NET
- 从源码构建：Git 与 .NET 10 SDK（`global.json` 指定 `10.0.100`）

## 快速开始

```powershell
dotnet restore WineCabinet.sln
dotnet build WineCabinet.sln -c Release --no-restore
dotnet run --project src/WineCabinet/WineCabinet.csproj -c Release --no-build
```

也可以运行 `Start Wine Cabinet.bat`：脚本会检查并按需安装 .NET 10 SDK，根据当前架构发布自包含单文件，把本地构建安装到 `%LOCALAPPDATA%\Programs\WineCabinet`，登记启动项后启动。更新前先退出旧实例。首次自动安装需要网络连接。

## 使用说明

1. 收纳：将桌面快捷方式、普通文件或文件夹拖到右侧图标上，拖放完成后面板自动展开。
   - 位于桌面（含公共桌面）的项目会移入收纳目录，并记录原始位置以便恢复。
   - 其他位置的文件与文件夹只在面板中登记，原文件留在原处；这类项目的右键菜单显示“从图标面板移除”。
   - 同一路径（或同一原始路径）重复拖入会被忽略；扩展名为 `.tmp`、`.ini`、`.db` 的文件不会收纳，拖放结束后会提示被跳过的文件名。文件夹始终支持。
   - “此电脑”“回收站”等 Shell 图标只能通过直接拖放添加，添加后会隐藏桌面上的对应图标。
2. 启动项目：左键单击图标展开面板，再次单击图标收起；左键单击项目即可打开，右键单击不会启动。中央应用格使用与桌面入口相同的动画 GIF，页码显示在面板右下角。上下滚轮翻页。
3. 右键菜单：
   - 桌面入口：`打开图标面板`、`扩展为 16 宫格` / `切换回九宫格`、`Exit Wine Cabinet`。
   - 面板中央应用图标：`扩展为 16 宫格` / `切换回九宫格`、`Exit Wine Cabinet`。
   - 收纳项目：由 Windows Shell 提供原生菜单，对应资源管理器的经典菜单（Windows 11 的“显示更多选项”）。内容随文件类型、Windows 版本、已安装的扩展和系统策略变化；例如快捷方式的打开/管理员运行/属性，文件的打开方式/复制/剪切/删除/重命名，以及“此电脑”的管理/属性和“回收站”的清空/属性。不可用的操作由系统禁用。按住 Shift 右键可显示扩展操作。
   - 原生菜单末尾的 `Wine Cabinet` 子菜单提供`恢复到桌面`或`从图标面板移除`、`向前移动`、`向后移动`。
   - 文件重命名会同步面板名称、实际路径和恢复时的文件名；原生删除或剪切后粘贴到其他位置会移除已失效的面板项目。原生“删除”针对实际文件，与恢复到桌面或仅从面板移除不同。
   - 原生菜单使用 Windows 自身的外观，入口和中央应用格继续使用圆角应用菜单。Shell 菜单加载失败时显示标明原因的基本操作菜单。
4. 切换布局：在右键菜单中直接切换，切换后回到第一页，选择保存到 `settings.json`。
   - 九宫格：3×3 布局，应用图标位于中央，每页 8 个收纳项目。
   - 16 宫格：4×4 布局，应用图标位于 (1,1) 格，左上 3×3 区域与九宫格使用相同的槽位顺序，其余 7 个格位显示后续项目，每页 15 个收纳项目。
   - 未被项目占用的格位显示为带浅灰边框的白色空格；4×4 布局始终绘制全部 16 个格位。
5. 常用项目提升：第一页之外的项目累计打开 3 次后进入第一页末位（九宫格第 8 个、16 宫格第 15 个项目位），并清零该项目的点击次数；第一页内的项目不会因为点击而换位。手动排序（向前/向后移动、拖动交换）会清零所有项目的点击次数。
6. 拖动与交换：按住图标拖到另一个图标上即交换位置，其他项目不变。
   - 拖出上边界切换到上一页、拖出下边界切换到下一页；越界翻页一次后需要先回到面板内部才能再次翻页。翻页后把项目放到目标图标上即可跨页交换。
   - 拖出左右边界、第一页上边界或最后一页下边界并释放，会把项目移出面板：来自桌面的项目恢复到原目录（同名文件已存在时使用 `(2)`、`(3)` 等后缀，不覆盖已有文件），原地登记的项目只从面板移除，原文件不受影响。
   - 放到空白格上不会改变顺序。
7. 移动与位置记忆：按住桌面图标即可拖动；面板展开后按住中央应用图标也能移动，面板会跟随图标移动。首次运行时图标位于主屏工作区右侧并垂直居中，拖动范围限制在指针所在屏幕的工作区内。程序在移动结束和退出时记住图标位置，重启电脑或重新启动程序后回到上次的位置；若上次所在的屏幕已断开或分辨率变化导致位置不可见，则回退到默认的右侧位置。
8. 系统托盘：运行实例在通知区域显示与入口相同图案的小图标。左键点击展开或收起图标面板，右键打开设置菜单（`打开图标面板`、切换九宫格/16 宫格、`Exit Wine Cabinet`），与入口右键菜单一致。图标被 Windows 收进溢出区时，可在任务栏设置中固定显示。
9. 退出：使用入口、面板或托盘菜单中的 `Exit Wine Cabinet`。

面板展开与收起：左键单击入口图标展开面板，再次单击收起；通过托盘图标左键点击也能展开或收起。按住左键并移动是拖动操作，因此按住时不会触发展开；鼠标离开面板与图标约 420ms 后自动收起，右键菜单打开或正在拖动时不会收起。展开与收起动画约 190ms。

## 数据存储

程序把数据保存在 `%LOCALAPPDATA%\WineCabinet`：

```text
%LOCALAPPDATA%\WineCabinet\library.json     收纳清单
%LOCALAPPDATA%\WineCabinet\Items\           从桌面移入的文件与文件夹
%LOCALAPPDATA%\WineCabinet\settings.json    布局选择（3 或 4）
%LOCALAPPDATA%\WineCabinet\position.json    桌面图标的位置
%LOCALAPPDATA%\WineCabinet\startup-error.log 启动失败记录
```

- 该目录由程序自动创建。
- 清单损坏或无法读取时，程序以空清单启动，不影响面板打开。
- 源文件已不存在的普通项目不会显示在面板上，该结果会在下一次保存清单时写回文件。
- `settings.json` 缺失或内容非法时使用九宫格；写入失败时本次会话仍会切换布局，只是不会被记住。
- `position.json` 记录图标左上角的屏幕坐标，在移动结束和退出时写入；文件缺失、无法读取或坐标不在任何已连接屏幕的工作区内时，图标回到默认的右侧居中位置。
- 启动阶段出错时把异常写入 `startup-error.log` 并弹窗提示，不再静默崩溃；视觉样式无法启用（例如 `%TEMP%` 不可写）时只记录原因，程序以无主题外观继续启动。
- 关闭程序后复制整个 `%LOCALAPPDATA%\WineCabinet` 目录即可完成备份。
- 注册表中只写入当前用户的启动项与桌面图标隐藏开关。

## 配置桌面图标

默认图像是 `assets/source/launcher.gif`，替换该文件后重新构建即可；图像会原样嵌入 EXE，动画 GIF 的全部帧都会保留。

也可以在构建时指定其他图像：

```powershell
dotnet build WineCabinet.sln -c Release -p:WineCabinetArtwork="assets\source\my-icon.gif"
```

指定的文件不存在时构建会直接报错。运行时使用 Windows/.NET 自带的 `System.Drawing` 解码，并把完整图像等比缩放到桌面入口或面板中央区域：非正方形图像居中显示，并在两侧或上下保留空白，不裁剪也不拉伸。可解码的格式由 `System.Drawing` 决定（GIF、PNG、JPEG、BMP、TIFF 等）。

## 构建与测试

```powershell
dotnet restore WineCabinet.sln
dotnet build WineCabinet.sln -c Release --no-restore
dotnet run --project tests/WineCabinet.Tests/WineCabinet.Tests.csproj -c Release --no-build
```

回归测试是可执行程序，全部通过时返回退出码 `0`，失败时把异常写入标准错误并返回 `1`。覆盖排序与交换、两种布局的分页容量与槽位映射、点击提升、边界移出判定、仅左键启动、布局选择与入口位置的读写、入口位置恢复与不可见回退、受管文件的恢复、内嵌多帧 GIF 的可读性、托盘图标尺寸与回退，以及横向、纵向、正方形图像的等比完整缩放。Shell 测试只查询系统菜单，并在临时目录验证文件重命名、路径同步与恢复，不执行清空回收站或删除系统项目。

窗口动画、圆角与投影、右键菜单与提权、OLE 拖放与跨页交换、Shell 图标隐藏与恢复、开机启动、单实例、托盘图标的点击与右键菜单、左键单击展开与再次单击收起、拖动结束不误触展开、真实多显示器下的位置恢复、视觉样式降级与启动失败提示，以及发布后可执行文件的首次运行，需要在真实 Windows 桌面上手动验证。

## 自包含发布

```powershell
.\scripts\publish.ps1 -RuntimeIdentifier win-x64
.\scripts\publish.ps1 -RuntimeIdentifier win-arm64
```

如果执行策略阻止脚本运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -RuntimeIdentifier win-x64
```

脚本使用 `src/WineCabinet/Properties/PublishProfiles/<RID>.pubxml` 发布，并校验输出目录中只有 `Wine Cabinet.exe`。发布配置启用自包含、单文件、压缩与本机库自解压，不启用 trimming，调试符号嵌入该文件。运行时与图标资源都包含在这一个文件中，目标电脑不需要安装 .NET。

## 安装与更新

构建完成后，先退出正在运行的 Wine Cabinet，再执行以下命令，把程序安装到固定目录并登记登录自启动：

```powershell
.\scripts\install.ps1 -Executable '.\artifacts\publish\win-x64\Wine Cabinet.exe' -Launch
.\scripts\diagnose-startup.ps1
```

ARM64 使用 `win-arm64` 输出。默认安装位置为 `%LOCALAPPDATA%\Programs\WineCabinet\Wine Cabinet.exe`；`-Launch` 表示安装完成后立即启动，省略它则只安装和登记启动项。脚本会校验复制前后的文件内容及登记的启动命令，并通过 Windows 注册表提供程序独立核对系统看到的值；若与当前进程看到的值不一致，会报错并停止启动。如果系统提供程序不可用，会明确提示未完成独立核对。`-WhatIf` 可预览操作而不执行安装。

更新时重新构建并执行相同命令，或直接运行 `Start Wine Cabinet.bat`。安装目录与 `%LOCALAPPDATA%\WineCabinet` 数据目录相互独立，更新程序不会清除已有收纳清单或文件。

## 开机启动

启动命令保存在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `Wine Cabinet` 值中，格式为 `"程序完整路径" --startup`。登录自启动时不访问该注册表项，直接显示桌面入口。手动运行时先以只读方式核对已有命令，只有缺失或不一致时才申请写权限；登记失败会显示原因，但仍可继续使用程序。不会安装服务或常驻第二个进程，也不会重新启用被用户禁用的 Windows 启动项。

建议使用上述安装命令，避免构建目录被清理造成启动路径失效。默认安装目录中的 EXE 存在时，手动运行其他位置的版本也优先保留该固定启动路径；未安装时才登记当前 EXE 的位置。也请确认 Windows“设置 → 应用 → 启动”中的 Wine Cabinet 已启用。

`scripts/diagnose-startup.ps1` 默认通过 Windows 注册表提供程序读取当前用户的实际启动命令，同时对比当前进程看到的值，并列出运行实例的路径和命令行；该操作只读。`SystemRegistryVerified` 表示是否完成系统侧核对，`RegistryViewsMatch` 表示两种读取结果是否一致，`HasStartupArgument` 应为 `True`。可通过 `-Executable` 指定期望启动的文件，检查 `StartupPathMatches` 是否为 `True`。若发现视图不一致，请从普通 Windows PowerShell 窗口运行安装脚本后重新诊断。

## 项目结构

```text
src/WineCabinet/           WinForms 应用
  Core/                    业务规则与持久化
  Platform/                Windows 平台集成
  UI/                      窗口、动画与菜单
assets/source/             嵌入程序集的图标资源
tests/WineCabinet.Tests/   回归测试程序
docs/architecture.md       架构说明
scripts/publish.ps1        自包含发布脚本
scripts/install.ps1        固定目录安装、登记与启动
scripts/diagnose-startup.ps1  只读自启动诊断
scripts/startup-registry.ps1  系统侧启动项读取辅助函数
.github/workflows/         Windows CI
Start Wine Cabinet.bat     SDK 检查、发布与启动入口
```

设计细节见 [`docs/architecture.md`](docs/architecture.md)。

## 许可证

本项目采用 [MIT License](LICENSE)。
