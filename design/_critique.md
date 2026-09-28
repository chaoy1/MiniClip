# Critique of my own plan, against the anti-default mandate

I wrote the plan above, then worked the same brief as if I had no stake in it — "a dark
popup that lists clipboard items" — to see where I would have landed by reflex, and
diffed the two. What follows is what actually changed, and why. (Condensed into
`DESIGN-SPEC.md` §9.)

## Where I had landed on the generic default, and what replaced it

**1. The accent. `#7FE3C0` acid-mint on near-black → `#6FD3B4` on `#0F141B`.**
The mint itself was fine (it is not the stock green or vermilion), but I had put it on
`#0D0D0F`, which is the tinted-near-black default wearing a different hex. Changed the
surface to a blue-slate `#0F141B` whose hue matches the terminals the payload comes
from, and desaturated the mint to `#6FD3B4` so it reads as *signal*, not as *neon*.

**2. A permanent 2 DIP hairline rail in the gutter of every row — cut.**
I had written "the rail runs the full list height so position is always legible". That
is a decoration that encodes a constant: 30 identical hairlines tell the user nothing and
cost contrast budget. Replaced with a rail that exists **only on the selected row**, and
which is *placed* by the list (proportional Y, 4 DIP end-caps at the extremes) so it
still answers "where in the list am I" without drawing 30 lines.

**3. `›` appended to the selected row, and a `↕` glyph on every multi-line row — cut.**
An appended action glyph is the `→`-on-every-button tell. The chevron survives only as a
gutter **caret**, which is a different device (it is a position marker on the row's
baseline, not a suffix on the text), and the `↕` now appears **only** when the payload
actually contains ≥2 lines, where it carries the true line count. A 40-char `git status`
gets nothing.

**4. `6/30` position counter in the footer — cut.**
I had this *and* the rail *and* a scroll thumb: three widgets reading one number. Kept
only the rail. The footer is now purely the keyboard legend.

**5. `↑↓ 选择 · Enter 粘贴 · Esc 关闭` — the middle dot is on the banned list.**
Not reformatted, replaced: three equal columns, each a **key cell** (right-aligned, in
the content monospace, because a key name is literal machine text) plus a small-caps-free
action label in the UI face. No separator character anywhere.

**6. Footer as an "eyebrow".** I had a 10 DIP tracked-out label above the list. Deleted.
The popup now has exactly **two regions: rows and a footer band**, and the footer band
shows one of three things depending on state (legend / empty message / what just
happened), so confirmation feedback *reuses existing pixels* instead of adding a toast.

**7. Section labels in the control strip and contact sheet.** They were tracked-out caps.
They are now sentence case in the UI face; only `prefers-reduced-motion`-style *literal*
strings (key names, hex values) stay monospace.

**8. Live timestamp column `3m`.** I had it. Cut: order is recency, so the timestamp
carries no information the index does not already carry, and it costs 40+ DIP of payload
width on a 400 DIP panel.

**9. Shadow.** The default is `0 8px 24px rgba(0,0,0,.45)`. The brief asks for *subtle*,
so: a 1 DIP contact ring plus a 16 DIP lift at 46%, weighted downward, and in dark mode
the top edge also gets a 1 DIP `paper-050 @ 7%` rim light — the panel reads as a raised
surface from its own edge, not from a big black cloud.

## What I deliberately kept, and why it is a choice rather than the default

* **Dark, rounded, subtle shadow, 400×40** — pinned by the client brief. Followed exactly.
* **Two typefaces on a functional line.** Cascadia **Mono** (not Code — no ligatures in
  a clipboard) for payload, Segoe UI Variable Text for chrome, `Microsoft YaHei UI`
  written into *both* stacks so CJK never falls back to a GDI-linked face.
* **Rail + caret + wash + brighter text** = four redundant encodings of "selected", so it
  survives greyscale and colour-blindness.
* **Menu and dialog in the UI face; popup rows in monospace.** The split is the product's
  hierarchy: settings are language, clips are payload.
