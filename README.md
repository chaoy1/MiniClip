简体中文 | [English](README.en.md)

# MiniClip

<p align="center"><img src="docs/images/app-icon.png" alt="MiniClip 应用图标" width="160"></p>

> 适用于 Windows 10/11 的文本剪贴板历史工具。在文字光标附近打开候选窗口，用键盘选回复制过的文本；候选窗口不抢焦点，原输入窗口保持活动状态。

MiniClip 常驻系统托盘，只处理文本，不提供云同步或遥测。[下载最新版本](https://github.com/chaoy1/MiniClip/releases/latest)。

## 功能亮点

- **键盘优先**：默认按 `Alt + V` 呼出，使用 `↑` / `↓` 选择、`Enter` 粘贴、`Esc` 关闭；快捷键可在设置中修改。
- **保持输入焦点**：候选窗口不激活，优先定位在文字光标附近；应用不提供光标坐标时使用焦点控件或窗口位置。
- **受控历史**：最多保留 100 条、总计 500,000 字符；完全相同的文本去重，图片、文件和富文本不入库。
- **粘贴保护**：粘贴前核对目标窗口；选择期间切换窗口或剪贴板被新内容覆盖时，取消自动粘贴。
- **本机存储**：历史以明文 JSON 保存；托盘菜单可清空历史、切换外观并打开设置。

## 界面预览

| 浅色 | 深色 |
| --- | --- |
| ![浅色候选窗口](docs/images/popup-light.png) | ![深色候选窗口](docs/images/popup-dark.png) |

[托盘菜单](docs/images/tray-menu.png) · [设置窗口](docs/images/settings.png) · [界面设计说明](docs/DESIGN.md)

## 快速开始

从 [GitHub Releases](https://github.com/chaoy1/MiniClip/releases/latest) 下载适合 Windows x64 的文件：

| 下载文件 | 使用方式 |
| --- | --- |
| `MiniClip-Setup-<版本>-x64.exe` | 推荐。双击安装；已包含 .NET 10 Desktop Runtime。可选择安装目录、开机启动和桌面快捷方式。 |
| `MiniClip-<版本>-win-x64.zip` | 解压后运行 `MiniClip.exe`，并保留压缩包内的全部文件。系统须已安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。 |

安装包目前未进行代码签名，Windows 可能显示来源确认提示。MiniClip 启动后不会出现主窗口，请在系统托盘查找图标。

## 键盘操作

`Alt + V` 呼出或关闭候选窗口 · `↑` / `↓` 选择记录 · `Enter` 粘贴到原窗口 · `Esc` 关闭

右键托盘图标可清空历史、选择浅色/深色/跟随系统、打开设置或退出；双击图标可打开设置。清空历史不会清除当前 Windows 剪贴板内容。

## 工作原理

```text
复制文本 → 剪贴板监听 → 去重与容量限制 → 本机 JSON 历史
                                          ↓
原窗口粘贴 ← 核对目标并发送 Ctrl+V ← 键盘选择 ← Alt+V 候选窗口
```

候选窗口不获取键盘焦点。按下 Enter 时，MiniClip 先核对原输入目标，再将所选文本写入系统剪贴板；短暂等待后再次确认目标和剪贴板未变化，才向原窗口发送 `Ctrl + V`。实现入口位于 `src/MiniClip/`；构建和验证方式见下方文档。

## 数据与隐私

- 历史记录是**本机明文 JSON**。MiniClip 不识别密码或验证码，复制这些内容后也可能留下记录；诊断日志不记录剪贴板原文。
- 程序目录可写时，历史、设置和日志保存在 `MiniClip.exe` 旁的 `data\`；否则回退到 `%LOCALAPPDATA%\MiniClip\`，并显示提示。
- 安装版卸载时会删除安装目录中的 `data\` 和 `%LOCALAPPDATA%\MiniClip\`。需要保留历史时请先备份。免安装版没有卸载器，删除程序目录前也应确认数据位置。删除文件不等于安全擦除。

## 已知限制

- 普通权限运行的 MiniClip 无法向管理员权限窗口注入粘贴按键；文本已放入剪贴板时，可手动按 `Ctrl + V`。
- 部分应用不提供文字光标坐标；自绘控件、游戏和安全桌面也可能拒绝模拟粘贴。
- 程序一次只允许运行一个实例。

## 开发

在 Windows 上安装 .NET 10 SDK 后，从仓库根目录构建：

```powershell
dotnet build src\MiniClip\MiniClip.csproj -c Release
```

安装包使用 `tools/build-installer.ps1` 和 Inno Setup 构建。发布步骤、自检命令与环境要求见[构建说明](docs/BUILD.md)。

## 文档

| 文档 | 内容 |
| --- | --- |
| [构建与发布](docs/BUILD.md) | 环境要求、安装包和免安装版的生成方法。 |
| [验证说明](docs/VERIFICATION.md) | 自检范围与发布前人工检查。 |
| [界面设计](docs/DESIGN.md) | 当前主题、布局与界面截图。 |

仓库保留源码、文档和展示图片；发布包及本机测试产物不纳入 Git 跟踪。
