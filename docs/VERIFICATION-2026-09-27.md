# MiniClip 当前版本验收记录（2026-09-27）

> 本页保留较早版本的 39 项验收结果。菜单与布局调整后的当前结果见 [VERIFICATION-2026-09-27-reliability-menu.md](VERIFICATION-2026-09-27-reliability-menu.md)。

本页记录当前代码对应的 Release 构建与自动自检。较早版本的详细核对过程保留在 [VERIFICATION.md](VERIFICATION.md)，其中的鼠标定位、8 行视口和 30 条容量不再描述当前行为。

## 构建与自检

在项目根目录执行：

```powershell
$env:PATH = 'D:\Software\dotnet-sdk;' + $env:PATH
dotnet build src\MiniClip\MiniClip.csproj -c Release --no-restore
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe --selftest artifacts\selftest-2026-09-27-selection-inset
```

Release 构建结果为 **0 个警告、0 个错误**。自检退出码为 **0**，控制台有 **39 项 `[PASS]`** 和 `ALL CHECKS PASSED`；原始输出在 [selftest-console.txt](../artifacts/selftest-2026-09-27-selection-inset/selftest-console.txt)。自检会短暂测试托盘图标与系统剪贴板，剪贴板测试结束后会恢复原内容。

本次新增或重点复核的自动检查包括：

| 检查 | 结果 |
| --- | --- |
| 最近历史与磁盘容量 | 100 条上限、500,000 字符预算通过 |
| 弹窗 | 220 × 142 DIP 最大尺寸、5 行视口、最新项在底部、100 条滚动与导航通过；选中底色右侧间距从 22.7 DIP 缩至 5.3 DIP |
| 位置 | Win32 文本框光标坐标读取、工作区边缘翻转与钳制、不抢焦点窗口样式通过 |
| 日志 | 7 天过期删除、10 MB 目录预算、活动日志保留通过 |
| 设置窗 | 开机启动选择与保存状态、历史条数及打开文件位置按钮通过 |
| 托盘菜单 | 220 DIP 自定义圆角短菜单定位、命令路由与禁用状态通过 |
| 清空历史 | 取消时不删除、明确确认后才允许删除的窗口交互通过 |
| 提示 | 自定义提示显示且不抢焦点通过 |
| 浅色和深色 | 设置窗、托盘菜单、提示及清空确认窗共 8 张 WPF 快照导出通过 |
| 滚动提示 | 静止时隐藏、滚动时显示、停下后自动隐藏通过 |

浅色与深色的弹窗、设置窗、托盘菜单、提示和清空确认窗截图见 [design/shots](../design/shots/) 中以 `-current.png` 结尾的文件。`popup-light-150-current.png` 与 `tray-menu-focused-150-current.png` 按 150% 缩放复核圆角边缘和菜单布局；`popup-scrolled-100-current.png` 展示滚动时选中底色贴近右边框。截图已人工查看，220 DIP 宽度下文字、边框和对比度均可读；浅色表面为 `#FDFDFD`。

构建完成后已重新启动 `src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe`。进程路径与 Release 可执行文件一致，启动日志记录了 `dpiAwareness=per-monitor`；日志不记录剪贴板正文。

## 仍需在真实使用中确认

自动自检使用 WinForms 文本框验证 Win32 光标，未覆盖 Chrome、Word、终端等所有输入控件的 UI Automation 光标提供器。提供器不返回坐标时会退回到焦点控件或窗口位置。多显示器混合缩放与托盘菜单在不同任务栏位置的实际视觉效果仍需在对应桌面环境手动确认。自检 PNG 是 WPF 视觉树渲染结果，并不等于对每个真实应用窗口的交互验收。
