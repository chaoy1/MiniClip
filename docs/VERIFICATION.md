# 验证与证据

> 本文记录较早版本的验收快照，以下 30 条历史、8 行弹窗、鼠标定位、原生托盘菜单等数据保留作历史对照。2026-09-27 的当前实现已改为 100 条历史、5 行弹窗、文字光标定位与自定义圆角菜单；当前结果见 [VERIFICATION-2026-09-27.md](VERIFICATION-2026-09-27.md)。

本文档说明 MiniClip 的哪些部分是**被验证过的**、用什么方式验证的，以及哪些部分**没有被验证**、仍需人工验收。

自检代码在 [src/MiniClip/Diagnostics/SelfTest.cs](../src/MiniClip/Diagnostics/SelfTest.cs)，最近一次完整自检的原始输出在 [artifacts/selftest-release/selftest-console-verified.txt](../artifacts/selftest-release/selftest-console-verified.txt)，同目录下还有自检日志与候选框 PNG 快照。

## 如何复现

```powershell
$env:PATH = "D:\Software\dotnet-sdk;$env:PATH"          # 本机 .NET 10 SDK 不在默认 PATH 上
dotnet build src\MiniClip\MiniClip.csproj -c Release    # 期望 0 警告 0 错误
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --selftest artifacts\selftest-release
```

- **构建前必须按 PID 结束正在运行的 MiniClip**：`bin\Release` 下的 `MiniClip.exe` / `MiniClip.dll` 被占用时，构建会以 `MSB3061` / `MSB3026` 报错。结束命令形如 `Stop-Process -Id <PID> -Force`，不要按进程名结束。
- 输出目录可省略，省略时写 `%TEMP%\miniclip-selftest`。
- **进程退出码就是失败项数**，全部通过是 `0`。要在 CI 或脚本里判断结果，看退出码即可。这一条曾经是坏的，已修复，见下文“曾经报错的缺陷”第 2 条。
- 输出目录会得到 `selftest.log` 和**六张** PNG 快照（`popup-default`、`popup-multiline-selected`、`popup-long-line-selected`、`popup-whitespace-selected`、`popup-scrolled-30`、`popup-empty`）。
- 自检不会启动真正的 MiniClip：不注册全局快捷键、不创建托盘图标、不接管剪贴板，跑完直接退出。因此它可以在 MiniClip 正在运行时执行；这种情况下“热键注册”一项会报告 Alt + V 被占用，按预期通过。
- 参数 `--selftest` 与 `--self-test` 等价。
- **不要并行跑两个自检，也不要让自检和别的自动化脚本同时点这个 exe。** 本次核对期间就撞上过：同一台机器上另一个脚本按进程名清理 `MiniClip.exe` 并反复重跑，结果一次运行被从外部结束（退出码 `-1`、只落了 4 张快照），另有几次 `selftest.log` 在后半段被改写，同一次运行的 `navigation routing` 之后的记录跑进了 `%LOCALAPPDATA%\MiniClip\miniclip-<pid>.log`。`selftest.log` 用 `File.AppendAllText` 追加，不是原子独占，并发写会互相覆盖。**判读自检结果请以控制台输出为准，并保存一份**——本次运行保存在 [artifacts/selftest-release/selftest-console-verified.txt](../artifacts/selftest-release/selftest-console-verified.txt)，它是完整的 26 行 `[PASS]` 加 `ALL CHECKS PASSED`。

最近一次完整记录（[artifacts/selftest-release/selftest-console-verified.txt](../artifacts/selftest-release/selftest-console-verified.txt)）：**26 项检查全部通过**，退出码 `0`。运行环境为 os=10.0.26100.0、dpiAwareness=per-monitor、dotnet=10.0.1、Release 配置、work area 2520×1680（150% 缩放）。复现命令与读数：

```text
> .\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --selftest artifacts\selftest-release
...
ALL CHECKS PASSED
> $LASTEXITCODE
0
```

> 本次核对里 `artifacts\selftest-release\selftest.log` 出现过两种状态：有的运行是完整的（26 行 `=pass`、末行 `result=pass`），有的运行在第 35 行 `close requested reason=Escape` 处截止、缺 `result=pass`，缺的那部分出现在 `%LOCALAPPDATA%\MiniClip\miniclip-<pid>.log` 里。原因是并发的另一次自检（或 `--run` 定时运行）改写了同一个文件（见上一条）。**结论：这份日志不是可靠的证据文件，控制台输出才是。** 仓库里较早的一份日志 [artifacts/selftest-debug/selftest.log](../artifacts/selftest-debug/selftest.log) 同样在第 35 行截断，而且同目录缺 `popup-scrolled-30.png`，它只能当历史记录看。

## 检查项对照表

每一项都打印一行 `[PASS]` / `[FAIL]` 加一段可复核的实测值，共 **26 项**；“实测值示例”一列直接摘自上面那份控制台记录。**加粗的五项是本次核对新增或改写过的检查**（原先只有 21 项，没有钩子调用、钩子路由、导航路由和长列表滚动，空状态那一项也不检查尺寸）。

| 检查项 | 证明什么 | 怎么测的 | 实测值示例 |
| --- | --- | --- | --- |
| `message window` | 能注册窗口类并创建一个 message-only 窗口（`HWND_MESSAGE`），也就是快捷键、剪贴板通知和托盘回调的接收地址 | 创建 `NativeMessageWindow` 并读回句柄；失败时把 `hInstance`、`WNDCLASSEXW` 结构体大小和类名长度一起打出来，因为 `CreateWindowExW` 失败时最常抱怨的就是这几个值 | `handle=0x6E0D7A lastError=none` |
| `hotkey register` | 默认热键（Alt + V）能被 `RegisterHotKey` 接受，或明确报告已被占用 | 用真实修饰键加 `MOD_NOREPEAT` 调 `Win32Hotkey.TryRegister`，随后立即注销。注册成功、或失败原因是 1409，都算通过——正在运行的 MiniClip 实例持有这个组合是预期情形 | `Alt+V registered and released`；MiniClip 正在运行时为 `Alt+V unavailable (快捷键已被其他程序占用) — expected if MiniClip is running` |
| `hotkey spare` | 注册链路本身没有被“已被占用”这一种情况掩盖：换一个几乎不可能冲突的组合仍然能注册成功 | 注册 `Ctrl+Alt+Shift+F24` 后注销 | `Ctrl+Alt+Shift+F24 registered and released` |
| `hotkey conflict` | 冲突检测路径真的会触发：同一个组合重复注册必须被 Windows 拒绝，且错误码必须是 `ERROR_HOTKEY_ALREADY_REGISTERED`（1409） | 第一次注册后，用另一个 id 再注册同一个组合，检查返回失败且错误码等于 1409 | `duplicate registration refused with 1409` |
| **`keyboard hook invoked`** | **低级键盘钩子不只是 `Install()` 返回了句柄，而是 Windows 真的回调了它**——注册成功但回调从不发生，会让 Clipboard Mode 看起来装好了却吞掉所有按键，且不报任何错 | 装 `KeyboardHook`，用 `SendInput` 注入一次真实按键，读 `HookTrace.Callbacks`。注入事件带 `LLKHF_INJECTED`，按设计会被放行，所以这一项只断言“回调发生了”，不断言路由 | `SendInput delivered=True callbacks=2 callbacks=2 lastMsg=0x0101 lastVk=0xFFFFFFFF` |
| **`keyboard hook routing`** | **↑ ↓ Enter Esc 的路由契约成立，且注入事件确实被放行（不会被 MiniClip 自己的合成 Ctrl+V 再次消费）** | 不走注入，直接调用钩子用的同一个入口（`KeyboardHook.KeyHandler`），断言注入事件没有被 `observed` 收集到 | `injected events are passed through by design; the handler contract is the same entry point the hook calls` |
| `clipboard round trip` | 写入系统剪贴板再读回来，内容**逐字符相同**，并且系统剪贴板序号确实递增 | 写入一段包含 CRLF、行首缩进、制表符、双引号、中文、emoji 和行尾空格的负载，用序数比较读回值；写入前后各读一次 `GetClipboardSequenceNumber()` | `wrote=True exact=True sequenceAdvanced=True` |
| `clipboard self-marker` | MiniClip 自己的写入可以被识别，不会因为自己写剪贴板而污染历史（§17） | 写入后检查剪贴板上是否存在注册格式 `MiniClip.Text` | `MiniClip.Text present, self-writes are identifiable` |
| `history dedup` | 与顶部相同的文本不新增记录；重复较早的记录只做置顶，不产生第二条 | 容量设为 3 的 `HistoryManager`，依次加入 alpha/beta/gamma，再重复加入 gamma、重复加入 alpha，检查返回的 `HistoryChangeReason` | `duplicatesIgnored=1 promotions=1` |
| `history promote-to-top` | 置顶之后列表顶部确实是那条旧记录，且总数没有变多 | 置顶 alpha 后读 `At(0)` 与 `Count` | `count=3` |
| `history capacity` | 超过上限时淘汰最旧一条，总数收敛到上限 | 容量 3 的实例再加第 4 条不同文本，检查 `Count` 与 `Evicted` | `count=3 evicted=True` |
| `history filters` | 空字符串和超过单条上限的文本被忽略，且忽略原因是可区分的（§10.1 的 100,000 字符上限） | 分别加入 `""` 和 `MaxEntryLength + 1` 长度的字符串，检查原因枚举 | `empty and over-limit clips ignored` |
| `storage round trip` | JSON 原子保存 → 读取 → 逐字符还原，路径完整走通 | 保存 3 条（含前后空格、CRLF、中文、emoji），再读回并做序数比较 | `save=Saved load=Loaded count=3 exact=True` |
| `storage format` | 磁盘格式是文档承诺的**纯字符串数组**，没有漂移成带属性的对象 | 读原始文件文本，检查以 `[` 开头且不含 `"Entries"` | `plain JSON array of strings` |
| `storage coalescing` | 内容没变时不重复写盘（避免无意义的磁盘 I/O） | 用同一份负载再保存一次，检查结果是 `Skipped` | `repeat save=Skipped` |
| `storage corrupt file` | 损坏文件不会被静默覆盖：原文件被改名保留，调用方拿到 `Corrupt`，并以空历史继续 | 写入一段非法 JSON，加载后检查结果枚举、目标列表为空、且目录里出现了 `corrupt.corrupt-*.json` | `outcome=Corrupt quarantined=True` |
| `storage delete` | 清空历史时磁盘文件真的被删掉（§12、§27 要求内存与磁盘一起清） | 调用 `DeleteAsync` 后检查 `File.Exists` | `outcome=Saved exists=False` |
| `tray icon` | 托盘图标真的注册进了 shell，并且能改提示文本 | `Shell_NotifyIcon` 创建图标并设置 tooltip | `created=True tooltip=True` |
| `tray menu` | 托盘菜单的装配结果包含必需的命令项，且数量符合预期 | 调用 `TrayMenuBuilder.Build` 检查行数、退出项、清空历史项、更换快捷键项。菜单只构建不弹出——`TrackPopupMenuEx` 是模态的，自检不能等着人点 | `rows=9` |
| **`popup window styles`** | **候选框真的带着 `WS_EX_NOACTIVATE`、`WS_EX_TOOLWINDOW`、`WS_EX_TOPMOST` 三个扩展样式存在于 Windows 中**——这是整个产品的核心承诺，也是唯一无法靠读代码确认的事情 | 创建 `PopupWindow`、`ShowAt(...)`、`UpdateLayout()`，再取 `WindowInteropHelper` 的 HWND，用 `GetWindowLongW(GWL_EXSTYLE)` 把系统真实生效的值读回来，逐位检查 | `exStyle=0x08080088 NOACTIVATE=True TOOLWINDOW=True TOPMOST=True` |
| `popup placement` | 候选框显示在屏幕上、且完整落在它所在显示器的可用区域内（任务栏之外），并且是贴着鼠标弹的而不是居中 | 读出窗口矩形，用矩形左上角反查该显示器的 work area，要求矩形完全被包含且宽高为正。锚点是自检运行时的真实鼠标位置 | `rect=(1878,14,2478,566) work=(0,0,2520,1680)`（另有几次为 `rect=(312,314,912,866)`，取决于自检运行时鼠标在哪） |
| `popup edge placement` | 屏幕四角与中心的定位算法都不会把候选框推出可用区域；贴边时是向另一侧翻转而不是简单滑动 | 对当前 work area 的左上、右上、左下、右下、中心五个锚点分别跑 `PositionWithinWorkArea`，再单独验证右边缘翻转与左边缘翻转。尺寸按开发机 150% 缩放下的实际像素取 600×552 | `5 anchors inside work area (2520x1680); flips at right and left edges` |
| **`navigation routing`** | **钩子拿到的 ↑ ↓ Enter Esc 真的走到了候选框：顶部/底部夹紧不回绕、Enter 恰好触发一次粘贴请求并带对文本、Esc 与 Enter 都能把窗口关掉** | 直接驱动 `PopupWindow.HandleNavigationKey`（钩子调用的正是这个入口），用 `PumpDispatcher(400ms)` 让异步的关闭流程跑完再断言 `Closed1` 的原因 | `open=True clampTop=True down=True clampBottom=True enterPastes=True enterCloses=True escCloses=True` |
| **`long list scrolling`** | **30 条历史全都能浏览到，不只是前 8 条**：列表真的溢出、滚动真的有偏移、被选中的第 21 行真的落进视口 | 用 `HistoryManager.DefaultCapacity`（30）条记录 `ShowAt`，按 ↓ 走 20 次，读 `ScrollViewer.ScrollableHeight` / `VerticalOffset`，再用 `TransformToAncestor` 把选中容器的上下沿换算到视口坐标比较 | `30 entries, windowHeight=368 scrollable=881 offset=511 selected=20 visible=True` |
| `popup empty state` | 历史为空时候选框没有选中项、没有可粘贴内容，`Enter` 不可能误粘贴（§9.2）；**并且面板按两行文案定尺寸，不是按一行列表** | 用空列表 `ShowAt`，检查 `SelectedIndex == -1` 且 `SelectedText is null`；同一次还落盘 `popup-empty.png` 供逐像素复核（见下文“已修正的核对结论”第三节） | `no selection, nothing to paste`（快照 800×216 px = 400×108 DIP） |
| `memory footprint` | 把**自检进程**实际占用读出来，作为 §26 内存目标的对照。注意这不是常驻托盘时的读数 | `Process.WorkingSet64` 与 `Process.PrivateMemorySize64` | `workingSet=159MB private=102MB` |

关于扩展样式那一项的补充：`exStyle=0x08080088` 拆开是 `WS_EX_TOPMOST (0x00000008)` + `WS_EX_TOOLWINDOW (0x00000080)` + `WS_EX_NOACTIVATE (0x08000000)`。也就是说，Windows 侧确实把这个窗口标记为“可以显示但永不激活”，而它同时不在 Alt+Tab 列表里（TOOLWINDOW）、并且置顶。证据位置：控制台记录 [artifacts/selftest-release/selftest-console-verified.txt](../artifacts/selftest-release/selftest-console-verified.txt) 里的 `popup window styles` 一行；`selftest.log` 里的同一条记录在 [artifacts/selftest-release/selftest.log](../artifacts/selftest-release/selftest.log)，但那份日志可能被并发运行改写，原因见上文“如何复现”。

## 自检没有覆盖什么

这一节比上面那张表更重要。

**自检是进程内的、无头的行为探针，它不模拟用户。** 它没有键盘、没有第二个应用、没有第二个显示器。因此下列内容**没有被证明**：

| 未覆盖项 | 为什么自检证明不了 |
| --- | --- |
| 焦点真的留在原应用里 | 自检只证明了候选框窗口带有 `WS_EX_NOACTIVATE`。样式是必要条件，不是行为本身。“Chrome 地址栏是否仍然保持焦点”必须在真实应用里用真人操作确认。 |
| 注入的 Ctrl + V 真的落到了目标控件 | 自检根本没有调用 `PasteController.PasteAsync`，也没有发送过一次面向目标窗口的 `SendInput`。`keyboard hook invoked` 里那一次 `SendInput` 是往当前线程注入的探针按键，用来证明钩子会被回调，它不带任何粘贴语义。剪贴板往返只验证了“文本进得去、出得来、一字不差”。 |
| 只粘贴一次 | 同上，没有任何一次真实粘贴发生过。 |
| `↑ ↓` 不会移动原文本框里的光标 | **这一条现在只被证明了一半。** `keyboard hook routing` 与 `navigation routing` 证明了：注入事件会被放行、非导航键会被放行、四个导航键的按键会走到 `HandleNavigationKey`。但“真实的 ↑ ↓ 在候选框打开时被吞掉、没有到达原文本框”仍然没有在真实宿主应用里验过——自检里由 `KeyHandler` 直接返回 `Consume` 只是模拟了那个契约，被吞掉之后的实际效果没人观察过。 |
| 关闭候选框后键盘行为恢复 | 同上。 |
| 目标窗口变化时拒绝粘贴（§25 场景六） | `PasteTarget.StillValid()` 的目标核对逻辑没有任何自动化测试覆盖。 |
| 多显示器、跨 DPI 的真实显示效果 | 边缘定位算法是在**当前这一块**显示器的 work area 上算的。日志里的 `work=(0,0,2520,1680)` 是一块 150% 缩放屏的可用区域。把候选框从 150% 屏拖到 100% 屏上是否仍然像素正确，自检无法回答。 |
| 托盘图标的实际外观、tooltip 的截断效果 | 只验证了 shell 接受注册。 |
| 快捷键设置对话框的全部交互 | 对话框没有出现在任何一项自检里。 |
| 空状态文案的渲染完整性 | **这一条已经补上了。** `popup-empty.png` 现在是 800×216 px（400×108 DIP），两行文案都在图里，见下文“已修正的核对结论”第三节。剩下的缺口只是“没有断言两行文字没有被 `TextTrimming` 截断”，目前靠看图确认。 |
| 启动时间与候选框首帧延迟 | 自检没有计时，见下文“性能目标怎么测”。 |

### 仍需人工验收的清单（§25）

规划书第二十五节的八个场景，逐条标注当前状态：

| 场景 | 内容 | 自检是否覆盖 | 状态 |
| --- | --- | --- | --- |
| 场景一 | 在 Chrome 地址栏按 Alt + V，MiniClip 出现且地址栏仍保持焦点 | 部分（只覆盖扩展样式） | **待人工验收** |
| 场景二 | 候选框打开后 ↑ ↓ 只控制 MiniClip，不移动文本框光标 | 部分（`navigation routing` 证明了按键会走到 `HandleNavigationKey`；**“原文本框没有收到这个按键”没有证明**） | **待人工验收** |
| 场景三 | Enter 后选中的历史文本出现在当前输入框 | 部分（`navigation routing` 证明了 Enter 会触发恰好一次带对文本的粘贴请求；真正的注入与落点没有） | **待人工验收** |
| 场景四 | Esc 后候选框消失，当前应用仍能正常输入 | 部分（`navigation routing` 证明了 Esc 能关掉窗口并给出 `Escape` 原因；恢复输入没有） | **部分待人工验收** |
| 场景五 | 候选框未打开时 ↑ ↓ Enter Esc 完全不受 MiniClip 影响 | 否 | **待人工验收** |
| 场景六 | 打开候选框后切窗口再 Enter：不向新窗口粘贴；快捷键被占用时明确提示注册失败 | 部分（快捷键冲突路径有自检；切窗口后的行为没有） | **待人工验收** |
| 场景七 | 空文本 / 非文本 / 超长内容不改变历史；重复与置顶行为；重启后历史保留；清空后重启为空；日志不含原文 | 大部分（去重、置顶、上限、过滤、保存、读取、删除、损坏隔离都有自检；**“重启后保留”这一条跨越进程重启，自检没有覆盖**） | **部分待人工验收** |
| 场景八 | Chrome 地址栏、记事本、VS Code 编辑区分别验证焦点与单次粘贴；边缘、多显示器、不同缩放比例下候选框完整可见；对不支持的控件记录结果 | 部分（边缘与翻转有自检；“三种宿主应用”和“多显示器”没有） | **待人工验收** |

建议的人工验收动作，按场景逐条执行并记录结果：

1. 在 Chrome 地址栏输入几个字符，按 Alt + V：候选框出现在鼠标附近，地址栏里的光标仍在闪动；按 ↓ 两次，地址栏内容不变；按 Esc，候选框消失，继续打字正常。
2. 在同一地址栏按 Alt + V 后按 Enter：地址栏里只出现一次所选文本（不是两次），系统剪贴板里是同一段文本。
3. 换个宿主应用重复第 1、2 步：记事本、VS Code 编辑区各做一遍。对不支持的控件（自绘控件、终端模拟器、游戏）记录结果，不要用“任何文本框均可用”作为验收口径。
4. 候选框打开后点一下别的窗口再按 Enter：不应该有任何文本被粘进新窗口，候选框关闭，↑ ↓ 恢复正常。
5. 把鼠标移到屏幕右下角、右上角、左下角、左上角，各按一次 Alt + V：候选框每次都完整可见，贴边时向另一侧翻转，不越过任务栏。
6. 如果有第二块显示器：把鼠标移到第二块屏（尤其是与主屏缩放比例不同的那块）上呼出候选框，确认它出现在鼠标所在的那块屏，且大小与清晰度正常。
7. 复制一段超过 100,000 字符的文本、复制一张图片、复制一个文件：历史都不应该变化。复制一段与顶部相同的文本：条数不变。复制一条较早的文本：它移到顶部而不是新增。
8. 新增几条之后用任务管理器强制结束 MiniClip 进程再启动：历史应该还在。点“清空历史”后重启：应该为空。打开 `%LOCALAPPDATA%\MiniClip\miniclip-<pid>.log` 检查全文不含任何剪贴板原文。
9. 验证快捷键冲突提示：让另一个程序占住 MiniClip 当前使用的组合（例如先用 MiniClip 自己注册 Alt + V，再从另一个进程注册同一组合），确认托盘状态行与气球提示都明确说明注册失败，而不是假装可用。也可以直接跑一次 `MiniClip.exe --selftest`：控制台里 `hotkey register` 一项会显示“已被其他程序占用”，这一行就是冲突路径真实触发的证据。
10. 从托盘退出，确认进程真的结束（任务管理器里没有残留），且键盘行为完全正常。
11. **新增（针对本轮修好的两处）：** 连按两次 Alt + V，或者按 Alt + V 之后立刻按 Esc，**在候选框还没有画出来之前**就结束它——窗口必须消失，不能留在屏幕上。这是 `Close1` 以前那个动画完成事件 bug 的现场条件，现在 `DispatcherTimer` 是兜底（见下文“曾经报错的缺陷”第 1 条）。
12. **新增：** 攒够 9 条以上历史后呼出候选框，连续按 ↓ 走到底：高亮行必须始终在可视区域内，列表要有细滚动指示，Enter 粘出来的必须是屏幕上高亮的那一条。
13. **新增：** 复制一段含双引号和中文的代码（例如 `he said "hi" & <b> and 中文 ok`），打开 `%LOCALAPPDATA%\MiniClip\history.json`：应当能直接读懂，不应出现 `\u0022` / `\u0026` 这类转义（emoji 仍会写成 `\uD83D\uDE42` 代理对，那是正常的）。

## 已修正的核对结论

下面三条是**早先一轮核对里的错误结论**，本轮逐条实测后推翻或改写。保留原文是为了让读者看清改的是什么、凭什么改。

### 一、候选框 PNG 快照没有裁掉底部——原来的推论错了

**原来的说法（错误）：** 自检给候选框喂了 10 条样例记录，对应高度是 448 DIP；而落盘的 PNG 只有 800×736 像素，按 `scale = 2.0` 折算只覆盖 400×368 DIP，正好少掉最后两条记录和整个底部提示栏。并据此断言“这些快照不能用来证明底部提示栏的渲染结果”。

**错在哪里：** 推理里有一个没有验证的隐含前提——**“10 条 fixture 就意味着 10 行可见”**。实际上 `PopupWindow.MaxVisibleRows = 8`（[src/MiniClip/UI/PopupWindow.xaml.cs](../src/MiniClip/UI/PopupWindow.xaml.cs) 第 30 行）把可见行数封顶在 8，`UpdateWindowHeight()` 算的是 `Math.Min(_rows.Count, MaxVisibleRows)`。所以 368 DIP 不是“被裁掉的高度”，而是**面板的真实高度**。

**按当前常量重新推导（`RowHeight = 40`、`ListPadding = 10`、`FooterHeight = 26`）：**

```text
10 DIP   列表上下内边距（ItemsControl Padding="0,10,0,10"，上下各 10）
320 DIP  8 行 × 40 DIP（MaxVisibleRows 封顶，不是 10 行）
 26 DIP  底栏（Border MinHeight="26"）
  2 DIP  Shell 上下各 1 DIP 边框
───────
368 DIP  窗口高 → 在 scale = 2.0 的快照里正好 736 px，宽度 400 DIP → 800 px
```

**实测复核（本轮重新生成并逐像素扫描）：**

```powershell
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --selftest artifacts\selftest-release
# popup-default.png 800×736 px；扫描 x=24..776 的亮行
```

`popup-default.png` 的文本带落在 y≈57–76 / 136–160 / 216–240 / 296–314 / 375–400 / 435–471（含折行的双行记录）/ 479–498 / 535–560，行距 80 px = 40 DIP，**共 8 行**；底栏分隔线与图例落在 **y≈698–720 px**，就是 `↑↓ 选择 / Enter 粘贴 / Esc 关闭`。看图更直接：[artifacts/selftest-release/popup-default.png](../artifacts/selftest-release/popup-default.png)。

**结论更正：底部提示栏从一开始就在快照里，快照是完整的。** 第 9、10 条记录不在图里也不是裁剪，而是它们本来就没被显示——这正是 `long list scrolling` 那一项要解决的问题（见下一条）。原先那句“评审快照时不要把它当成完整面板”**作废**。

### 二、列表现在会滚动，第 9 条以后不再“只能靠按键盲选”

`UI/PopupWindow.xaml` 第 193 行有 `x:Name="ListScroller"` 的 `ScrollViewer`（`VerticalScrollBarVisibility="Auto"`）包着 `ItemsControl`；`PopupWindow.UpdateRailPosition()` 在每次选中变化时对选中容器调 `BringIntoView()`。自检里 `long list scrolling` 一项的实测值：

```text
30 entries, windowHeight=368 scrollable=881 offset=511 selected=20 visible=True
```

拆开读：30 条记录 → 面板高度仍是 368 DIP（8 行封顶）→ 内容可滚动高度 881 DIP → 按 ↓ 20 次后 `VerticalOffset=511` → 选中的是第 21 条（`selected=20`）→ 该行确实在视口内（`visible=True`）。快照 [popup-scrolled-30.png](../artifacts/selftest-release/popup-scrolled-30.png) 可以直接看到：顶部 `entry 12` 被切掉一半（说明真的滚了），中间 13–19，高亮行 `entry 20` 带轨道与箭头，底部图例完整。

**所以 README 里“候选框列表不滚动 / 第 9 条以后只能靠按键移动选中项后 Enter”这条限制已经不存在。** 需要注意的只是滚动条被刻意做成 2 DIP 宽的细指示条（`App.xaml` 里的 `HairlineScrollBar`，不是可拖拽的常规滚动条），溢出时用来“确认还有内容”，列表仍然由 ↑ ↓ 驱动。

### 三、空状态的两行文案现在完整渲染

`UpdateWindowHeight()` 对 `_rows.Count == 0` 单独处理：

```csharp
Height = 70 + ListPadding + FooterHeight + 2;   // 70 DIP 是两行文案的内容高度
```

70 + 10 + 26 + 2 = **108 DIP**。实测 `popup-empty.png` = 800×216 px = 400×108 DIP，与推导一致。逐像素扫描该图，两条文本带在 **y≈41–65 px（12.5 DIP，第一行）** 与 **y≈79–101 px（11.5 DIP，第二行）**，两行都完整，没有任何一行被切掉一半。原先记录的“第二行只渲染出约 11 个设备像素高”已不成立。

### 小结：这三条更正对文档的影响

- 原“已知的证据缺口”第 1 条（快照被裁）**整条作废**，并保留下面的错误推理过程。
- 原“已知的证据缺口”第 2 条（空状态第二行被裁）**已修复并复测通过**。
- “自检没有覆盖什么”表里“空状态文案的渲染完整性”一行的措辞已相应放宽。

## 曾经报错的缺陷

这一节记录**已经确认被修好**的缺陷，以及证明它们被修好的那条命令或读数。留着是为了让下一个改到这些代码的人知道那里原来是什么样。

### 1. `PopupWindow.Close1` 依赖动画完成事件，窗口可能永远不消失（最严重的一个）

**故障形状：** 旧实现里 `Close1` 起一个 70 ms 的 `DoubleAnimation`，只靠 `Completed` 事件去调 `FinishClose` 真正隐藏窗口。动画的 `Completed` 只有在**时钟推进过、并且元素已经参与过合成**之后才会触发。于是一个“还没渲染过就要求关闭”的候选框会：把 `_isClosing` 置上 → 动画永远不完成 → `FinishClose` 永不执行 → **窗口一直挂在屏幕上**。

**触发条件并不罕见：** 连按两次 Alt + V，或者在候选框首帧画出来之前就按 Esc；自检里出现的正是这个状态——日志里那句 `close requested reason=Shutdown open=True closing=False` 后面原本等不到 `finishClose`。

**修法：** `Close1` 现在同时起一个 140 ms 的 `DispatcherTimer` 兜底，谁先到谁调 `FinishClose`（`DispatcherTimer.Tick` 与 `fade.Completed` 都先 `closeGuard.Stop()`，所以不会双跑）。代码注释把原因写在了现场：

> Two independent guarantees that the popup actually goes away, because relying on the animation alone is a real bug: an animation's Completed event only fires once the clock has advanced and the element has been composed, so a popup that closes before it has ever rendered … would set `_isClosing`, never animate, never complete, and stay on screen forever. The timer is the backstop.

**怎么证明它被修好了：** `navigation routing` 一项里，Esc 与 Enter 两条路径都先 `PumpDispatcher(TimeSpan.FromMilliseconds(400))` 再断言 `Closed1` 带回的原因：

```text
open=True clampTop=True down=True clampBottom=True enterPastes=True enterCloses=True escCloses=True
```

`enterCloses=True` / `escCloses=True` 只有在 `FinishClose` 真的跑完、`Closed1` 真的触发时才会为真。

**为什么这个检查必须写在应用进程内部：** MiniClip 的低级键盘钩子**故意忽略注入事件**（`KeyboardHook.HookCallback` 里 `LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED` 直接 `CallNextHookEx` 放行）。这不是疏漏，是必需——MiniClip 自己合成的 Ctrl + V 也要从这条钩子上过一遍，如果吞掉注入事件，粘贴就永远发不出去。代价是：**任何外部测试工具能造出来的按键都是注入事件**，它们能证明“钩子被调用了”（`keyboard hook invoked` 就是干这个的），但**永远无法证明钩子把按键路由到了哪里**。所以路由只能在进程内、直接驱动 `PopupWindow.HandleNavigationKey` 来验证——这也正是 `navigation routing` 存在的唯一理由。

### 2. 自检退出码被污染：全过也会以 `0xE0434352` 退出

**故障形状：** `App.Teardown()` 从 `ProcessExit` 处理器里调 `Mutex.ReleaseMutex()`，而 `ProcessExit` 跑在**另一个线程**上，不是当初获取互斥体的那个线程。CLR 于是抛：

```text
ApplicationException: Object synchronization method was called from an unsynchronized block of code
```

进程最终以 `0xE0434352`（CLR 未处理异常）退出——**即使检查全过也一样**。文档承诺的“退出码 = 失败项数”因此被静默摧毁，任何靠退出码判断 CI 结果的地方都会误报失败。

**修法：** 释放前先在 `_ownsMutex` 上判断所有权，并额外 `catch (ApplicationException)` 兜住“不是拥有线程”的情况：

> ReleaseMutex throws ApplicationException unless the calling thread owns the mutex, and ProcessExit does not run on the thread that took it. Guarding on ownership is what makes teardown safe to call from both the tray's exit path (correct thread) and the process-exit hook (wrong thread). Without this the process ends with an unhandled exception, which silently destroys the documented "--selftest exit code = failure count" contract.

**怎么证明它被修好了：** 本轮多次干净运行的退出码都是 `0`：

```text
> cmd /c ".\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --selftest artifacts\selftest-release ..."
ALL CHECKS PASSED
> $LASTEXITCODE
0
```

另外查 Windows 事件日志可以直接看到这个缺陷的痕迹与修复分界。`Application Error` / `.NET Runtime` 里 `MiniClip.exe` 带着 `Exception code: 0xe0434352` 的崩溃记录集中在 **22:26–22:39**，也就是 `App.xaml.cs` 里加所有权判断（文件修改时间 22:43:45）**之前**；22:43:45 之后的干净自检运行（22:45:12、22:47:08、22:49:02）都退出码 `0`，并且**没有**新增对应的崩溃事件。复核命令：

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'; StartTime=(Get-Date).AddHours(-2)} |
  Where-Object { $_.Message -like '*MiniClip*' } |
  Select-Object TimeCreated, @{n='Msg';e={$_.Message}}
```

一次失败运行（被外部脚本按进程名结束的那次）给出的是 `-1` 并且输出不完整，那属于外部干扰，不是这个缺陷复发。

### 3. Release 构建解析不了候选框 XAML，Debug 却没事

**故障形状：** `ScrollViewer.Resources` 里写了 `<Style TargetType="ScrollBar" BasedOn="{StaticResource HairlineScrollBar}"/>`，而 `HairlineScrollBar` 定义在外层 `Window.Resources`。嵌套资源字典在**解析期**要解析 `BasedOn` 的 `StaticResource`，查找沿解析树走，看不到外层 `Window.Resources` 里的键。它**只在编译过的 Release XAML 构建里失败，Debug 容忍**——于是这个缺陷可以一路发布出去，只要两种配置不是都被跑过。

**修法：** `HairlineScrollBar` 现在定义在 `App.xaml` 的 `Application.Resources` 里（应用级作用域，从任何地方都可解析）。`App.xaml` 的注释把这条经验写下来了：

> a nested ResourceDictionary cannot reliably resolve `BasedOn="{StaticResource ...}"` against the enclosing Window.Resources: the lookup walks the parse-time tree and does not see it. This fails only in a compiled Release XAML build — Debug tolerates it — so it is the kind of defect that ships if the two configurations are never both exercised.

**怎么证明它被修好了：** Release 强制全量重建，0 警告 0 错误，并且生成出来的程序能真的创建候选框（`popup window styles` / `popup placement` 两项通过，`popup-default.png` 落盘）：

```powershell
$env:PATH = "D:\Software\dotnet-sdk;$env:PATH"
dotnet build src\MiniClip\MiniClip.csproj -c Release --no-incremental
# 已成功生成。 0 个警告 0 个错误
```

**记住这条：** WPF XAML 的资源查找失败**可能只在一种构建配置里出现**。改 `App.xaml` / `Window.Resources` / `ScrollViewer.Resources` 之后，Release 必须实际跑一遍，不能只看 Debug 通过。

### 4. JSON 序列化器把引号和中文写成转义序列

**故障形状：** `JsonStorage.SerializerOptions` 用的是 `JavaScriptEncoder.Create(UnicodeRanges.All)`。中文是保住了，但 `"` 和 `&` 仍然被写成 `\u0022` / `\u0026`——一段复制来的代码落盘就成了转义汤，而规划书 §12 明确要求这个文件是用户能打开、能直接编辑的普通 JSON 数组。

**修法：** 改成 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`（`src/MiniClip/Storage/JsonStorage.cs` 第 439 行）。

**为什么选它——三种编码器的实测对照**（同一段负载，用一个临时探针程序跑的）：

| 编码器 | 双引号 | `&` / `<` `>` | CJK | 结论 |
| --- | --- | --- | --- | --- |
| 不设 `Encoder`（默认） | `\u0022` | `\u0026` / `\u003C` `\u003E` | `\u4E2D\u6587` | 中文和引号都转义，不可读 |
| `Create(UnicodeRanges.All)` | `\u0022` | `\u0026` / `\u003C` `\u003E` | 中文 | 中文可读，引号与 `&` 仍是转义汤 |
| `UnsafeRelaxedJsonEscaping` | `"` | `&` / `<` `>` | 中文 | **只有这一种是人能读的** |

三种编码器的往返（`Deserialize` → 序数比较）**都是精确的**，所以这个选择只影响可读性，不影响正确性。“Unsafe”指的是放宽 HTML 敏感的转义——只有把 JSON 内嵌进标记语言时才有风险；这个文件写在用户自己的目录里、由同一个程序读回，不涉及这个场景。

**怎么证明它在修好之后仍然成立——一次真实的剪贴板往返：** 用 Python 通过 Win32 API 写系统剪贴板（PowerShell 传参会把引号弄坏），内容 `he said "hi" & <b> and 中文 ok`，等 MiniClip 落盘后直接读 `%LOCALAPPDATA%\MiniClip\history.json` 的原始字节：

```python
import win32clipboard, win32con
win32clipboard.OpenClipboard(); win32clipboard.EmptyClipboard()
win32clipboard.SetClipboardData(win32con.CF_UNICODETEXT, 'he said "hi" & <b> and 中文 ok')
win32clipboard.CloseClipboard()
```

```json
[
  "he said \"hi\" & <b> and 中文 ok",
  "line1\r\n  indented\ttab \"quoted\" 中文 \uD83D\uDE42 trailing  "
]
```

逐字节检查结果：

```text
contains \u0022 escape : False
contains \u0026 escape : False
contains literal 中文  : True
first 4 bytes          : 5B 0D 0A 20        （'[' CR LF ' '，无 BOM）
```

读回剪贴板内容与写入值 `exact=True`（Python 侧比较），文件开头没有 BOM。**要如实说明两点：**

1. 文件里那对**包住字符串的**引号仍然是 ASCII 的 `"`（`"he said ..."` 的首尾），只是字符串**内容里**的引号写作 `\"`——这是 JSON 语法要求的转义，不是 `\u0022`。所以“引号保持字面量”指的是不再出现 `\u0022` 这种 Unicode 转义。
2. emoji 仍然写成 UTF-16 代理对 `\uD83D\uDE42`。`UnsafeRelaxedJsonEscaping` 不保证非 BMP 字符字面输出，这一点与“文件可读”的承诺不冲突，但值得知道。

### 5. 其它仍然成立的历史结论（未变）

- `InvariantGlobalization` 必须保持 `false`（下一节）。
- `WNDCLASSEXW.lpszClassName` 必须用指针，不能用 `ByValTStr`（下一节）。
- `KeyboardHook` 用**钩子事件流**跟踪修饰键，而不是 `GetAsyncKeyState`：Alt + V 打开候选框时用户手还按着 Alt，`SendInput` 只是把事件排队，异步键态还没反映出来，轮询会把每次 `↑` 都看成 `Alt+↑`，整份列表静默失效。这条结论没有变。

## 三个不能回退的构建配置结论

这三条是实际复现出来的故障及其修法，不是风格偏好。改动它们会重新引入已经修好的 bug。

### 一、`InvariantGlobalization` 必须保持 `false`

`MiniClip.csproj` 里的注释原文：

> `InvariantGlobalization` must stay false. Setting it true (tempting for a small footprint) makes WPF's font cache throw `TypeInitializationException` from `MS.Internal.FontCache.MajorLanguages` the first time text is laid out, which breaks the popup entirely. CJK text also needs the real culture data.

也就是说：为了缩小体积把它打开是一个很自然的想法，但 WPF 的字体缓存会在**第一次排版文本时**抛 `TypeInitializationException`（类型初始值设定项抛出异常），候选框直接废掉——不是显示异常，是根本画不出来。另外中文内容需要真实的区域文化数据。项目文件里同时把 `<InvariantGlobalization>false</InvariantGlobalization>` 写死，生成的 `MiniClip.runtimeconfig.json` 里对应 `"System.Globalization.Invariant": false`，可以用发布产物复核这一点。

### 二、`WNDCLASSEXW.lpszClassName` 必须用指针，不能用 `ByValTStr`

`src/MiniClip/Interop/NativeMethods.cs` 里 `WNDCLASSEXW` 的注释原文：

> A raw `LPCWSTR`, not a marshalled string, and that is deliberate. Declaring this as `[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]` yields a struct of exactly the right size (584 bytes on x64) that `RegisterClassExW` nevertheless rejects with `ERROR_INVALID_PARAMETER`. Measured on this machine against three variants; only the pointer forms work.

`src/MiniClip/Interop/NativeWindow.cs` 里对应的注释补充了当时的排查方式：

> Declaring `lpszClassName` as `[MarshalAs(UnmanagedType.ByValTStr)]` produces a correctly-sized 584-byte `WNDCLASSEXW` that `RegisterClassExW` nevertheless rejects with `ERROR_INVALID_PARAMETER` (87) on x64 — verified with a standalone probe against three layout variants, where only the pointer forms registered. An explicit `HGLOBAL` keeps the marshalling entirely under our control.

要点：这不是“结构体大小算错了”。用 `ByValTStr` 声明的结构体在 x64 上正好是 584 字节，尺寸完全正确，但 `RegisterClassExW` 依然返回失败，`Marshal.GetLastWin32Error()` 是 `ERROR_INVALID_PARAMETER`（87）。在三种布局变体上做过对照，只有指针形式能注册成功。所以类名是用 `Marshal.StringToHGlobalUni` 分配后用 `IntPtr` 传进去、用完立刻 `FreeHGlobal`。

自检里专门留了给这条结论兜底的诊断：`message window` 一项失败时会打印 `hInstance`、`wndclassexSize` 和类名长度——这三个值就是 `CreateWindowExW` / `RegisterClassExW` 失败时最常被怀疑的参数。当前记录里这一项是通过的，所以日志中并没有打出实际字节数；结论文本完全来自上面两处注释所描述的复现过程（584 字节、错误码 87、三种变体对照）。

### 三、XAML 资源查找失败可能只在一种构建配置里出现

`HairlineScrollBar` 必须定义在 `App.xaml` 的 `Application.Resources` 里，不能挪回 `PopupWindow.xaml` 的 `Window.Resources`。嵌套资源字典（`ScrollViewer.Resources`）在解析期解析 `BasedOn="{StaticResource ...}"` 时看不到外层 `Window.Resources` 的键；它**只在编译过的 Release XAML 构建里失败，Debug 容忍**，所以是可以一路发布出去的缺陷。详见上一节第 3 条。

**操作含义：** 动过 `App.xaml` / 任何 `Window.Resources` / `ScrollViewer.Resources` 之后，Release 构建必须实际跑一遍，不能只看 Debug 通过。Release 自检里的 `popup window styles` 与 `popup-default.png` 就是这条配置的哨兵。

## 性能目标怎么测

规划书第二十六节列了五个目标。**内存这一项本轮有了实测数字，其余四项仍然只是目标**：

| 目标（§26） | 目标值 | 测量口径（§26 规定） | 仓库里现有的实测数据 |
| --- | --- | --- | --- |
| 后台内存 | 尽量 < 50 MB | 目标发布配置、含 30 条历史 | **常驻托盘、空闲、历史 2 条：工作集 ≈ 55.7 MB，私有 ≈ 13.9 MB**（`Get-Process`，三次独立运行一致）。§26 的 < 50 MB 是**目标值，不是实测值**；按工作集口径当前略高于目标（约 +5.7 MB，约 11%），按私有字节口径则远低于目标。另：自检自己的 `memory footprint` 读数是 `workingSet=159MB private=102MB`，那是**自检进程**的读数，不等于发布态。详见下面“内存的两个口径” |
| 启动时间 | < 1 秒 | 从进程创建到托盘图标可用，重复 30 次记中位数与最慢值 | 无 |
| 快捷键响应 | 接近即时 | — | 无 |
| 候选框显示 | < 100 ms | 从收到快捷键消息到窗口首帧可见，重复 30 次记中位数与最慢值 | 无 |
| 剪贴板写入 | 用户基本无感 | — | 无；代码里有一个确定的等待：粘贴前固定 `SettleMilliseconds = 55` 毫秒，等目标窗口处理完剪贴板更新再发 Ctrl + V |

### 内存的两个口径（别把它们混在一起）

| 测的是什么 | 怎么测的 | 工作集 | 私有字节 |
| --- | --- | --- | --- |
| **常驻托盘的 MiniClip，空闲** | 结束所有实例 → 启动一个 → 等 30 s → `Get-Process -Id <PID>` 读 `WorkingSet64` / `PrivateMemorySize64` | **≈ 55.7 MB**（三次独立运行：55.6 / 55.7 / 55.7） | **≈ 13.9 MB**（13.9 / 13.9 / 14.3） |
| **自检进程**（`memory footprint` 那一行） | 同一台机器上跑 `--selftest`，由 `DiagnosticsLog.ReadMemory()` 读自己 | 158–159 MB | 101–102 MB |

**两个数字差了一个数量级，差在“谁在读”。** 自检进程在读数之前已经在同一个进程里做过这些事：

- 初始化了 WPF（`App.InitializeComponent()` + 多个 `Window`）：字体缓存、渲染管线、`PresentationFramework` 全部加载并预分配；
- 创建过**至少 8 个候选框窗口**（默认那一组 + `navigation routing` + `long list scrolling` + `popup empty state`），每个都带 `DropShadowEffect`；
- 注册过托盘图标、装过两次全局键盘钩子、往系统剪贴板写过多次；
- 全程没有强制 GC，`RenderTargetBitmap` 产出的 6 张 800×736 位图直接在托管堆上分配。

所以那一行是**诊断工具的读数，不是产品占用**。要论证 §26 的内存目标，只能引用上表第一行。

**实测命令（可直接复制）：**

```powershell
Get-Process -Name MiniClip -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }   # 只按 PID
Start-Sleep -Seconds 3
$p = Start-Process '.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe' -PassThru
Start-Sleep -Seconds 30
Get-Process -Id $p.Id | Select-Object Id, WorkingSet64, PrivateMemorySize64
```

本机读数序列（30 s 那一次即上表数字）：

```text
t=  5s  WS=55.74 MB  Private=14.09 MB  Threads=13  Handles=305
t= 10s  WS=55.74 MB  Private=14.09 MB  Threads=13  Handles=305
t= 20s  WS=55.74 MB  Private=14.09 MB  Threads=13  Handles=305
t= 30s  WS=55.66 MB  Private=13.90 MB  Threads=9   Handles=303
```

**口径提醒（诚实说明）：** §26 规定的测量环境是“目标发布配置的普通 Windows 电脑上，含 30 条历史”。本机这次是 Release 调试布局（不是单文件发布产物）+ 历史文件里只有 2 条记录 + 从没打开过候选框。历史条数对读数的影响没有单独测过；工作集里有相当一部分是 WPF 的共享/可回收页，不是 MiniClip 独占的。要想把这条目标判为达标或不达标，应当按 §26 的口径补一次带 30 条历史的测量，并把工作集与私有字节分开记录。

源码里**没有**任何计时埋点或基准测试代码（检索 `Stopwatch` / `ElapsedMilliseconds` 等无结果），`DiagnosticsLog` 只有 `ReadMemory()` 一个读数函数。所以其余四个目标目前都无法从仓库直接得到数字，测量需要外部手段：

- **启动时间**：从进程创建到托盘图标可用的时间。可用 PowerShell 记时：启动进程后轮询通知区域图标出现并不容易，实用做法是启动进程并记录时间戳，然后看同一次启动写入 `%LOCALAPPDATA%\MiniClip\miniclip-<pid>.log` 的时间差——日志的第一行带毫秒时间戳。重复 30 次，记录中位数和最慢值。
- **候选框首帧延迟**：从 `WM_HOTKEY` 到窗口首帧可见。这需要在 `MiniClipController.OnWindowMessage`（收到 `WM_HOTKEY` 的位置）到 `PopupWindow.ShowAt` 之后加临时计时点，或用一个外部工具记录按键时间与截屏中面板出现的时间差。当前没有现成测量。
- **内存**：按上面“内存的两个口径”的命令做，**不要**引用自检的 `memory footprint` 行当作产品占用。

按 §26 的要求，如果测出来不达标，应当先记录测试环境和瓶颈，再决定是否调整目标，而不是改用主观感受代替数字。

## 已知的证据缺口

以下两点是**早先那一轮**核对时记录的问题。第 1 条已被本轮实测推翻（错误在我，不在代码），第 2 条对应的缺陷已被修复并复测。**原文保留**，改动与证据见“已修正的核对结论”。

1. ~~**候选框 PNG 快照没有覆盖整个面板，底部被裁掉了。**~~ **——这条结论是错的，不要采信。** 原文：自检给候选框喂了 10 条样例记录，对应的高度是 448 DIP；而落盘的 PNG 只有 800×736 像素——按自检里 `scale = 2.0` 折算，快照只覆盖 400×368 DIP，正好少掉最后两条记录和整个底部提示栏（`↑↓ 选择` / `Enter 粘贴` / `Esc 关闭`）。逐像素扫描 `popup-default.png` 可以复核：面板底色一直延伸到图像最底边，最后一条可见文本是第 9 条 `kubectl`，图像里既没有那条分隔细线，也没有任何提示文字。因此**这些快照不能用来证明底部提示栏的渲染结果**，评审快照时也不要把它当成完整面板。默认那份记录里 `popup-long-line-selected.png`、`popup-multiline-selected.png`、`popup-whitespace-selected.png` 三张的选中项（第 6、7、8 条）仍然落在可见范围内，所以它们是有效的；只有第 9、10 条和底部提示栏没有出现在任何一张快照里。

   **错因：** 把一个未经验证的前提当成了事实——以为 10 条 fixture 就意味着 10 行可见。`PopupWindow.MaxVisibleRows = 8` 早就把可见行数封了顶，368 DIP 就是面板的真实高度，底栏一直在图里（像素扫描在 y≈698–720 px 找到图例文字）。重新推导的几何与实测复核见“已修正的核对结论”第一节。

2. ~~**空状态的第二行文案被裁掉。**~~ **——已修复。** 原文：`popup-empty.png` 里第二行“复制一段文本后，再次打开即可选择”只渲染出约 11 个设备像素高（应为约 40 像素）。原因是面板高度按一行列表算：列表区 = `1 × 40 + 10 × 2` = 60 DIP，加底部 26 DIP 与上下边框 2 DIP 共 88 DIP，减去边框后留给内容 68 DIP；而空状态 `StackPanel` 的内容高度是上下边距 32 DIP + 第一行 20 DIP + 第二行 18 DIP = 70 DIP，超出约 2 DIP。自检的 `popup empty state` 只断言了“没有选中项、没有可粘贴内容”，不会发现这个渲染问题。

   **现状：** `UpdateWindowHeight()` 对空列表单独按两行文案定尺寸（70 DIP 内容 + 10 列表内边距 + 26 底栏 + 2 边框 = 108 DIP）。实测 `popup-empty.png` = 800×216 px = 400×108 DIP，两行文本带分别落在 y≈41–65 px 与 y≈79–101 px，都完整。复核见“已修正的核对结论”第三节。

## 与规划书的分歧清单

这几条是拿当前实现和 [MiniClip 极简文本剪贴板项目规划书.md](../MiniClip%20极简文本剪贴板项目规划书.md) 逐条对下来的差异。**这不是缺陷清单**，是把“我们和规划书哪里不一样”摊开，避免以后有人以为实现是照着规划书抄的。

### 已经消解的分歧

- ~~**设计规格缺失。**~~ `design/DESIGN-SPEC.md` 现在存在（46 113 字节 / 45 KB，10 节：Token system、Component spec、带尺寸的线框图、列预算、WPF 实现注意事项、动效表、原则、克制清单、反默认评审、交付文件清单），同目录另有 `design/IMPLEMENTATION-NOTES.md`（2 944 字节）。**这一条不再是分歧。**
- ~~**JSON 磁盘格式不可读。**~~ 见“曾经报错的缺陷”第 4 条，已修复并实测。

### 仍然成立的分歧（保留）

> **编号说明：** 早先那份分歧清单里，“候选框列表不滚动”是第 4 条。它已经修好了（见“已修正的核对结论”第二节），所以下面的编号从 1 重新开始，**不再与旧编号对应**。

1. **规划书的托盘菜单更短。** 规划书 §15 只列出状态、清空历史和退出；当前实现在快捷键注册成功时还有“打开历史文件位置”“切换到浅色/深色外观”“更换快捷键”等入口，共 10 行，注册失败时多一行设置快捷键，共 11 行。状态行负责说明快捷键是否可用，外观入口不占用候选弹窗空间。这里记录的是实现超出原规划书的范围。

2. **设置对话框与开机自启动超出了规划书的 V1 范围。** 规划书第 29 行把范围写成“不增加搜索或设置页面”，第 78 行把“复杂设置页面”列进非目标。实现里有 `UI/HotkeySettingsWindow.xaml`（一个改快捷键的对话框，含“开机自动启动”开关）与 `Settings/StartupRegistration.cs`（写 `HKCU\...\CurrentVersion\Run` 的 `MiniClip` 值）。README 已经把这条作为“唯一的例外”写明。**仍然成立**，读者应当知道这是实现方的取舍，不是规划书的要求。

3. **规划书 §8 把 `WH_KEYBOARD_LL` 写成“候选方案”，实现把它当作常驻机制。** 规划书第 288–291 行的措辞是：`WH_KEYBOARD_LL` “作为 Clipboard Mode 内处理方向键、Enter、Esc 的**候选方案**”，并且“`Alt + V` 优先用 `RegisterHotKey` 注册；**只有确需拦截导航按键时才安装低级键盘 Hook**”。实现里只要进入 Clipboard Mode 就一定安装钩子（`MiniClipController.EnterClipboardMode` → `_keyboardHook.Install()`，失败还会把 `hook=False` 写进诊断），退出 Clipboard Mode / 失焦 / 异常 / 进程退出时卸载。也就是说钩子是这条链路的**标准机制**，不是备选。规划书暗示的“可以先不装钩子”在本设计下不成立：候选框带 `WS_EX_NOACTIVATE`、永远不获得键盘焦点，因此 `↑ ↓ Enter Esc` 根本不会作为 `WM_KEYDOWN` 送到任何 MiniClip 拥有的窗口，没有钩子就没有导航。规划书第 631 行“键盘 Hook 在需要导航时安装”其实已经默认了这一点，只是用词仍是“候选”。**仍然成立**，措辞差异值得记住。

4. **浅色主题缺失（2026-09-27 已修正）。** 早期 `design/_plan.md` 和 `design/miniclip.css` 曾提供一套未进入 WPF 的浅色方案。现在弹窗由 `PopupThemePalette.cs` 提供黑白浅色/深色两套主题，托盘菜单可切换；旧 CSS 与 `Tokens.xaml` 仍保留为早期设计和设置对话框资源。当前规格见 [design/MINIMAL-THEMES.md](../design/MINIMAL-THEMES.md)。

5. **设计交付物里还有三份文件没有被任何文档引用。** `design/index.html`（10 285 字节）、`design/miniclip.js`（20 668 字节）、`design/_probe.html`（16 647 字节）以及 `design/shots/` 目录在 README 和本文档里都没有链接；README 的“相关文档”表原先只列了 `_plan.md`、`_critique.md`、`miniclip.css`、`states.html`。文件在树里，只是没人从文档走得到。**新发现**，README 里已经补上链接。

## 复现本文档里那些数字的工具

三个一次性脚本留在 [artifacts/_verify/](../artifacts/_verify/)，它们不是产品的一部分，只是让上面的读数可复核：

| 脚本 | 用途 |
| --- | --- |
| [artifacts/_verify/pixel_scan.py](../artifacts/_verify/pixel_scan.py) | 逐像素扫描候选框快照，列出文本带的 y 区间与高度（DIP）——“已修正的核对结论”第一、三节的数字来自它 |
| [artifacts/_verify/clip_roundtrip.py](../artifacts/_verify/clip_roundtrip.py) | 通过 Win32 API 写系统剪贴板并读回，用于触发一次真实的剪贴板→落盘链路（PowerShell 传参会弄坏引号，所以用 Python） |
| [artifacts/_verify/EncoderProbe/](../artifacts/_verify/EncoderProbe/) | 三种 `JavaScriptEncoder` 的对照实测（`dotnet run --project`） |
