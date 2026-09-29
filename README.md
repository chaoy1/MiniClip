# MiniClip

MiniClip 是一款适用于 Windows 10/11 的文本剪贴板历史工具。按下全局快捷键后，它会在输入位置附近显示历史记录；使用方向键选择，按 Enter 粘贴。候选窗口不会获取键盘焦点，原输入窗口保持活动状态。

[下载最新版本](https://github.com/chaoy1/MiniClip/releases/latest) · [构建与发布](docs/BUILD.md) · [验证说明](docs/VERIFICATION.md)

| 浅色 | 深色 |
| --- | --- |
| ![浅色候选窗口](docs/images/popup-light.png) | ![深色候选窗口](docs/images/popup-dark.png) |

## 安装

在 [GitHub Releases](https://github.com/chaoy1/MiniClip/releases/latest) 中选择适合的 Windows x64 文件：

| 文件 | 适用情况 |
| --- | --- |
| `MiniClip-Setup-<版本>-x64.exe` | 推荐。安装包包含 .NET 10 Desktop Runtime，无需另行安装运行时。 |
| `MiniClip-<版本>-win-x64.zip` | 免安装版本。解压后运行 `MiniClip.exe`；系统须已安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。请保留压缩包中的全部文件。 |

安装包默认安装到当前用户目录，可在向导中更改路径，并选择是否创建桌面快捷方式或设置开机启动。安装包当前未进行代码签名；Windows 可能显示来源确认提示。

## 使用

| 操作 | 功能 |
| --- | --- |
| `Alt + V` | 打开或关闭候选窗口；可在设置中修改。 |
| `↑` / `↓` | 选择历史记录。 |
| `Enter` | 将选中内容写入系统剪贴板并粘贴到原窗口。 |
| `Esc` | 关闭候选窗口。 |

MiniClip 常驻系统托盘。右键托盘图标可清空历史、切换浅色或深色外观、打开设置以及退出程序；双击图标可打开设置。历史最多保留 100 条，并受 500,000 字符总量限制。只记录文本，不记录图片、文件或富文本。

## 数据与隐私

- 历史记录以**明文 JSON** 保存在本机。MiniClip 不会识别密码或验证码；复制这些内容后，它们也可能进入历史记录。
- 程序目录可写时，历史、设置和日志保存在 `MiniClip.exe` 所在目录的 `data\` 中；否则回退到 `%LOCALAPPDATA%\MiniClip\`，并显示提示。
- MiniClip 不提供云同步或遥测，诊断日志不记录剪贴板原文。
- 安装包卸载时会删除安装目录下的 `data\`，并清理 `%LOCALAPPDATA%\MiniClip\`。卸载前请备份需要保留的历史记录。删除文件不等于安全擦除。
- 免安装版本没有卸载器。若数据保存在程序目录，删除该目录会同时删除历史；如已回退到 `%LOCALAPPDATA%\MiniClip\`，需手动处理该目录。

## 已知限制

- 普通权限运行的 MiniClip 无法向管理员权限窗口注入粘贴按键；此时可手动按 `Ctrl + V`。
- 某些应用不提供文字光标坐标，候选窗口会退回到焦点控件或窗口附近。部分自绘控件、游戏和安全桌面可能不接受模拟粘贴。
- 程序仅支持单实例运行。

## 项目结构

| 路径 | 内容 |
| --- | --- |
| `src/MiniClip/` | WPF 应用源码与资源。 |
| `installer/` | Inno Setup 安装配置。 |
| `tools/` | 安装包构建和图标生成脚本。 |
| `docs/BUILD.md` | 环境要求、构建与发布步骤。 |
| `docs/VERIFICATION.md` | 自检方法、覆盖范围与人工验收事项。 |
| `docs/DESIGN.md` | 当前界面的视觉说明与截图。 |

仓库只保存源码、文档和必要的展示图片。安装包、免安装压缩包及本机测试产物通过 GitHub Releases 分发或在本地生成，不纳入 Git 跟踪。
