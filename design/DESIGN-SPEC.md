# MiniClip — 设计规格 / DESIGN SPEC

> 历史设计稿：本文描述最初的蓝灰、薄荷绿弹窗。2026-09-27 起，当前 WPF 弹窗改为黑白浅色/深色主题并移除底栏；请以 [MINIMAL-THEMES.md](MINIMAL-THEMES.md) 为准。

Handoff for the WPF/XAML implementation. Everything here is a **DIP** (device-independent
pixel = the WPF unit). The prototype uses CSS `px`, which is the same unit; 1 CSS px in
`design/*.html` = 1 DIP in XAML. Nothing in this document is a ratio, a percentage or an
`em` value that has to be recomputed per DPI — at 100 %, 125 %, 150 % and 200 % the
window simply renders larger in device pixels and every relationship below is unchanged.

Source of truth: `design/miniclip.css` (tokens + components), `design/miniclip.js`
(content model + row rendering), `design/index.html` (prototype),
`design/states.html` (contact sheet).

---

## 1. Token system

### 1.1 Color — dark (primary)

The base is a **blue-slate**, not a neutral near-black: the payload comes out of
terminals and editors, and a cool paper makes white text read as *paper*, not as *glow*.

| Token | Hex | Role |
| --- | --- | --- |
| `ink-900` | `#0F141B` | Popup surface — the panel's paper. Also the tray menu, the dialog, the field well's parent. |
| `ink-850` | `#121925` | Recessed well: the hotkey record field, the `Esc`/`Save` button fill, and the row hover wash. |
| `ink-700` | `#2C3A4C` | Hairlines at full strength (scroll indicator, dialog field border) — used where a line must be *read*, not merely sensed. |
| `paper-050` | `#E7EDF4` | Payload text: clip content, key names in the footer, menu item labels. 15.2:1 on `ink-900`. |
| `paper-500` | `#94A5BA` | Chrome text: footer action labels, the second preview line, dialog labels and help, the `↕ n` marker. 7.2:1 on `ink-900`. |
| `signal-500` | `#6FD3B4` | **Selection only.** The rail, the caret, the focus ring, the "recording" field border, the success word in the footer band. Never used for decoration. 7.5:1 on `wash-selected`, 9.9:1 on `ink-900`. |
| `wash-selected` | `#16332F` | The selected row's fill — a mint-tinted slab, not a grey one. |
| `wash-hover` | `#121925` | Row hover (identical to `ink-850`; hover is one step of luminance, nothing more). |
| `caution-450` | `#E0A94A` | Hotkey conflict: the popup's notice band, the dialog's conflict border, the tray menu's status line. 8.5:1 on `ink-900`. |
| `danger-450` | `#E8826B` | Paste failure, and the destructive tray item ("退出 MiniClip"). |
| `stroke-accent` | `rgba(111,211,180,.34)` | The 1 DIP inset ring on the selected row. |
| `stroke-hairline` | `rgba(231,237,244,.10)` | The 1 DIP separator between rows and above the footer band. |
| `rim-light` | `inset 0 1px 0 rgba(231,237,244,.07)` | Top edge highlight — how the panel reads as raised in dark mode, from its own edge. |
| `scrim` | `rgba(7,10,15,.55)` | Behind the hotkey dialog when it is shown over other content. |

### 1.2 Color — light (same structure, re-derived)

Windows users run light mode; this is not a "light theme feature", it is the same design
with the neutrals inverted. The selection language is **identical**: mint rail + caret +
tinted slab + brighter text. Only the accent is darkened, because `#6FD3B4` on white
cannot carry a 4.5:1 line.

| Token | Hex | Role | Contrast |
| --- | --- | --- | --- |
| `surface` | `#FFFFFF` | Popup / menu / dialog surface | — |
| `well` | `#EDF2F8` | Recessed field, button fill, hover wash | — |
| `wash-selected` | `#DCEFE9` | Selected row fill | payload 14.6:1 |
| `hairline` | `#D3DCE6` | Full-strength lines | — |
| `hairline-soft` | `rgba(19,26,35,.09)` | Row separators | — |
| `text` | `#131A23` | Payload | 17.5:1 on `#FFFFFF` |
| `text-chrome` | `#56637A` | Chrome, second preview line | 6.1:1 |
| `accent` | `#0D7261` | Rail, caret, ring, success | **4.9:1 on the selected wash**, 5.8:1 on white |
| `stroke-accent` | `rgba(13,114,97,.40)` | Selected ring | — |
| `caution` | `#8A5B00` | Conflict | 5.9:1 on white |
| `danger` | `#B03A24` | Failure, destructive | 6.0:1 on white |
| `rim-light` | `inset 0 1px 0 rgba(255,255,255,.9)` | Top edge | — |
| `scrim` | `rgba(19,26,35,.28)` | Dialog backdrop | — |

> The light accent is `#0D7261`, not the `#0F7F6B` this spec first proposed. `#0F7F6B`
> measures 4.92:1 on white — fine — but only **4.12:1** on the selected row's
> `wash-selected`, which is where the accent actually has to be read (the `已粘贴` word and
> the rail both sit on it). AA for 12 DIP text is 4.5:1 and it failed. Darkening to
> `#0D7261` gives 4.9:1 on the wash and 5.8:1 on white. This is the one number in the
> light palette that is not simply a mirror of the dark one, because in light mode the
> accent is dark-on-light and must clear the *tinted* surface, not just the white one.

**Accessibility floor met:** every body-text pair below is ≥ 4.5:1 — verified, dark and
light, including the accent on the selected surface. The selected row's payload is 11.5:1
(dark) and 14.6:1 (light). Chrome at 12 DIP is treated as body text, not "large text", so
4.5:1 applies to it too.

Full measured matrix (computed, not estimated):

| Pair | Ratio | |
| --- | --- | --- |
| dark payload on surface | 15.7:1 | AAA |
| dark chrome on surface | 7.4:1 | AAA |
| dark payload on **selected** | 11.5:1 | AAA |
| dark accent on **selected** | 7.5:1 | AAA |
| dark caution on surface | 8.8:1 | AAA |
| dark danger on surface | 6.9:1 | AA |
| light payload on surface | 17.5:1 | AAA |
| light chrome on surface | 6.1:1 | AA |
| light payload on **selected** | 14.6:1 | AAA |
| light accent on **selected** | 4.9:1 | AA |
| light accent on surface | 5.8:1 | AA |
| light caution on surface | 5.9:1 | AA |
| light danger on surface | 6.0:1 | AA |

### 1.3 Type

| Role | Size | Line-height | Weight | Letter-spacing | Stack |
| --- | --- | --- | --- | --- | --- |
| Row content | 13 | 20 | 400 | 0 | `Cascadia Mono, Consolas, Microsoft YaHei UI, monospace` |
| Row content, selected | 13 | 20 | 500 | 0 | same |
| Row marker `↕ n` | 12 | 16 | 400 | 0 | same |
| Chrome (footer, menu, dialog) | 12 | 16 | 400 | 0 | `Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, sans-serif` |
| Chrome strong (menu item, dialog title) | 12–13 | 16–20 | 600 | 0 | same |
| Key names (`↑↓`, `Enter`, `Esc`, `Alt+V`) | 13 | 16 | 400 | 0 | the **content** stack — a key name is literal machine text |

Letter-spacing is **0 everywhere**. Nothing is set in capitals; nothing is tracked out.

**Why the content face is Cascadia _Mono_ and not Cascadia _Code_.** Code carries
programming ligatures: `!=`, `->`, `=>`, `www` are drawn as single glyphs. A clipboard
preview must never draw a glyph that is not the character the user copied. Mono has no
ligatures.

**Why `Microsoft YaHei UI` is written into both stacks.** Cascadia Mono and Consolas have
no CJK coverage at all. Without it, `暂无文本记录` falls back through GDI font linking to
whatever the system picks, with a different weight and a different baseline from the Latin
around it. Measured on this machine: YaHei UI is **15.5 % wider** than the `sans-serif`
fallback (`暂无文本记录` = 78.0 DIP vs 67.5 DIP at 13 DIP), so the mismatch is visible, not
theoretical. `Microsoft YaHei UI` (not `Microsoft YaHei`) is the UI cut and matches the
shell and the chrome face. Cascadia Mono has no bold face either — see §5.

**WPF notes.** `Segoe UI Variable Text` exists on Windows 11 only; the `Segoe UI` fallback
covers Windows 10. Use `TextOptions.TextFormattingMode="Ideal"` and
`TextOptions.TextRenderingMode="ClearType"`; `Display` mode quantises advances and would
break the column budget in §4.

### 1.4 Spacing scale

`4 · 6 · 8 · 10 · 12 · 14 · 16 · 20 · 24 · 32`. Nothing else, with three named
exceptions that are optical rather than structural: the 9 DIP leading between the gutter
and the text (eyeball, not the 8 DIP step), the 5 DIP key-to-label gap in the footer, and
the 2 DIP lift on the panel's entrance.

### 1.5 Radii

| Token | Value | Applied to |
| --- | --- | --- |
| `rad-key` | 4 | Buttons, the hotkey field, the checkbox, menu item hover |
| `rad-rail` | 2 | The selection rail (a 3×20 bar with 2 DIP ends) |
| `rad-menu` | 6 | Tray context menu |
| `rad-popup` | 10 | The candidate popup |
| `rad-dialog` | 12 | Hotkey settings window |

Radii are **not uniform**: hierarchy is expressed as panel > window > menu > control, and
the rail is a capsule because it is a pill, not a corner.

### 1.6 Elevation / shadow

| Token | Value |
| --- | --- |
| `shadow-panel` | `0 0 0 1px rgba(0,0,0,.30), 0 14px 34px -10px rgba(0,0,0,.62), 0 3px 9px -4px rgba(0,0,0,.45)` |
| `shadow-menu` | `0 0 0 1px rgba(0,0,0,.28), 0 10px 26px -8px rgba(0,0,0,.58), 0 2px 6px -3px rgba(0,0,0,.40)` |
| `shadow-dialog` | `0 0 0 1px rgba(0,0,0,.32), 0 26px 64px -16px rgba(0,0,0,.70), 0 6px 18px -8px rgba(0,0,0,.50)` |
| `shadow-key` | `0 1px 0 rgba(0,0,0,.35)` (pressed-key underside on `mc-kbd`) |
| `rim-light` | `inset 0 1px 0 rgba(231,237,244,.07)` — dark mode only |

In WPF: a `DropShadowEffect` per layer, `ShadowDepth` 0 for the contact layer (a 1 DIP
ring), 3 and 14 for the two ambient layers, `BlurRadius` ≈ 2×`ShadowDepth + 6`,
`Opacity` as above, `Color=#000000`. The panel is drawn into a window **24 DIP larger on
every side** than the panel itself so the shadow is never clipped by the window rect
(see §3.1) — and remember `AllowsTransparency="True"` plus `WindowStyle="None"` costs you
hardware acceleration on some drivers; verify the < 100 ms first-frame budget with it on.

### 1.7 Borders and hairlines

There is **no `Border` element on the popup**. The client's brief says no border, and the
panel gets its edge from two things instead: the `rim-light` inset and the contact layer
of the shadow. Inside the panel there are two hairline styles, both 1 DIP:

* `stroke-hairline` `rgba(231,237,244,.10)` — between rows, and between the list and the
  footer band. On the selected row this is replaced by `stroke-accent`.
* `hairline` `#2C3A4C` — only where a line must be legible as a line: the dialog's field
  border and the scroll indicator.

### 1.8 Motion

| Token | Duration | Easing | Applied to |
| --- | --- | --- | --- |
| `t-appear` | 90 ms | `cubic-bezier(.2,.8,.2,1)` | Popup entrance: opacity 0→1, scale .98→1, translateY 2→0 DIP |
| `t-dismiss` | 70 ms | same | Popup exit: **opacity only** — no scale, the panel is being dismissed by a key, not thrown away |
| `t-row` | 70 ms | same | Row background, ring, rail and caret opacity |
| `t-confirm` | 160 ms | same | The confirmation band's own fade-in |
| `t-menu` | 120 ms | same | Tray menu and dialog: opacity 0→1, translateY 3→0, scale .985→1 |
| `t-flash` | 160 ms | same | The target control's brief brightening after a successful paste (app-side, not panel-side) |

**Nothing else in the panel ever animates.** No hover transitions on menu items beyond
the wash, no per-row entrance, no stagger.

`prefers-reduced-motion: reduce` multiplies every duration by zero via a `--mc-motion`
token — including the menu and dialog pops, not only the panel. In WPF, read
`SystemParameters.ClientAreaAnimation` and `SystemParameters.MenuAnimation` and skip the
`Storyboard` entirely when either is false. A reduced-motion user must still see the panel
inside the 100 ms budget; do not substitute a slower cross-fade.

### 1.9 Z-order / window layering (app-side)

| Layer | Window | Style |
| --- | --- | --- |
| 1 | Candidate popup | `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW \| WS_EX_TOPMOST`, `ShowActivated=false`, `Topmost=true`, `ShowInTaskbar=false` |
| 2 | Confirmation band | Drawn **inside** the popup window (§6) — never a second window, a second window is a second chance to steal focus |
| 3 | Tray menu | `ContextMenu` on the `NotifyIcon` |
| 4 | Hotkey dialog | Ordinary activated window (`WS_EX_NOACTIVATE` **not** set — it is a real dialog) |

---

## 2. Component spec

### 2.1 Popup shell

```
width                 400 DIP (fixed)
height                286 DIP default  = 10 + 6×40 + 10 + 26
                      246 DIP empty    = 70 + 26
radius                10 DIP
padding               0
inner list padding    10 DIP top and bottom
footer band           min 26 DIP  = 4 + 16 + 5 + 1 DIP hairline
background            ink-900 / #FFFFFF
edge                  rim-light inset + shadow-panel; NO Border element
```

The panel is **two regions only: rows and a footer band.** There is no title bar, no
close button, no scrollbar track, no header, no scroll shadow.

### 2.2 Row (40 DIP)

```
┌── 14 ──┬─ 9 ─┬────────── 353 ──────────┬─ 24 ─┐
│ gutter │lead │        text             │ mark │   40 DIP
└────────┴─────┴─────────────────────────┴──────┘
```

| Part | Geometry | Content |
| --- | --- | --- |
| Gutter | 0–14 DIP, holds the rail and caret | — |
| Rail | `x=0 w=3 h=20`, `y=10` within the row, `radius 2`, `signal-500` | Selection. Visible **only** on the selected row (opacity 0→1, 70 ms). |
| Caret | 6×6 DIP rotated 45°, borders 2 DIP `top`+`right`, `x=5 y=17`, `signal-500` | Selection, and "Enter acts on this" |
| Text | `x=23`, width 353 DIP, `line-height 20`, vertically centred | 1 or 2 lines |
| Marker | `x=376 w=24`, right-aligned, `paper-500` | `↕ n` — **multi-line rows only** |

State matrix:

| State | Background | Ring | Rail | Caret | Text |
| --- | --- | --- | --- | --- | --- |
| default | transparent | — | hidden | hidden | `paper-050`, weight 400 |
| hover | `ink-850` | — | hidden | hidden | `paper-050`, weight 400 |
| selected | `wash-selected` `#16332F` | 1 DIP inset `stroke-accent` | shown | shown | `paper-050`, **weight 500** |
| disabled | n/a in V1 | — | — | — | — |
| empty-history | the whole panel is replaced by §2.6 | | | | |

Four independent encodings of "selected" (**shape**: rail + caret; **hue**: mint;
**luminance**: the tinted slab; **weight**: 500) — this is what makes it survive
greyscale, deuteranopia and a bad laptop panel. There is deliberately no full-row accent
*fill*: an accent-filled row shouts across the desk, and this panel's job is to not
interrupt.

**There is no disabled row in V1.** The spec's only candidate ("清空历史" leaving nothing
behind) is handled by the empty state, which is a *panel* state, not a row state — see
the restraint note in §8.

The `↕ n` marker appears **only** when the payload contains ≥ 2 non-empty lines, and `n`
is the true count of those lines. Rationale: a 40-character `git status` cannot be
multi-line, so a marker on it would be pure decoration, while on a 4-line commit message
the marker is the missing half of a 2-line preview — it says "the box is showing you two
of four". Single-line rows — including a 600-character URL — get no marker at all.

### 2.3 Gutter / rail as the position indicator

The rail is the popup's **only** position readout, and this is deliberate: a numbered
index column, an `n/total` counter and a scrollbar would each be a second widget reading
the same number.

* The rail's **presence** says *which* row is selected.
* Its **vertical position within its row** says *where in the list* you are: it stays
  centred in the row for every row, but the rows that are on screen shift, so the rail's
  screen position is the list position. First row → rail at the panel's top; last row →
  rail at the bottom.
* **Overflow** is confirmed separately, by a 2 DIP `ink-700` capsule at the panel's right
  edge, shown only when `history.Count > visibleRows`. In the prototype the list scrolls
  natively with a hidden scrollbar (`scrollbar-width: none`, `::-webkit-scrollbar{width:0}`).

**No numbering.** `1..30` in a gutter would print a constant on every row; the only thing
that varies is which row is selected, and the rail already owns that axis.

### 2.4 Machine-text behaviour (the display transform)

The copy is **byte-exact**; everything below is display-only.

| Situation | Rule |
| --- | --- |
| Line endings | `\r\n` and `\r` normalised to `\n` for display |
| Tabs | expanded to 4 spaces for display |
| Blank lines | dropped. `a\n\nb` previews as two lines `a` and `b`, and the marker says **2** — the count is of non-empty lines, i.e. of lines the user will actually paste |
| Leading blank lines | dropped, so the preview always starts at the first real line |
| Leading indentation | kept, up to 8 columns — a code block must still read as a code block |
| Interior whitespace | single-line payloads keep it verbatim; wrapped payloads collapse runs to one space (a wrapped line cannot preserve them anyway) |
| Column budget | every preview line is hard-clipped to **44 columns**, where a full-width character counts as 2 columns (§4) |
| Clip mark | three full stops `...`, not `…` — see §4 |
| Single-line payload | exactly one row; clipped at the budget; **no marker**, no "expand" affordance |
| Multi-line payload | first **two** non-empty lines, each clipped at the budget; `↕ n` on the right |
| Long URL | one line, clipped at the budget; the full URL is never wrapped over two rows, because that would push a whole other entry out of the panel |
| Whitespace-only payload | rendered as §2.5 — **never** as an empty row |

### 2.5 The blank entry

The planning document says a payload with `length > 0` after **no trim** is kept, so
`"   "` is a real entry. It must not render as an empty row — that reads as a rendering
bug.

```
¶ 3 个空白字符
```

* `¶` in `paper-050`, **italic** (the one italic in the system, and it is the standard
  editorial mark for "there is a character here you cannot see").
* `3 个空白字符` in `paper-500`, italic, at the **12 DIP chrome size** — the count is a
  readout about the payload, not payload.
* Copied value is still `"   "`, byte for byte. The confirmation band echoes
  `（3 个空白字符）`.

### 2.6 Empty state

History is empty and the popup still opens — the planning document requires it, and it is
the right call: a hotkey that does nothing is indistinguishable from a broken hotkey.

```
┌────────────────────────────────────────────────┐
│                                                │  16 DIP top
│  还没有记录到文本                              │  13/20 600, paper-050
│  复制一段文本后，再次打开即可选择              │  12/18 400, paper-500
│                                                │  16 DIP bottom
├────────────────────────────────────────────────┤
│  Esc 关闭                                      │  26 DIP band
└────────────────────────────────────────────────┘
```

* Copy states **what happened** and **the one action that changes it**. No apology, no
  mood, no illustration, no "Get started" button.
* `Enter` is inert in this state (planning doc §9.2).
* The footer band **drops `↑↓ 选择` and `Enter 粘贴`**, keeping only `Esc 关闭` — offering
  ↑↓ on an empty list is a lie in either direction.

### 2.7 Footer band (26 DIP)

One band, three contents, same pixels. `data-foot` selects which:

| State | Content |
| --- | --- |
| `hint` | `↑↓ 选择`, `Enter 粘贴`, `Esc 关闭` — three fixed-width cells, 70 / 90 / 60 DIP, leading-aligned at `x = 11` |
| `empty` | `Esc 关闭` |
| `confirm` | `已粘贴` in `signal-500` 600 + the echoed payload in 13 DIP mono `paper-050` + `已回到原输入位置` in 12 DIP chrome |
| `conflict` | `!` + `Alt+V` in 13 DIP mono `paper-050` + `被其他程序占用，请更换` in `caution-450` |

The hint cells are **not separated by a character.** There is no middle dot, no pipe, no
slash: the 148 DIP of leftover space between three 70–90 DIP cells *is* the separator.
Each cell is a key name in the content monospace (a key name is literal machine text)
plus an action label in the UI face.

### 2.8 Confirmation feedback

After `Enter`:

1. The chosen text is written to the clipboard and the panel closes (real app).
2. The target control receives `Ctrl+V` and briefly brightens (`t-flash`, 160 ms).
3. The panel's footer band — while it is still on screen in the prototype — swaps to the
   `confirm` content: **`已粘贴` + the first 46 characters of the payload + `已回到原输入位置`**.

The echo exists because the interesting failure of a focus-preserving paste is *silent
success*: the user pressed Enter, something happened somewhere, and they want to know
what and where before they keep typing. The band shows `已粘贴` (not a green circle, not a
checkmark badge), and any key press or 700 ms clears it.

Failure states, same band, `is-fail`:

| Condition | Band |
| --- | --- |
| Foreground window changed between open and Enter (planning doc §18) | `未粘贴` in `caution-450` + the echo + `目标窗口已变化` |
| `SendInput` refused (elevated target, §28.2) | `未粘贴` + `请手动 Ctrl+V` — the text **is** on the clipboard, and the copy says so |

### 2.9 Tray context menu

`224 DIP` wide, `rad-menu` 6, `shadow-menu`, one 4 DIP outer padding and 6/10 DIP item
padding, 1 DIP `stroke-hairline` separators at 8 DIP horizontal inset.

```
MiniClip                          ← 12/16 600, paper-050
快捷键 Alt+V                      ← 12/16 400, paper-500   (caution-450 when registration failed)
────────────────────────────
清空历史                          ← is-disabled (opacity .65) when history is empty
更换快捷键              Alt+V     ← ONLY present in the conflict state
快捷键设置              Alt+V
────────────────────────────
退出 MiniClip                     ← danger-450
```

**The menu is the fallback surface for the one failure the popup cannot report.** If
`RegisterHotKey` fails, the hotkey never fires, so there is no popup in which to show an
error — the status line under the app name and the extra `更换快捷键` item are how the
user finds out. Never present a failed registration as a working one.

Width is fixed at 224 DIP because the right column (`Alt+V`) is right-aligned and a
250+ DIP menu reads as a Win32 menu, while this is a 30-entry clipboard tool's entire
settings surface. In WPF this is a `ContextMenu` with `OverridesDefaultStyle`; the
`Alt+V` accelerator chips are `TextBlock`s, **not** `MenuItem.InputGestureText` (which
only takes a `KeyGesture` and cannot render `Alt+V` in the mono face).

### 2.10 Hotkey dialog (`快捷键设置`)

`360 DIP` wide, `rad-dialog` 12, `shadow-dialog`, 16 DIP padding. An ordinary activated
window — this is the one surface in the product where taking focus is correct.

```
设置                                             13/20 600
MiniClip 只要一个快捷键，其余保持默认。          12/16 400 paper-500
全局快捷键                                       12/16 400 paper-500
┌──────────────────────────────────────────────┐
│ Alt+V                                 未生效 │  38 DIP, rad-key 4, border 1 DIP
└──────────────────────────────────────────────┘
! 该组合已被其他程序占用，MiniClip 无法注册，请换一个组合。   12/16 caution-450
☑ 开机自动启动   登录后常驻托盘                  14×14 box radius 3
                                      [ 关闭 ] [ 保存 ]
```

Field states:

| State | Border | Value | Right label |
| --- | --- | --- | --- |
| `idle` | `hairline` | `Alt+V` | — |
| `recording` | `signal-500`, caret blinking | `Alt+` + caret | `正在录入` in `signal-500` |
| `conflict` | `caution-450` | the rejected combination | `未生效` in `caution-450` |
| `reserved` | `caution-450` | e.g. `Ctrl+Alt+Del` | `未生效` in `caution-450` |

The conflict state has **two** parts, and both are required: the amber border (you cannot
save this) and the one-line note naming the reason. Copy differs by cause:

* taken by another program → `该组合已被其他程序占用，MiniClip 无法注册，请换一个组合。`
* reserved by Windows → `Windows 保留该组合，任何程序都无法注册。`

`保存` is `aria-disabled` / `IsEnabled=False` in both amber states. Never leave a working
primary button next to a combination that cannot be registered — that is a lie the user
only discovers after a restart.

`Backspace` in the field restores `Alt+V` (the default) and the note says so when idle.

### 2.11 Conflict notice inside the popup

The popup can be opened with a **configurable** hotkey while the *default* is conflicted,
so it also carries the warning — but only in the band, never as a modal:

```
! Alt+V 被其他程序占用，请更换
```

`!` in `caution-450`, `Alt+V` in 13 DIP mono `paper-050`, the rest in 12 DIP
`caution-450`. The list above it still works normally: a conflicted hotkey is a warning
about the *next* launch, not a reason to disable the panel in front of the user.

---

## 3. Wireframe with measurements

### 3.1 Popup, default (400 × 286 DIP)

```
        x=0        14   23                              376   400
        │          │    │                                │     │
 y=0    ┌──────────┴────┴────────────────────────────────┴─────┐ ── radius 10
        │                                                        │
 y=10   │ ▌›  SELECT * FROM users;                               │  row 0  selected
        │ ┊                                                      │  40 DIP
 y=50   │    git status                                          │  row 1
        │                                                        │  40
 y=90   │    localhost:8080                                      │  row 2
        │                                                        │  40
 y=130  │    docker compose up -d                                │  row 3
        │                                                        │  40
 y=170  │    npm run build --workspace=@miniclip/core            │  row 4
        │                                                        │  40
 y=210  │    fix(clipboard): 保留原焦点，改为 WS_EX_NOAC...   ↕ 3│  row 5  multi-line
        │    候选窗口不再激活自身，避免输入法状态被重置。        │  40
 y=250  ├────────────────────────────────────────────────────────┤ ← 1 DIP stroke-hairline
        │ ↑↓ 选择      Enter 粘贴      Esc 关闭                  │  footer 26
 y=276  └────────────────────────────────────────────────────────┘ ── radius 10
        total height 276 + 10 bottom padding = 286
        │          │    │          │        │         │
        └ 3 DIP rail (x 0–3, y 10–30, radius 2)
             └ 6×6 caret at x=5 y=17, rotated 45°, 2 DIP top+right borders
                  └ 9 DIP lead-in
                       └ text column 353 DIP  (x=23 … x=376)
                                              └ 24 DIP marker column, right-aligned
```

Per-element numbers:

| Element | x | y | w | h |
| --- | --- | --- | --- | --- |
| Shell | 0 | 0 | 400 | 286 |
| List | 0 | 0 | 400 | 260 (10 + 6×40 + 10) |
| Row *n* | 0 | 10 + 40n | 400 | 40 |
| Rail (selected row) | 0 | row_y + 10 | 3 | 20 |
| Caret (selected row) | 5 | row_y + 17 | 8 | 8 (6×6 rotated) |
| Text | 23 | row_y + 10 | 353 | 40 (2×20, centred) |
| Marker | 376 | row_y + 12 | 24 | 16 |
| Footer band | 0 | 260 | 400 | 26 |
| Hint cell 1 / 2 / 3 | 11 / 81 / 171 | 265 | 70 / 90 / 60 | 16 |

The second line of a 2-line row sits at `row_y + 20`; both lines are inside the same
40 DIP row, so a two-line entry does not push the list down and the panel keeps its height
whether the selection is on a one-line or a two-line entry. **This is the single most
important structural decision in the row** — it is what makes ↑↓ feel like an IME
candidate list rather than a chat log.

### 3.2 Empty (400 × 96)

```
        ┌────────────────────────────────────────────────────────┐
 y=0    │                                                        │
 y=16   │  还没有记录到文本                                      │  13/20 600
 y=36   │  复制一段文本后，再次打开即可选择                      │  12/18 400
 y=54   │                                                        │
 y=70   ├────────────────────────────────────────────────────────┤
 y=70   │  Esc 关闭                                              │  26
 y=96   └────────────────────────────────────────────────────────┘
```

### 3.3 Panel placement near the caret (app-side)

```
                    ┌─ caret / insertion point
                    ▼
   ┌────────────────────────────┐
   │ text the user is typing    │   ← original window, keeps focus throughout
   └────────────────────────────┘
                    ┌───────────────────────────────┐
                    │ ▌› first candidate            │   ← panel top-left is offset
                    │    …                           │     (+14, +18) DIP from the caret
```

* Anchor with `GetCaretPos`/`GetGUIThreadInfo` when available; fall back to the mouse
  position (V1's stated rule).
* Clamp to the **work area of the monitor containing the anchor** (`MonitorFromPoint` +
  `GetMonitorInfo().rcWork`), flipping above the caret when the panel would cross the
  bottom edge.
* The window rect includes the 24 DIP shadow margin on every side; the *panel's* top-left
  is the coordinate the offset above applies to, not the window's.

---

## 4. The column budget (read this before writing the row template)

The text column is `400 − 14 − 9 − 24 = 353 DIP` of content box (356 nominal; the
selected ring is an **inset** stroke, so it never shrinks the box).

Measured advances at 13 DIP in Cascadia Mono on this machine:

| | DIP | em |
| --- | --- | --- |
| Latin (`M`) | 7.617 | 0.586 |
| CJK (`永`) | 13.000 | 1.000 |
| `…` U+2026 | 13.617 | 1.047 |
| `...` | 22.851 | — |

So the budget is counted in **columns**, the way a terminal counts them — full-width
characters take 2 — and it is fixed at **44 columns**:

* 44 Latin columns = **335 DIP** ≤ 353 ✅
* 22 CJK columns = **286 DIP** ≤ 353 ✅
* mixed is bounded by 44 columns ✅

Two things this buys you:

1. **A row's height never depends on its content.** No row can grow to three lines, so
   40 DIP is a guarantee rather than an average, and the panel's height is a constant.
2. **The break point is identical in the prototype and in WPF**, because it is computed
   from a count, not from a font metric that differs between Chromium and DirectWrite.

**Use `...` (three full stops), not `…`.** Cascadia Mono renders `…` with a wide advance
(1.047 em — wider than a Latin glyph and nearly a full CJK cell), which silently consumes
budget; and it is a standard mark that a font is free to omit, in which case a clipped
line ends with **no visible mark at all** — the worst possible outcome for a preview that
is supposed to be trustworthy. The prototype detects coverage at startup
(`canvas.measureText`) and falls back automatically; in WPF do the same check with
`GlyphTypeface.CharacterToGlyphMap.TryGetValue(0x2026, ...)` or simply hard-code `...`.

If the implementer prefers to clip in XAML rather than in code, `TextTrimming="CharacterEllipsis"`
on a `TextBlock` is acceptable **only** with `MaxHeight` capped at 40 DIP and
`TextWrapping="NoWrap"` for single-line rows — but then the ellipsis will be WPF's, the
break point will differ slightly from the prototype, and the CJK case can still produce a
wider line than the Latin case. The counted budget is the version that holds.

---

## 5. WPF implementation notes not visible in the prototype

1. **Cascadia Mono has no bold face.** The selected row's weight 500 in CSS is a
   variable-font axis; in WPF, asking for `FontWeight="SemiBold"` on a family without a
   bold face makes WPF *synthesise* it, which smears 13 DIP stems and changes advances.
   Either embed the variable font, or select the row with `FontWeight="Normal"` and let
   the rail + caret + slab + ring carry the state — all four already do, and the weight is
   the fifth, most expendable redundancy.
2. **`UseLayoutRounding="True"`** on the popup root, `SnapsToDevicePixels="True"` on the
   1 DIP hairlines. The row grid is all integers, so it snaps exactly; the 45°-rotated
   caret does not, which is intended (it is a shape, not a line).
3. **Do not use `ScrollViewer` for the list.** Use an `ItemsControl` inside a
   `VirtualizingStackPanel`, or simply a `Canvas`/`StackPanel` of the ≤ 6 visible rows with
   a manual `TranslateTransform`, and drive it with `selectedIndex`. A `ScrollViewer`
   brings its own `ScrollBar`, focus behaviour, mouse-wheel handling and `IsHitTestVisible`
   questions into a window that must never take focus. Scroll on selection change only —
   `ScrollIntoView` on a no-activate window is a focus incident waiting to happen.
4. **The confirmation band is drawn in the same window.** Do not implement it as a second
   `Window`/`Popup`: `Popup` creates an `HWND` and a second no-activate window is a second
   way to lose the original focus. Swap the footer band's content and keep the window.
5. **`WS_EX_NOACTIVATE` + `ShowActivated="False"` + `Topmost="True"`**, then
   `SetWindowPos(..., SWP_NOACTIVATE | SWP_SHOWWINDOW)`. Also handle `WM_MOUSEACTIVATE`
   returning `MA_NOACTIVATE` so a stray click on the panel cannot pull focus out of the
   user's editor. Mouse selection is therefore **optional** at the XAML level: hover works
   and is specified, but a click need not commit a paste.
6. **`Alt+V` and IMEs.** `Alt` is the Windows menu-key; `RegisterHotKey(hwnd, id, MOD_ALT |
   MOD_NOREPEAT, VK_V)` returns a bool — check it, and on failure raise the conflict state
   in §2.9/§2.10 immediately, not on the next click.
7. **The panel must be readable before it is pretty.** First frame < 100 ms (planning doc
   §26). Pre-create the window and its visual tree at startup, keep it hidden, and only
   move and show it on the hotkey. Creating the tree on the hotkey will miss the budget on
   a cold start.
8. **Locale.** The interface language is Chinese, consistently: the popup, the footer, the
   empty state, the tray menu and the dialog. Key names (`↑↓`, `Enter`, `Esc`, `Alt+V`) and
   payloads stay in the Latin monospace, because they are literal machine text. Do not mix
   an English string into a Chinese band — the one place this matters most is
   `已粘贴` / `未粘贴`, which must not become "Pasted".

---

## 6. Motion summary (what animates, in what order)

```
Alt+V ─────────────────────────────────────────────────────────────
  0 ms   panel tree already built, window only moved + shown
  0 ms   opacity 0 → 1, scale .98 → 1, y +2 → 0          (90 ms, ease .2,.8,.2,1)
 70 ms   selection settles (row wash, ring, rail, caret)  (70 ms)

↑ / ↓ ────────────────────────────────────────────────────────────
         selected row: background + ring + rail + caret, cross-fading between
         the two rows                                          (70 ms)

Enter ────────────────────────────────────────────────────────────
  0 ms   clipboard write, panel hidden                      (70 ms, opacity only)
  0 ms   target control brightens on paste                  (160 ms, app-side)
  release↑ target returns

Esc ──────────────────────────────────────────────────────────────
         panel hidden                                      (70 ms, opacity only)

hotkey conflict (registration failure)
         tray status line + menu item change — no animation, it is a state
```

`prefers-reduced-motion` / `SystemParameters.ClientAreaAnimation == false` → every
duration becomes 0. The panel must still appear inside 100 ms.

---

## 7. Principles — what makes this MiniClip's panel and not a generic dark popup

1. **It is a peripheral instrument.** It appears 30 cm from where the user is looking and
   must be read in a glance and ignored in the next second. Every decision favours
   *instant legibility of the payload* over expressing a brand.
2. **One rule runs through everything: payload wears the monospace, chrome wears the
   system UI face.** Command content, key names and the payload echo are Cascadia Mono;
   action labels, menu items, dialog copy and the empty-state message are Segoe UI
   Variable Text. The single exception is the italic `¶`, which is an editorial mark, not
   UI text.
3. **One device, two readings.** The rail is selection *and* position *and* (by presence
   or absence of the right-edge capsule) overflow. Nothing is drawn twice.
4. **Redundant encoding, never colour alone.** Selected = rail (shape) + caret (shape) +
   mint (hue) + tinted slab (luminance) + weight (mass). Any one of the five can fail and
   the state is still unambiguous.
5. **Cut anything the payload already says.** No kind labels (`CMD`, `URL`, `SQL` — the
   text states its own kind better than a 4-letter tag), no age column (the order *is*
   recency; row 0 is by definition the newest), no index (the rail owns it), no `→`.
6. **Two regions and a band.** Rows, and a footer band that changes content but never
   exists twice. Confirmation, conflict and the keyboard legend share the same 26 DIP.
7. **The accent is a signal, not a paint.** `signal-500` appears on exactly four things
   (rail, caret, focus ring, the word `已粘贴`) and nowhere else in the panel. It is the
   only saturated pixel on screen, which is why it can be a soft mint instead of a shout.
8. **Hierarchy from hairlines and 4 DIP steps, not from fills and 12 DIP radii.**
9. **Failure is stated, never implied.** A conflicted hotkey is visible in the tray, in
   the dialog and (when the panel can open at all) in its footer band.
10. **The panel never takes focus, so it never takes responsibility for the user's
    keystrokes.** The footer legend exists to make that boundary legible: those four keys
    and nothing else.

---

## 8. Restraint — what was deliberately removed, and why

| Removed | Why |
| --- | --- |
| Title bar, close button, border | Client brief. The panel is dismissed with `Esc`, and a border would be a second edge competing with the rim-light. |
| A permanent hairline rail on every row | My own first draft. 30 identical hairlines encode a constant and spend contrast budget for nothing. The rail exists only on the selected row. |
| A numbered index column | The number is a constant down the column; only the selection varies, and the rail already shows it. |
| `n / total` position counter in the footer | A third widget reading the same number as the rail. |
| A visible scrollbar | A `ScrollViewer` bar in a 400 DIP panel that must never take focus is three problems for one benefit. Overflow is a 2 DIP capsule; position is the rail. |
| Age / timestamp per row | 30 pieces of information nobody acts on, competing with the payload. Order is recency. |
| Kind tags (`TEXT`, `CMD`, `URL`) | The payload states its own kind more accurately. |
| Source application per row | Not recorded in V1. |
| Per-row "expand" / "copy" affordances | The whole interaction is `Enter` on the selected row. A per-row action is a second way to do the one thing the panel does. |
| A `→` on any action | The action-glyph tell. The caret is a *position* marker in the gutter, not a suffix on the text. |
| Middle-dot separators (`A · B · C`) in the footer | Replaced by three fixed-width cells whose leftover space does the separating. |
| ALL-CAPS tracked-out labels above anything | The footer legend is the only label in the panel and it is sentence case, 12 DIP, `paper-500`. |
| Monospace for chrome labels | Inverted: monospace marks *payload*, and using it for UI labels would erase the distinction the whole design rests on. |
| A toast / second window for confirmation | Confirmation reuses the footer band's pixels. A second window is a second focus hazard. |
| A full-row accent fill for selection | Shouts across the desk; this panel's job is to not interrupt. |
| Animated entrance beyond 90 ms / 2 DIP / 1.02 scale | The panel must feel *already there*. A slide-in would make it an event. |
| Mood or apology in the empty state | `还没有记录到文本` + the one action that changes it. |
| A disabled row state | V1 has no per-row disabled condition. "History is empty" is a panel state, and it gets the empty-state treatment instead. |
| Search, favourites, categories, a main window | Planning doc §3 — not in V1. |
| Illustration, icon set, brand mark in the panel | The tray icon is the only mark; inside the panel, glyphs appear only where they carry information (the caret and the `↕` multi-line marker). |

---

## 9. Anti-default review (step 2 of the process, condensed)

I wrote the plan above, then re-derived the same brief as if I had no stake in it — "a dark
popup that lists clipboard items" — and diffed the two. What actually changed:

1. **Accent + surface.** First pass was `#7FE3C0` acid-mint on `#0D0D0F` — the stock
   dark-mode look with a different hex. Changed to `#6FD3B4` on a blue-slate `#0F141B`
   whose hue matches the terminals the payload comes from, and desaturated the mint so it
   reads as signal rather than neon.
2. **A permanent rail on every row → a rail only on the selected row**, placed so its
   screen position still encodes list position.
3. **`›` appended to the selected row and `↕` on every multi-line row → cut.** The
   chevron survives only as a gutter caret; `↕` now carries a true line count and appears
   only where a payload really is multi-line.
4. **Three position widgets → one.** The `6/30` counter and the scrollbar were both
   removed in favour of the rail.
5. **`↑↓ 选择 · Enter 粘贴 · Esc 关闭` → three fixed-width cells.** The middle dot is on
   the banned list; the separator is now whitespace that is already there.
6. **An eyebrow label above the list → deleted.** The panel is rows + one band, and the
   band doubles as the confirmation surface, so feedback costs zero extra pixels.
7. **Tracked-out caps in the review chrome → sentence case** in the UI face, with
   monospace reserved for literal strings (key names, hex values).
8. **A live `3m` timestamp column → cut** (redundant with order, expensive in a 400 DIP
   panel).
9. **Shadow.** The default `0 8px 24px rgba(0,0,0,.45)` became a 1 DIP contact ring + a
   16 DIP lift, plus a 1 DIP rim-light in dark mode so the panel reads as raised from its
   own edge rather than from a black cloud.

Kept as genuine choices rather than defaults: the two-face functional split; Cascadia
**Mono** (no ligatures in a clipboard); `Microsoft YaHei UI` named in both stacks; the
rail/caret/tint/weight redundancy; the two-line row that keeps 40 DIP constant; the
minted selected slab.

---

## 10. Delivered files

| File | What it is |
| --- | --- |
| `design/index.html` | Interactive prototype. `Alt+V` / `↑` / `↓` / `Enter` / `Esc`; live toggles for empty state, light theme, hotkey conflict, 30-entry list, and target-window-changed failure. URL API: `?open=0&empty=1&theme=light&long=1&sel=N&conflict=1&fail=1`. |
| `design/states.html` | Static contact sheet, 9 states. `?theme=light` for the light token set, `?only=<name>` for one tile (`default`, `selection-middle`, `empty`, `list-30`, `multiline`, `long-line`, `hotkey-conflict`, `dialog`, `tray-menu`). |
| `design/miniclip.css` | The token system and every component. The single source of truth for this document. |
| `design/miniclip.js` | Content model (`preview`, `clipCols`, `strCols`, `echo`), row/popup/menu/dialog renderers, and `selfTest()`, which measures the pinned geometry in the live DOM. |
| `design/shots/*.png` | Contact sheet (dark and light), nine per-state captures, and four prototype captures (`prototype-open`, `prototype-empty`, `prototype-30-dark`, `prototype-light`). |
| `design/_plan.md`, `design/_critique.md` | The plan and the anti-default review this document condenses. |
| `design/_probe.html` | The measurement harness behind every number in §3 and §4 (open it in a browser; it prints its results). |
