# MiniClip 弹窗外观：浅色与深色

2026-09-27 修订。本文是当前 WPF 弹窗的设计说明；目录中的 `DESIGN-SPEC.md`、`miniclip.css` 和 HTML 原型记录的是先前的蓝灰、薄荷绿方案。

## 设计原则

- 弹窗只显示剪贴板条目。底部的 `↑↓ / Enter / Esc` 提示栏已移除。
- 使用柔和的中性背景、细边框、自然文字层级和内收的圆角选中底色；不使用彩色强调、渐变、箭头、装饰线或模糊阴影。
- 两套主题共用尺寸与排版，切换时不改变键盘操作或无焦点窗口行为。托盘右键菜单的“外观设置”子菜单提供浅色、深色、跟随系统，选择写入 `settings.json`。
- 剪贴板内容使用系统 UI 字体以兼顾中文和英文；每条显示单行预览，超长内容省略，右侧保留多行标记。

## 色彩与尺寸

| 项目 | 浅色 | 深色 |
| --- | --- | --- |
| 背景 | `#FDFDFD` | `#1B1B1B` |
| 当前行 | `#ECECEC` | `#303030` |
| 边框 | `#E5E5E5` | `#454545` |
| 正文 | `#242424` | `#F0F0F0` |
| 次要文字 | `#747474` | `#ABABAB` |
| 滚动指示 | `#A7A7A7` | `#5A5A5A` |

历史弹窗宽 220 DIP，托盘主菜单宽 184 DIP，外观子菜单宽 128 DIP。历史弹窗单行高 26 DIP，滚动视口外上下各留 5 DIP；最多同时显示 5 行，窗口最大高度 142 DIP。边框 1 DIP、窗口圆角 10 DIP。选中底色左右各内收 4 DIP，圆角 6 DIP；滚到最上方时，选中底色距顶部约 8 DIP，不露出第六条记录。滚动指示覆盖在列表右缘，不占用一整列宽度；选中底色到右边框的实测间距约 5.3 DIP。滚动指示只在滚动时短暂出现。列表从上到下由旧到新，默认选中最下方的最新记录。窗口保持 `WS_EX_NOACTIVATE`，优先贴近文字光标，并保留键盘导航。

## 原生渲染

浅色：

![浅色弹窗](shots/popup-light-current.png)

150% 缩放下的浅色弹窗：[截图](shots/popup-light-150-current.png)。

100 条历史滚动到最旧记录时的选中态：[截图](shots/popup-scrolled-100-current.png)。

深色：

![深色弹窗](shots/popup-dark-current.png)

设置窗、托盘菜单、提示和清空确认窗也使用相同的中性配色；它们的浅色表面为 `#FDFDFD`。短菜单依次显示状态、清空历史、外观设置、设置和退出。外观子菜单显示当前模式的勾选，并支持鼠标和方向键。设置页包含快捷键、开机启动、历史条数、打开历史文件位置及明文保存说明。清空历史必须再确认。托盘菜单按键焦点用浅灰选中底色表示，不绘制虚线；分隔线左右留白。各窗口的浅色和深色截图在 `design/shots/` 中以 `-current.png` 结尾。实现入口为 `src/MiniClip/UI/PopupWindow.xaml`、`TrayMenuWindow.xaml`、`AppearanceMode.cs`、`PopupThemePalette.cs` 和 `ChromeThemePalette.cs`。截图由程序的 `--selftest` 模式从 WPF 视觉树导出。

150% 缩放下的已打开托盘菜单：[截图](shots/tray-menu-focused-150-current.png)。

外观子菜单：[浅色](shots/appearance-submenu-light-current.png) · [深色](shots/appearance-submenu-dark-current.png)。

外观子菜单固定在主菜单右侧。靠近屏幕右缘时，主菜单先向左预留子菜单空间；[150% 缩放的展开图](shots/appearance-expanded-right-current.png)。
