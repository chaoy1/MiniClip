# MiniClip — 设计计划 (Step 1: plan before code)

## What MiniClip actually is (design brief in one line)

A 400 DIP panel that appears next to the text caret, is read in ~100 ms of glancing,
contains *machine text*, and — above all — **must not interrupt**. The interface is
peripheral, not central. It is closer to an IME candidate window / an on-screen OSD
than to an app.

Design consequence used everywhere below: **the popup has no "chrome", it has a
reading surface.** Anything that is not the text is a hairline or 4 DIP of air.

---

## 1. Color — core palette (dark, primary)

Six named values. Family = deep blue-slate (the colour of the terminal/minimap that
the content comes from), not "near-black".

| Name | Hex | Role |
| --- | --- | --- |
| `ink-900` | `#0F141B` | popup surface (the paper) |
| `ink-850` | `#121925` | selected row wash, menus, dialog field |
| `ink-700` | `#2C3A4C` | hairlines, scroll thumb |
| `paper-050` | `#E7EDF4` | clip text (primary content) |
| `paper-500` | `#94A5BA` | chrome text, preview 2nd line, hints |
| `signal-500` | `#6FD3B4` | SELECTION ONLY: rail, caret, focus ring, success |

Two more functional hues, deliberately **not** the accent:

| Name | Hex | Role |
| --- | --- | --- |
| `caution-450` | `#E0A94A` | hotkey conflict / registration failure |
| `danger-450` | `#E8826B` | paste failure / destructive menu item hover |

Anti-default check: the generic dark popup is `#0B0B0B`/`#111` + one acid-green or
vermilion accent. `#0F141B` is a blue-slate with real hue, so white text on it
reads as *cool paper*, and the accent is a **soft mint** (`#6FD3B4`) that belongs to
the same cool family rather than fighting it. Selection is never carried by the
accent alone (see §3).

## 2. Type — two stacks, split on a functional line

| Role | Stack | Why |
| --- | --- | --- |
| **Content** (the clip text — shell, SQL, URLs, commit messages) | `"Cascadia Mono", Consolas, "Microsoft YaHei UI", monospace` | The payload is machine text where column alignment, indentation and the identity of each character (`l/1/I`, `O/0`, `rn/m`) *are* the information. **Cascadia Mono, not Cascadia Code** — the Mono cut has no programming ligatures, so `!=`, `->`, `=>` render as the literal characters the user copied. A clipboard must never display a character that is not in the payload. |
| **Chrome** (footer hints, menu, dialog labels, buttons) | `"Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", sans-serif` | Chrome is *language*, not payload — it wants optical sizing and the platform's own UI voice, so the popup reads as a Windows instrument, not a web page. |

CJK is not left to fallback: **`Microsoft YaHei UI` is written into both stacks
explicitly.** `Cascadia Mono` has no CJK coverage, so without this the OS would drop
to a face chosen by GDI font linking and `暂无文本记录` would break weight and
vertical metrics against the Latin text around it. `Microsoft YaHei UI` is the UI
cut (not `Microsoft YaHei`, the document cut) — same family the shell uses.

Scale (DIP = device-independent pixel = WPF unit):

| Token | Size / line-height / weight / tracking | Use |
| --- | --- | --- |
| `row-content` | 13 / 20 / 400 / 0 | clip text, both lines |
| `row-content-strong` | 13 / 20 / 600 / 0 | selected row's text |
| `chrome` | 12 / 16 / 400 / 0 | footer hints, hints in dialog |
| `chrome-strong` | 12 / 16 / 600 / 0 | menu item, dialog title |
| `dialog-title` | 13 / 20 / 600 / 0 | hotkey dialog title |

No all-caps, no tracking-out, no monospace for chrome labels. Small monospace is
exactly the generic tell; the split above is functional, not decorative.

## 3. How selection is expressed — and how position is expressed

**Rejected:** the full-row accent fill (default #1), a left rail *plus* a fill
(default #2), a numbered index column.

**Chosen — the rail carries selection, the rail carries position:**

* A `3 × 20` DIP **mint rail** in a 14 DIP gutter at the row's left edge marks the
  selected row — solid, high-chroma, the only saturated pixel in the panel.
* The selected row *also* gets a `1 DIP` mint inset stroke and a shallow
  `ink-850` wash. Three redundant cues: **shape** (rail), **hue** (mint),
  **luminance** (wash + brighter text) — so it survives greyscale, deuteranopia
  and a bad laptop panel. Nothing about it is "a subtle colour shift alone".
* A `4 × 4` rotated-square **caret** on the same 20 DIP line as the rail, in the
  gutter, points at the row: it says *this one, and press Enter*.
* **Position in the list** is encoded by the rail itself, not by a second widget:
  the rail is placed inside a track that spans the *whole* list, its Y is the
  selected index's proportional position, and it morphs into 4 DIP end-caps at the
  first and last item. The rail is therefore simultaneously a selection marker and
  a scrollbar — one device, two readings, zero extra pixels.
* **Overflow** (30 entries) is confirmed by a separate `2 DIP` scroll thumb at the
  panel's right edge, shown only when content overflows.

**Numbering:** removed entirely. `1..30` in a gutter would show a *constant* for
every row — the only thing that varies is which one is selected, and the rail
already owns that axis.

## 4. Row structure — what earns its pixels

```
|<-- 14 gutter -->|<------------- 344 text ------------->|<-- 24 right -->|
[ rail ][caret]   preview line 1 (13/20 Cascadia Mono)     [ ↕ n ]  (multi only)
                  preview line 2, clamped
                                    (40 DIP total, 10 + 20 + 10)
```

* **Clip text** — the only thing the user is choosing. 13/20, one or two lines.
* **Caret + rail** — selection/position (§3). Not decoration, they are the answer
  to "where am I".
* **`↕ n`** — appears *only* on entries whose payload contains ≥2 lines (a real git
  commit message), where `n` is the true line count. A 3-line indicator anchored to
  a 2-line preview is not a label, it is the missing half of the preview: it says
  "the box is lying to you by omission". Single-line rows — including a 600-char
  URL — get **no** marker: one line can only mean one line, so a marker there would
  be pure decoration.
* **Age / timestamp — cut.** Order is recency; the 1st row is the newest by
  definition. Printing `3m` on every row is 30 pieces of information nobody acts
  on, and it competes with the payload.
* **Source app — cut.** V1 does not track it.
* **Kind labels (`TEXT`, `CMD`, `URL`) — cut.** The payload states its own kind
  more accurately than a 4-letter tag can.

## 5. Machine-text behaviour (say it, then implement it)

* `\r\n` and `\r` normalised to `\n`.
* Leading blank lines skipped, so the preview always starts at the first real line.
* Trailing whitespace per line trimmed for *display*; the copy is byte-exact.
* Runs of spaces/tabs collapse to a single space **for display**; leading
  indentation of the shown line is kept (up to 8 cols) so code blocks still read as
  code. A payload is never re-formatted on the way out.
* Single-line entries: `TextTrimming=CharacterEllipsis`, exactly one line, clipped
  at the panel edge. No `…` badge, no "expand".
* Multi-line entries: first **two** lines, second line ellipsised → this is what
  makes multi-line-ness visible without a label.
* **Blank-ish entry** (payload is whitespace or a lone `\n`): never rendered as an
  empty row. It renders `¶` in `paper-050` italic, followed by
  `1 个空白字符` / `2 个空白字符` in `paper-500`-italic at `chrome` size. The user
  learns *there is an entry here, and it is whitespace* → the only useful fact.
  (A row that looked empty would read as a rendering bug.)

## 6. Empty state

Two lines, no apology, no mood:

```
还没有记录到文本
复制一段文本后，再次打开即可选择
```

Second line states the *only* action that can change the state. The footer hint
line swaps `↑↓ 选择 · Enter 粘贴 · Esc 关闭` → `Esc 关闭`, because offering ↑↓ on
an empty list is a lie either way.

## 7. Layout concept + ASCII wireframes

Popup, default (400 × 260 = 10 + 6×40 + 10 = 260 DIP):

```
╭──────────────────────────────────────────────────────────────╮  ← 10 r
│                                                              │  ← 10 pad
│ ▌› SELECT * FROM users;                                      │  ← row 40
│  › git status                                                │
│  › docker compose up -d                                      │
│  › localhost:8080                                            │
│  › npm run build --workspace=@miniclip/core                  │
│  › https://github.com/miniclip/releases/download/v1.0.0/…    │  ▐ thumb
├──────────────────────────────────────────────────────────────┤  ← hairline
│ ↑↓ 选择   Enter 粘贴   Esc 关闭                       6/30   │  ← 26 footer
╰──────────────────────────────────────────────────────────────╯  ← 10 pad
```

Empty:

```
╭──────────────────────────────────────────────────────────────╮
│  还没有记录到文本                                            │
│  复制一段文本后，再次打开即可选择                            │
├──────────────────────────────────────────────────────────────┤
│ Esc 关闭                                                     │
╰──────────────────────────────────────────────────────────────╯
```

## 8. Motion

The popup must feel like it *was already there*, not like it flew in.
`appear 90 ms cubic-bezier(.2,.8,.2,1)` over **opacity + 0.98 scale + 2 DIP y**;
`row 70 ms`, `dismiss 70 ms opacity only`, `confirm 160 ms`. Nothing else on the
panel ever animates. All of it is dropped under `prefers-reduced-motion`.

## 9. Principles

1. The panel is a **peripheral instrument**: it must be readable in a glance and
   ignorable in the next second.
2. **Content wears the monospace; chrome wears the system UI face.** One rule,
   applied without exception.
3. **One device, two readings** (the rail = selection + position + scroll).
4. **Redundant encoding of state** (shape + hue + luminance), never colour alone.
5. **Cut anything the content already says** (kind, age, index, arrows).
6. Hairlines and 4 DIP steps, not fills and 12 DIP radii, build the hierarchy.

## 10. Light variant

Same structure and same selection language, re-derived: `surface #FFFFFF`,
`wash #EDF2F8`, `hairline #D3DCE6`, `text #131A23`, `chrome #5A6779`,
`accent #0F7F6B` (same hue family as `#6FD3B4`, darkened until it carries 4.5:1
against white for the rail, the caret and the ring). The rail stays the selection
device; the wash stays shallow; the monospace stays Cascadia Mono.
