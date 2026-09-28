# MiniClip 菜单、历史安全与布局验收（2026-09-27）

## 构建与自检

在 `D:\Documents\Projects\MiniClip` 执行独立 .NET 10 SDK 的 Release 构建，结果为 **0 警告、0 错误**。随后运行 Release `MiniClip.dll --selftest artifacts/selftest-2026-09-27-reliability-menu-final`，控制台报告 **42 项 PASS**、`ALL CHECKS PASSED`，进程退出码为 **0**。[原始控制台输出](../artifacts/selftest-2026-09-27-reliability-menu-final/selftest-console.txt)。自检覆盖历史上限、故障读取与删除、外观偏好、菜单路由、五行视口、滚动选中态、NoActivate、键盘钩子、托盘、日志清理及各窗口截图。

| 重点检查 | 实测结果 |
| --- | --- |
| 历史读取失败 | 独占锁模拟无法读取，菜单启用“清空异常历史…”恢复入口；后续新增记录留在内存，原历史文件字节不变；删除失败提示，解除锁后可重试成功 |
| 设置状态 | 首次使用提示标记与“跟随系统”连续保存后，磁盘与控制器状态一致 |
| 菜单 | 主菜单 184 DIP，外观子菜单 128 DIP；浅色、深色、跟随系统三项，当前模式显示勾选，子菜单命令路由通过 |
| 历史弹窗 | 最大 142 DIP；滚动视口为 129 DIP，恰好五条 26 DIP 行所在区域；滚到最上方时选中底色距上边约 8 DIP，距右边约 5.3 DIP |
| 日志 | 七天、单文件 1 MB、目录 10 MB 的清理检查通过 |
| 视觉 | 浅色和深色设置窗、菜单、外观子菜单、提示、清空确认窗，共 10 张快照导出 |

自检在 100 条测试记录下循环显示候选框十次，样本首次 146.9 ms、中位数 146.9 ms、最慢 183.6 ms，私有内存样本增量 27.2 MB。这是在同一进程完成全部 WPF 自检后测得的短时样本，不能推断为常驻内存泄漏；它高于规划书 `< 100 ms` 的候选框响应目标，后续若优化应先定位布局与窗口显示耗时。

重启后的真实 Release 进程为 `src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe`（本次 PID 21300）。诊断日志记录 `startupReadyMs=163.9`、工作集 59 MB、私有内存 15 MB。启动时间从 `OnStartup` 入口到托盘与首次提示处理完毕，尚不是规划书要求的 30 次端到端进程创建中位数。

## 截图

- [浅色菜单](../design/shots/tray-menu-light-current.png) · [深色菜单](../design/shots/tray-menu-dark-current.png)
- [浅色外观子菜单](../design/shots/appearance-submenu-light-current.png) · [深色外观子菜单](../design/shots/appearance-submenu-dark-current.png)
- [五条记录](../design/shots/popup-light-150-current.png) · [滚到顶部](../design/shots/popup-scrolled-100-current.png)

## 尚需真实桌面观察的行为

自检通过 WPF 命令事件验证子菜单选择，尚未用物理鼠标完成从主菜单移入子菜单的完整操作。跟随系统的颜色解析已通过亮/暗输入测试，当前系统配置为浅色；本次没有切换用户的 Windows 外观设置。发布到其他电脑前，还需执行干净环境安装与 DPI、真实光标位置验证。

## 2026-09-28 子菜单位置修正

屏幕右缘处的展开方向已修正。Release 重新构建为 0 警告、0 错误；[自检输出](../artifacts/selftest-2026-09-28-submenu-right/selftest-console.txt)显示 42 项 PASS、退出码 0。主菜单窗口范围为 `x=2044..2320`，子菜单为 `x=2316..2508`，工作区右缘 `x=2520`：子菜单位于主菜单右侧，且完整留在工作区内。[展开截图](../design/shots/appearance-expanded-right-current.png)。

## 2026-09-28 Typora 光标定位修正

Typora 的主窗口焦点句柄覆盖整个窗口，并未提供 Win32 光标句柄；原实现只接受与主窗口完全相同的 UI Automation 进程 ID，且 100 ms 内没有结果时直接退到窗口左上角。现在允许同名渲染进程的焦点元素，但要求其坐标落在目标窗口和焦点控件内；没有文本光标时优先用自动化焦点控件边界，较慢的有效结果可在弹窗打开后 600 ms 内重新定位。定位日志只记录来源、坐标、耗时和进程 ID。

[初版 Release 自检](../artifacts/selftest-2026-09-28-caret-anchor/selftest-console.txt)为 43 项 PASS、退出码 0，包含 Win32 文本框光标位置与拒绝窗口原点、焦点控件外坐标的检查。该次自检不能模拟 Typora 的编辑器可访问性提供者，随后通过真实前台操作继续验证。

用户在 Typora 初次实测仍报告“位置有偏差”。当时三次快捷键日志均为 `AutomationFocus x=709 y=95`，即焦点编辑区的左上偏移；未取得文字插入点。后续增加 MSAA `OBJID_CARET` 查询，使用焦点窗口句柄并在目标仍处前台时尝试全局光标对象，拒绝高度为零及窗口原点等无效矩形。新增自检先因方法缺失失败，接入后在真实 WinForms 文本框取得与插入点相近的坐标；[Debug 自检日志](../artifacts/selftest-2026-09-28-accessible-final/selftest.log)结果为 PASS、退出码 0。

随后完成 Release 重新构建（0 警告、0 错误），[Release 自检日志](../artifacts/selftest-2026-09-28-accessible-release/selftest.log)全部通过、退出码 0。新进程 PID 20140 的 Typora 快捷键日志显示定位来源为 `AccessibleCaret`；插入点沿正文移动时，纵坐标分别出现 492、873、1083、1311 等值，读取耗时约 1–26 ms。用户再次在 Typora 正文实测并确认弹窗位置“准确”。这项结论限于当前 Typora 版本和本机显示环境；其他未提供有效光标对象的应用仍会使用焦点控件回退定位。

## 2026-09-28 跟随系统与夜间模式

之前“跟随系统”只读取 `AppsUseLightTheme`，因此无法响应 Windows 外壳深色设置和独立的夜间模式开关。现在任一深色条件成立时使用深色配色；手动浅色、深色优先于自动判断。夜间模式状态从 Windows CloudStore 的当前用户二进制数据只读解析；对计划模式按已保存的起止时间推算，并在每次呼出时重新计算。格式不受支持时回退到常规颜色模式。每次呼出候选框前重新解析主题，避免复用的窗口停留在旧配色。计划时段的手动临时覆盖尚未在真实桌面中验证。

本机实测夜间模式关闭时状态值长 41 字节，手动开启后长 43 字节并出现开启字段。主题判定和这两种二进制样本、计划时段样本、截断异常样本均纳入自检。[Release 自检日志](../artifacts/selftest-2026-09-28-nightlight-release/selftest.log)全部通过、退出码 0；构建结果为 0 警告、0 错误。新进程 PID 28924 的两次真实呼出在同一个候选框句柄 `0x1E07C0` 上依次记录 `theme=Dark`、`theme=Light`，确认切换夜间模式后复用窗口能够切换黑白配色。当前系统应用与外壳颜色模式均为浅色，夜间模式已关闭。
