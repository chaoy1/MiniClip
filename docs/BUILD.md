# 构建、运行与发布

本文档记录 MiniClip 从源码到可运行程序及安装包的流程：前置条件、构建、运行、发布、自检、安装、卸载和开机启动。

本文档里的命令都在本机实际执行过并记录了下真实输出。工作目录假定为仓库根目录 `D:\Documents\Projects\MiniClip`。

## 前置条件

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11。`app.manifest` 只声明了 Windows 10/11 的 `supportedOS` 项。 |
| 构建用 SDK | .NET 10 SDK。本机实际使用的是 **10.0.401**（`D:\Software\dotnet-sdk\sdk\10.0.401`）。 |
| 目标框架 | `net10.0-windows`，启用 WPF（`UseWPF`）与 `UseWindowsForms`（只为了 `System.Drawing` 画托盘图标）。 |
| 运行用运行时 | **.NET 10 Desktop Runtime**（提供 `Microsoft.WindowsDesktop.App` 10.0.x）。 |
| 运行用运行时（本机现状） | `C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.1`，另有 `Microsoft.NETCore.App 10.0.1`。 |

两个需要提前说明的事实：

1. **SDK 不在 `PATH` 上。** 本机的 `D:\Software\dotnet-sdk` 是解压出来的独立 SDK 目录，`PATH` 里的 `C:\Program Files\dotnet\dotnet.exe` 只是一个只带运行时的宿主（`dotnet --list-sdks` 返回空）。所以构建前必须先把 SDK 目录放到 `PATH` 前面。
2. **运行时不是随程序一起发布的。** 默认配置是框架依赖（`SelfContained=false`），`MiniClip.runtimeconfig.json` 声明需要 `Microsoft.NETCore.App` 与 `Microsoft.WindowsDesktop.App`，版本下限都是 `10.0.0`，实际解析到机器上已安装的 10.0.x。目标机器没有装 .NET 10 Desktop Runtime 时，双击 `MiniClip.exe` 会提示缺少运行时；这种情况请改用自包含发布（见下文“发布”）。

## 构建

```powershell
$env:PATH = "D:\Software\dotnet-sdk;$env:PATH"
dotnet build src\MiniClip\MiniClip.csproj -c Release
```

本机实测输出：

```
  MiniClip -> D:\Documents\Projects\MiniClip\src\MiniClip\bin\Release\net10.0-windows\MiniClip.dll

已成功生成。
    0 个警告
    0 个错误
```

说明：

- SDK 已经在 `PATH` 上之后，`dotnet build` 可以直接用；但仓库根目录下**没有** `.sln` / `.slnx`，也没有 `global.json`，所以裸跑 `dotnet build` 会因为没有找到项目而失败，需要带上项目路径，或者 `cd src\MiniClip` 再执行。
- Debug 配置同理：`dotnet build src\MiniClip\MiniClip.csproj`。
- 构建产物是框架依赖的：5 个文件、合计约 0.38 MB（`MiniClip.dll` 172 KB、`MiniClip.exe` 160.5 KB、`MiniClip.pdb` 55.4 KB、`MiniClip.deps.json`、`MiniClip.runtimeconfig.json`）。

## 运行

```powershell
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe
```

MiniClip 是托盘程序，**不会出现主窗口**：启动后进程常驻后台，只在通知区域显示一个图标。找不到窗口是正常的，去托盘找图标。

- 程序启动时会创建单实例互斥体，第二个实例直接退出。
- 托盘图标：使用全局快捷键呼出候选框，双击托盘图标打开设置，右键打开紧凑菜单。菜单的“外观设置”包含浅色、深色、跟随系统；设置页可更换快捷键、调整开机启动并打开历史文件位置。
- 退出只能从托盘右键菜单的“退出 MiniClip”，或在任务管理器里结束进程。
- 首次运行会显示一次黑白提示，说明历史以明文保存在本机。

从命令行启动时不会打印任何内容，命令会一直挂着直到退出。要确认它在跑，看进程或托盘图标。

## 发布

规划书第二十二节与第二十九节第八阶段要求“打包前明确是依赖已安装运行时，还是自包含发布”。下面是**五种发布方式的实测对比**，不是估算。

### 实测对比

量法：每种方式用 `dotnet publish` 真发布、真运行，跑 `MiniClip.exe --run <秒>` 多次采样常驻内存（`GetProcessMemoryInfo`），启动时间取应用自己写入的首行时间戳与进程创建时间的差值，取中位数。复现脚本：[tools/measure-publish.py](../tools/measure-publish.py)、[tools/measure-weight.py](../tools/measure-weight.py)、[tools/compare-singlefile.py](../tools/compare-singlefile.py)。

**磁盘占用看"整个目录"那一列**，不要看主 exe：非单文件模式下真正的代码在 `MiniClip.dll` 里（301 KB），主 exe 只有 179 KB，只算 exe 会严重低估。

| 发行方式 | 整个目录 | 文件数 | 常驻内存 | 启动（中位） | 需要装运行时 |
| --- | ---: | ---: | ---: | ---: | :---: |
| **框架依赖（推荐）** | **0.55 MB** | 5 | 58.1 MB | **226 ms** | 是 |
| 框架依赖 + 单文件 | 0.55 MB | 2 | 58.3 MB | 229 ms | 是 |
| 自包含（当前默认） | 149.1 MB | 7 | 60.1 MB | 257 ms | 否 |
| 自包含 + 压缩 | 71.0 MB | 7 | **160.8 MB** | 440 ms | 否 |
| 自包含 + 不打包单文件 | 155.7 MB | 269 | — | — | 否 |

**安装包用的就是最后一行**：`tools/build-installer.ps1` 以 `--self-contained true -p:PublishSingleFile=false` 发布，得到 269 个文件、约 155.7 MB 的目录，再由 Inno Setup 用 `lzma2/ultra64` 压成 48.8 MB 的安装程序。这和第 3 条的“压缩”不是一回事——Inno 的压缩只作用于安装包本身，安装完成后磁盘上就是那 155.7 MB 的普通文件，运行时不会被解压进内存。

四条关键结论：

1. **141 MB 全部是 .NET 运行时，没有一分是 MiniClip 自己**。框架依赖版整个目录 **0.55 MB**——同一个程序，差约 270 倍。项目源码不过 3.5 MB。
2. **自包含买不到内存和速度上的任何好处**。常驻内存 60.1 MB vs 58.1 MB，启动 257 ms vs 226 ms——几乎一样。它唯一的价值是"目标机器不用装 .NET 10 桌面运行时"。
3. **压缩是个陷阱**。文件从 141 MB 降到 63 MB，但**常驻内存从 60 MB 涨到 161 MB**、启动从 257 ms 涨到 440 ms——因为压缩包每次启动都要在内存里解开。用内存换磁盘，对一个常驻后台的工具是亏的。
4. **不打包单文件更差**：155.7 MB / 269 个文件，比单文件还大。140 MB 的运行时横竖都要带着，单文件已经是最紧凑的形态。

另外实测否掉了 **ReadyToRun**：`-p:PublishReadyToRun=true` 让文件从 0.47 MB 涨到 0.84 MB，启动反而从 230 ms 变成 253 ms（最慢 552 ms）。对 WPF 这种启动路径，编译进原生的收益抵不过加载更大的映像。不要开。

### 单文件 vs 非单文件：怎么选

这两个的**磁盘占用基本一样**（559 vs 560 KB），运行时行为也几乎一样。差别只在**打包形态**：

| | 非单文件 | 单文件 |
| --- | --- | --- |
| 文件 | `MiniClip.exe` 179 KB + `MiniClip.dll` 301 KB + 2 个 json + pdb = **5 个** | `MiniClip.exe` 483 KB + pdb = **2 个** |
| 程序集位置 | 磁盘上独立存在，`MiniClip.dll` 在进程模块列表里 | 内嵌在 exe 中，进程里只有 `MiniClip.exe` 一个模块 |
| 主 exe | 179 KB | 483 KB |
| 启动 / 内存 | 226 ms / 58.1 MB | 229 ms / 58.3 MB（差异在噪声范围内） |
| 能否只替换 dll 更新 | 可以 | 不行，必须重发整个 exe |
| 首次运行的落盘 | 无 | 无（纯框架依赖的单文件把托管程序集留在内存里，不解包到 `%TEMP%`） |

**结论：功能上没有任何区别，0.47 MB 和 0.17 MB 的对比是错觉**——那只是把同一个 301 KB 的 dll 从"旁边一个文件"挪到了"exe 里面"。整包都是 0.55 MB。

选非单文件：更常见、可单独替换 dll、便于排查（进程模块列表里能看到 `MiniClip.dll`）。自己用、团队内部用选这个。

选单文件：如果分发对象不那么懂电脑，"就一个 exe，双击"比"5 个文件别删"更省解释。代价是文件本身大一点，但整包一样大。

### 方案一：框架依赖（推荐）

```powershell
$env:PATH = "D:\Software\dotnet-sdk;$env:PATH"
dotnet publish src\MiniClip\MiniClip.csproj -c Release -r win-x64 --self-contained false
```

加 `-p:PublishSingleFile=true` 则打成单文件，见上表。

- 产物：`src\MiniClip\bin\Release\net10.0-windows\win-x64\publish\`，**整个目录 0.55 MB**。
- 目标机器需要 **.NET 10 Desktop Runtime**（`Microsoft.WindowsDesktop.App`）。没装会提示缺少运行时。
- 本机已确认装有 `Microsoft.WindowsDesktop.App 10.0.1`（`C:\Program Files\dotnet\shared`），所以这个产物在这里可以直接双击运行。
- 升级运行时不必重新发布。

### 方案二：自包含单文件（只在目标机器无法装运行时时用）

```powershell
dotnet publish src\MiniClip\MiniClip.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

- 产物 141.22 MB。目标机器不需要装 .NET，拷过去就能跑。
- **不要加 `-p:EnableCompressionInSingleFile=true`**：省 78 MB 磁盘，代价是 +100 MB 常驻内存和 +183 ms 启动（见上表）。
- **不要加 `-p:PublishTrimmed=true`**：WPF 与 WinForms 均不被 SDK 支持裁剪，会直接构建失败（`NETSDK1168` / `NETSDK1175`）。这一点实测过，不是推测。
- 所谓“单文件”只指托管程序集打包进 exe，`D3DCompiler_47_cor3.dll`（4.52 MB）、`wpfgfx_cor3.dll`（1.87 MB）、`PresentationNative_cor3.dll`（1.18 MB）等 WPF 原生库仍然独立存在，所以目录是 149 MB 而不是 141 MB。

### 建议怎么选

| 你的情况 | 选 |
| --- | --- |
| 自己用 / 团队内部 / 能控制目标机器 | **框架依赖**（0.47 MB）。装一次 .NET 10 桌面运行时，之后体积和启动都最优 |
| 给一台不确定装没装 .NET 的机器，且对方不方便装 | 自包含单文件（141 MB）。这是唯一不需要解释运行时安装的方案 |
| 想“又小又不用装运行时” | **做不到**。那 140 MB 就是 .NET 桌面运行时的体积，只有“让对方装一次”和“自己背着走”两条路 |

规划书第二十二节明确 `MiniClip.exe` 是启动入口，**不预设发行包只能有一个文件**——是否单文件由发布模式决定，不影响程序本身，所以这个选择随时可以改。

## 自检

```powershell
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --selftest <输出目录>
```

例如：

```powershell
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --selftest D:\temp\miniclip-selftest
```

行为：

- 输出目录可以省略；省略时写到 `%TEMP%\miniclip-selftest`。目录不存在会自动创建。
- 每一项检查打印一行 `[PASS]` / `[FAIL]`，最后打印 `ALL CHECKS PASSED` 或 `N CHECK(S) FAILED`。
- **进程退出码就是失败项数**：全部通过是 `0`，一项失败是 `1`，以此类推。可以直接在脚本里判断退出码。
- 自检会在输出目录写入 `selftest.log`，以及候选框的 PNG 快照（深色 `popup-default.png`、浅色 `popup-light.png`、多行、长行、空状态和 100 条滚动状态等）。
- 自检跑完就退出，不会启动常驻 MiniClip：它会临时注册并释放测试快捷键、短暂创建测试托盘图标，并为往返检查写入系统剪贴板。运行自检前请先完成当前剪贴板内容的使用；它可以与常驻实例并存，此时 Alt + V 被占用属于预期结果。为避免剪贴板测试相互干扰，不要并行运行两份自检。
- `--selftest` 和 `--self-test` 两种写法都接受。

> **证据取舍**：`selftest.log` 由 `File.AppendAllText` 追加写入，**不是独占的**。如果同一台机器上同时有别的进程往同一个输出目录跑自检，两个进程的行会交错，日志可能在半途断掉、看起来像卡死。做归档证据时以**控制台输出**为准（重定向到自己命名的文件），日志只当辅助。

## 限时运行（`--run`）

```powershell
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --run 60
```

启动**真正的** MiniClip（注册快捷键、创建托盘图标、监听剪贴板），常驻指定秒数后走完整退出流程并结束进程，退出码 `0`。

存在的理由：MiniClip 没有窗口也没有控制台，"它能否长时间静默常驻"从外面看不出来——更麻烦的是，从脚本里启动的进程往往会随该脚本所在作业对象一起被结束，于是看起来像程序自己退了。这个开关把"启动 → 常驻 → 干净退出"压缩进一条命令里，可以观察诊断日志里的 30 秒心跳行和退出序列：

```
lifecycle  workingSet=56MB private=13MB
lifecycle  timed run complete after 60s
lifecycle  exit requested
lifecycle  history flushed
lifecycle  Application.Exit
lifecycle  dispatcher shutdown started
lifecycle  dispatcher shutdown finished
lifecycle  ProcessExit
```

秒数会被限制在 1–3600 之间。注意它和其他模式一样会把生命周期行写进诊断日志——日志里只有状态和数值，**不含剪贴板原文**。

当前检查结果与自检**不覆盖**的场景见 [VERIFICATION-2026-09-27-reliability-menu.md](VERIFICATION-2026-09-27-reliability-menu.md)；较早版本的详细记录见 [VERIFICATION-2026-09-27.md](VERIFICATION-2026-09-27.md) 和 [VERIFICATION.md](VERIFICATION.md)。

## 安装包

安装包使用自包含 `win-x64` 发布：运行所需的 .NET 10 Desktop Runtime 已随程序打包。构建脚本发布的是 **269 个文件、约 155.7 MB** 的自包含目录，再由 Inno Setup 以 `lzma2/ultra64` 压缩；当前产物 `dist\MiniClip-Setup-1.0.0-x64.exe` 为 **48.8 MB**（51,203,024 字节）。构建需要 .NET 10 SDK 和 Inno Setup 7；后者的 `ISCC.exe` 可通过参数指定：

```powershell
& .\tools\build-installer.ps1 `
  -DotnetExe 'D:\Software\dotnet-sdk\dotnet.exe' `
  -IsccExe "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
```

输出位于 `dist\MiniClip-Setup-1.0.0-x64.exe`。构建脚本会按项目版本命名安装包，并打印 SHA-256；版本号是用显式 UTF-8 读取从 `.csproj` 里取的，原因见 [VERIFICATION.md](VERIFICATION.md) 的“不能回退的构建配置结论”。

安装向导始终显示目录选择页（`DisableDirPage=no`），默认安装到当前用户的 `%LOCALAPPDATA%\Programs\MiniClip`——这个位置用户可写，“数据跟着安装目录走”才成立；也可以改成任意有写入权限的位置，不要求管理员权限。安装任务有两项，**默认都不勾选**：

| 任务 | 内容 | 默认状态 |
| --- | --- | --- |
| `autostart` | 开机自动启动 MiniClip（写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `MiniClip` 值） | 不勾选；若该值已经存在，向导会自动勾上以反映当前实际状态 |
| `desktopicon` | 创建桌面快捷方式 | 不勾选 |

安装时会在安装目录写入标记文件 `MiniClip.portable`，它决定数据放在哪里，见下一节。安装完成页可选择立即运行。安装或卸载前若 MiniClip 正在运行，向导会提示先退出；即使没退出，安装/卸载过程也会**按可执行文件的完整路径**结束本安装目录下的那个实例——刻意不用按映像名结束，否则会把从别处运行的绿色版或另一个安装目录的实例一起杀掉。

无界面安装可使用 Inno Setup 的 `/VERYSILENT /DIR="<目录>" /TASKS="autostart,desktopicon"`；不传 `/TASKS` 时两项都不执行，也就是首次安装默认不开机启动、不建桌面快捷方式。更换安装目录时请先卸载旧安装，避免旧目录留下未被新安装器管理的文件。安装包为当前用户安装，不包含代码签名证书。

### `MiniClip.portable` 标记文件

这个空标记文件是数据位置的开关。它来自 [installer/portable-marker.flag](../installer/portable-marker.flag)，由 `installer/MiniClip.iss` 的 `[Files]` 条目复制到安装目录并改名为 `MiniClip.portable`：

- **标记存在，且安装目录可写**：[src/MiniClip/Settings/AppPaths.cs](../src/MiniClip/Settings/AppPaths.cs) 判定为便携布局，`history.json`、`settings.json` 和诊断日志都写进 `<安装目录>\data\`。这是安装包的默认形态，也是“卸载不留痕迹”成立的前提。
- **删掉标记**：程序下次启动时找不到它，就回到 `%LOCALAPPDATA%\MiniClip\`。注意这不会搬走已有数据——原来 `data\` 里的历史仍留在安装目录，新写入的换到用户数据目录。
- **标记存在但安装目录不可写**（普通用户装进 `C:\Program Files` 就是这种情况）：程序回退到 `%LOCALAPPDATA%\MiniClip\`，并在托盘提示「安装目录不可写，历史已改存到用户数据目录」，而不是静默丢掉历史。“可写”是用真实的创建/写入/删除探针测出来的，不是看目录是否存在——目录存在却拒绝写入正是最常见的失败形态。

## 卸载与数据

数据写在哪里由可执行文件旁边有没有 `MiniClip.portable` 决定，启动时解析一次（[src/MiniClip/Settings/AppPaths.cs](../src/MiniClip/Settings/AppPaths.cs)）。三种情况：

| 情况 | 数据目录 | `AppPaths.Mode` |
| --- | --- | --- |
| 通过安装包安装（有标记，且安装目录可写） | `<安装目录>\data\` | `Portable` |
| 绿色发布包，或从 `bin\` 直接运行（没有标记） | `%LOCALAPPDATA%\MiniClip\` | `LocalAppData` |
| 有标记但安装目录不可写 | 回退到 `%LOCALAPPDATA%\MiniClip\`，托盘提示「安装目录不可写，历史已改存到用户数据目录」 | `LocalAppDataFellBack` |

三个写入者都走这同一个入口：[src/MiniClip/Storage/JsonStorage.cs](../src/MiniClip/Storage/JsonStorage.cs)（`DefaultHistoryPath` 是每次重新求值的属性，不是启动时缓存的字段）、[src/MiniClip/Settings/SettingsStore.cs](../src/MiniClip/Settings/SettingsStore.cs) 和 [src/MiniClip/Diagnostics/DiagnosticsLog.cs](../src/MiniClip/Diagnostics/DiagnosticsLog.cs)；诊断日志和数据放在同一个目录。两种布局下目录里的文件相同：

| 文件 | 内容 |
| --- | --- |
| `history.json` | 剪贴板历史，明文 JSON 字符串数组，最多 100 条，并受 500,000 字符总量限制 |
| `settings.json` | 用户设置：快捷键、列表方向、是否已显示过首次运行提示 |
| `miniclip-<pid>-<时间戳>-<序号>.log` | 诊断日志。保留最近 7 天，单文件最多 1 MB，目录总量最多 10 MB；启动时和每日维护时清理。只记录条数、状态和结果，不含剪贴板原文 |
| `history.corrupt-<时间戳>.json` | 仅在历史文件损坏时出现，是保留下来的原文件 |
| `settings.corrupt-<时间戳>.json` | 仅在设置文件损坏时出现 |
| `history.json.tmp` | 仅在写入过程中异常中断时可能残留，可以安全删除 |

**通过安装包安装的版本，卸载会删除数据目录里的全部内容。** 卸载器移除：安装文件、开始菜单入口（勾选过 `desktopicon` 时还有桌面快捷方式）、卸载注册信息、`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `MiniClip` 值、Windows 启动管理器中对应的状态（`StartupApproved\Run` 与 `Run32`），以及数据目录本身。具体清理动作写在 `installer/MiniClip.iss` 的 `[UninstallDelete]` 与 `[Code]` 里，一共三个路径：

- `<安装目录>\data\`：整个子树，历史、设置、日志和损坏文件备份都在里面
- `<安装目录>\MiniClip.portable`：标记文件
- `%LOCALAPPDATA%\MiniClip`：整个子树，针对旧版本或用户手工拷贝的绿色版安装

`CurUninstallStepChanged` 在 `usPostUninstall` 里还会再取一次 `{app}\data` 并 `DelTree`：用户可以装到任意位置，只靠 `[UninstallDelete]` 里的常量路径不够稳。卸载将永久删除上述历史、设置、日志及损坏文件备份；需要保留的内容应在卸载前自行备份。安装目录里的其他文件不会被删——`[UninstallDelete]` 只列了上面三个路径，安装文件被移除、`data\` 被删除之后目录清空并随之移除；如果用户往安装目录里放了别的东西，目录会保留。Windows 自己维护的预取、最近使用等系统缓存不属于 MiniClip 数据。删除文件不等于安全擦除磁盘数据。

直接使用绿色发布包的版本没有卸载器：删掉发布文件只删掉程序，`%LOCALAPPDATA%\MiniClip\` 里的历史、设置、日志以及 `HKCU\...\Run` 的开机启动值都还在，需要手动清理。托盘的“清空历史”只删 `history.json`，不删 `settings.json` 和诊断日志。

## 开机启动

由设置对话框里的“开机自动启动”开关控制，实现方式是**当前用户**的 `Run` 注册表值：

```
键：  HKCU\Software\Microsoft\Windows\CurrentVersion\Run
名称：MiniClip
数据："<MiniClip.exe 的完整路径>"（带引号，路径含空格也能正确解析）
```

- 不需要管理员权限，不需要计划任务，不需要启动文件夹快捷方式。
- 这项设置对当前用户可见、可改：任务管理器的“启动”选项卡里会出现 `MiniClip`，可以在那里直接禁用。
- 程序读这个值时只在值存在、能解析出路径、且该路径上的文件确实存在时才认为“已开启”，所以移动或删除程序之后，开关会自动显示为关闭状态。
- 通过设置对话框关闭开关会删除这个注册表值。
- 安装向导的 `autostart` 任务也操作同一个值（默认不勾选；若该值已经存在，向导会自动勾上以反映现状）。卸载时会删除这个值，同时清掉 Windows 启动管理器里的 `StartupApproved\Run` / `Run32` 状态：在任务管理器里禁用过“启动”项之后，那些状态会压过注册表值，只删值不删状态会让下一次安装仍旧显示为已禁用。

## 设计交付物的重新生成

`design/` 里的东西不需要构建，直接用浏览器打开 [design/index.html](../design/index.html) 就能操作原型。要重新生成截图（`design/shots/`）：

```powershell
python tools\shoot-design.py
```

它会用一个**独立的** Chrome profile（`.design/chrome-profile`）跑无头截图，**不会**碰你日常使用的浏览器配置——这是硬性要求：不加 `--user-data-dir` 启动 Chrome 会挂到你已经开着的实例上，之后任何清理都可能关掉你自己的窗口。

这个脚本还会拒绝保存被裁切的截图：`--window-size` 设的是视口，Chrome 不会按页面内容自动加高，窗口太矮会**静默**截掉底部。脚本先量页面高度，截完再检查最后一行是否是背景色，最后自动裁掉多余的空白。

`%TEMP%` 之外，项目里只有 `.design/` 是纯scratch 目录（Chrome profile，几 MB），删掉下次跑脚本会自动重建。

## 相关文档

- 产品定位、快捷键、隐私与已知限制：[README.md](../README.md)
- 自检覆盖范围与验收证据：[VERIFICATION-2026-09-27-reliability-menu.md](VERIFICATION-2026-09-27-reliability-menu.md)
- 视觉规格与原型：[design/DESIGN-SPEC.md](../design/DESIGN-SPEC.md)
- 原始规格：[MiniClip 极简文本剪贴板项目规划书.md](<../MiniClip 极简文本剪贴板项目规划书.md>)
