# MiniClip 视觉设计（design/）→ WPF 实现，需要注意的 8 件事

设计交付物在 `design/`：`DESIGN-SPEC.md`（工程规格）、`index.html`（交互原型）、
`states.html`（状态接触表）、`miniclip.css` / `miniclip.js`（token 与渲染器）、
`shots/`（截图）。原型里 1 CSS px = 1 DIP。以下是规格里可能被忽略、但会直接影响
实现正确性的几点。

1. **选中行是 4+1 重冗余，不是"换个背景色"。**
   导轨（shape）+ 插入符（shape）+ 薄荷色（hue）+ 染色底（luminance）+ 字重 500。
   任何一项失效都不影响可读性——这是无障碍要求，不要简化成"只改背景色"。

2. **导轨同时是位置指示器。** 不要额外加序号列、`n/总数` 计数或滚动条。
   溢出只用面板右缘 2 DIP 的胶囊表示。规划书里的"默认 5~8 行"取 6 行。

3. **CJK 必须显式指定 `Microsoft YaHei UI`。** Cascadia Mono / Consolas 都没有中文
   字形。实测：`暂无文本记录` 用 YaHei UI 是 78.0 DIP，用系统 sans-serif 回退是
   67.5 DIP — 差 15.5%，不指定就会和周围拉丁文不同字重、不同基线。

4. **列宽预算 44 列，按"终端列"算，不是按字符数。** 文字列 353 DIP；
   实测 13 DIP 下拉丁字符 7.617 DIP、CJK 字符 13.000 DIP（1 em）。
   所以全角算 2 列。这样断行位置在原型和 WPF 里完全一致，且行高恒定 40 DIP。

5. **截断用 `...`（三个句点），不要用 `…`（U+2026）。** Cascadia Mono 的 `…`
   字宽 1.047 em（比拉丁字符还宽，接近一个全角），会悄悄吃掉预算；而且字体可以合法
   地不包含这个字形，那样被截断的行会**完全没有截断标记**。WPF 里可以用
   `GlyphTypeface.CharacterToGlyphMap` 检查覆盖率。

6. **Cascadia Mono 没有粗体。** 原型里选中行 `font-weight:500` 是可变字体轴；
   WPF 里 `FontWeight="SemiBold"` 会合成假粗体，糊掉 13 DIP 的笔画并改变字宽。
   要么嵌入可变字体，要么选中行保持 `Normal` —— 另外 4 重冗余已经足够。

7. **空白条目不能渲染成空行。** 规划书规定不 trim，所以 `"   "` 是真实记录。
   渲染为斜体 `¶` + `3 个空白字符`（12 DIP chrome 字号）。复制值仍是 `"   "`。

8. **列表不要用 `ScrollViewer`，确认反馈不要用第二个 Window。**
   两者都会给一个"绝不抢焦点"的窗口引入焦点风险。列表用
   `ItemsControl` + `VirtualizingStackPanel`（或直接 6 行 + `TranslateTransform`），
   确认条复用 popup 自己的 26 DIP 底栏。

补充：暗色是主视觉；浅色是同一套结构、同一个选中语言，只是反相后重新推导。
浅色强调色是 `#0D7261`（不是 `#0F7F6B`）—— 后者在选中行底色上只有 4.12:1，
低于 AA 的 4.5:1。所有对比度数值已在 `DESIGN-SPEC.md` §1 中实测列出。
