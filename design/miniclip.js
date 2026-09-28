/* ============================================================================
   MiniClip — content model + renderers, shared by index.html and states.html.
   No framework, no build, no network.  Plain ES2019, file:// safe.
   Every geometry constant here mirrors miniclip.css and DESIGN-SPEC.md.
   ========================================================================= */
(function (global) {
  'use strict';

  var GUTTER = 14;   /* gutter width, DIP            */
  var ROW_H  = 40;   /* row height, DIP              */
  var LH     = 20;   /* content line-height, DIP     */
  var PAD    = 10;   /* list vertical padding, DIP   */
  var INDENT = 8;    /* leading spaces kept, cols    */

  /* The text column is 400 − 14 (gutter) − 9 (leading) − 24 (line marker) = 356
     DIP of content box, 353 inside the selected ring (the ring is an inset stroke,
     so it never shrinks the box).  Measured advances at 13 DIP in Cascadia Mono:
     Latin 7.617, CJK 13.000 (1 em — the CJK face is not monospaced against the
     Latin).  The budget is therefore counted in COLUMNS, the way a terminal counts
     them, not in characters: 44 Latin columns = 335 DIP, 22 CJK columns = 286 DIP.
     This is the number the WPF row template needs if it clips in code; a TextBlock
     with TextTrimming=CharacterEllipsis does not. */
  var COLS = 44;

  /* Cascadia Mono is a programming face and does not carry every punctuation glyph
     a UI wants — U+2026 HORIZONTAL ELLIPSIS is the one that matters here: with no
     glyph for it Chromium silently renders nothing, so a clipped line would end
     mid-character with no mark that it was clipped.  Checking coverage once at
     startup and falling back to three full stops keeps the mark honest.  WPF must
     do the same check (GlyphTypeface.CharacterToGlyphMap). */
  var ELLIPSIS = (function () {
    try {
      var c = document.createElement('canvas').getContext('2d');
      c.font = '13px "Cascadia Mono", Consolas, monospace';
      var w = c.measureText('\u2026').width, three = c.measureText('...').width;
      /* a missing glyph measures as zero-width or as the .notdef box */
      if (!(w > 0) || w >= three) return '...';
      return '\u2026';
    } catch (e) { return '...'; }
  })();

  /* Display columns of one character: full-width (CJK, CJK punctuation, full-width
     forms) occupy two, everything else one. */
  function charCols(code) {
    return (code >= 0x1100 && (
      code <= 0x115F ||                       /* Hangul Jamo init. */
      code === 0x2329 || code === 0x232A ||
      (code >= 0x2E80 && code <= 0xA4CF && code !== 0x303F) ||  /* CJK radicals … Yi */
      (code >= 0xAC00 && code <= 0xD7A3) ||   /* Hangul syllables */
      (code >= 0xF900 && code <= 0xFAFF) ||   /* CJK compat ideographs */
      (code >= 0xFE30 && code <= 0xFE6F) ||   /* CJK compat forms */
      (code >= 0xFF00 && code <= 0xFF60) ||   /* full-width forms */
      (code >= 0xFFE0 && code <= 0xFFE6) ||
      (code >= 0x20000 && code <= 0x3FFFD)    /* ext. B+ */
    )) ? 2 : 1;
  }

  function strCols(s) {
    var n = 0;
    for (var i = 0; i < s.length; i++) n += charCols(s.charCodeAt(i));
    return n;
  }

  /* Hard-clip a display line to the column budget, ending on a real ellipsis. */
  function clipCols(s, budget) {
    if (strCols(s) <= budget) return s;
    var ell = strCols(ELLIPSIS);
    var out = '', n = 0;
    for (var i = 0; i < s.length; i++) {
      var c = charCols(s.charCodeAt(i));
      if (n + c > budget - ell) break;
      out += s[i]; n += c;
    }
    return trimEnd(out) + ELLIPSIS;
  }


  /* ---------------------------------------------------------------- data -- */
  /* Real payloads: shell, SQL, a URL, a multi-line commit message, a
     whitespace-only entry, an over-long single line. */
  var BASE = [
    'SELECT * FROM users;',
    'git status',
    'localhost:8080',
    'docker compose up -d',
    'npm run build --workspace=@miniclip/core',
    'fix(clipboard): 保留原焦点，改为 WS_EX_NOACTIVATE\n\n候选窗口不再激活自身，避免输入法状态被重置。\nEnter 前复核前台窗口，目标变化时取消粘贴。',
    'https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles#ws_ex_noactivate-and-swp_noactivate-are-the-documented-basis-for-a-window-that-shows-without-activating-itself',
    '   ',
    'git log --oneline --graph --decorate --all --since="2 weeks ago" --author="zhou" --pretty=format:"%h %ad %s" --date=short',
    'C:\\Users\\User\\AppData\\Local\\MiniClip\\history.json'
  ];

  var FILLER = [
    'npm run dev:web',
    'docker ps --format "table {{.Names}}\\t{{.Status}}"',
    'ssh deploy@10.0.12.7 -p 2222',
    'SELECT id, name FROM projects WHERE archived = 0 ORDER BY updated_at DESC LIMIT 50;',
    'git commit --amend --no-edit',
    'curl -sSL https://get.example.com/install.sh | sh',
    'kubectl -n staging rollout restart deploy/api',
    'C:\\Program Files\\dotnet\\dotnet.exe publish -c Release -r win-x64 --self-contained false',
    'reg add HKCU\\Software\\MiniClip /v Hotkey /t REG_SZ /d "Alt+V" /f',
    'Get-Process | Sort-Object WS -Descending | Select-Object -First 10',
    'docker compose logs -f --tail=200 api',
    'EXPLAIN ANALYZE SELECT count(*) FROM events WHERE created_at > now() - interval \'7 days\';',
    'git rebase --interactive origin/main',
    '127.0.0.1:43120',
    'taskkill /PID 8412 /T /F',
    '{\n  "hotkey": "Alt+V",\n  "maxItems": 30\n}',
    'npm ci --prefer-offline --no-audit',
    'SELECT * FROM users WHERE email LIKE \'%@example.com\';',
    'dotnet build -c Release /p:Platform=x64',
    'git stash push -u -m "wip: popup rail"'
  ];

  function historyFull() {
    return BASE.concat(FILLER).slice(0, 30).map(function (t, i) {
      return { id: i, text: t };
    });
  }
  function historyShort() { return BASE.slice(0, 6).map(function (t, i) { return { id: i, text: t }; }); }

  /* ------------------------------------------------------------ payload -- */
  function normalize(text) {
    return String(text == null ? '' : text)
      .replace(/\r\n?/g, '\n')
      .replace(/\t/g, '    ');
  }

  /* Non-empty logical lines: the count shown by the ↕ marker must be the number of
     lines the user will actually paste, not the number of newline bytes. */
  function lineCount(text) {
    return normalize(text).split('\n').filter(function (l) { return l.trim() !== ''; }).length;
  }

  function blankCount(text) { return normalize(text).length; }

  /* Display form. Never mutates the payload: trimming and space-collapsing are
     display-only, and the copied text stays byte-exact. */
  function trimEnd(l) { return l.replace(/[ \t]+$/, ''); }

  function preview(text) {
    var raw = normalize(text).split('\n');
    /* blank lines inside the payload are not content and not breaks — 30 rows of
       them would waste the panel.  Interior blanks are dropped, not rendered. */
    var ne = raw.filter(function (l) { return l.trim() !== ''; });

    if (!ne.length) {                                                   /* whitespace-only clip */
      var n = blankCount(text);
      return { kind: 'blank', lines: ['¶'], badge: n + '\u00A0个空白字符', total: 1 };
    }

    var multiline = ne.length > 1;
    var body = ne.slice(0, 2).map(function (l) {
      var lead = (l.match(/^ */) || [''])[0].slice(0, INDENT);
      var rest = collapse(l.slice(lead.length), multiline);
      return clipCols(lead + rest, COLS);
    });

    return { kind: multiline ? 'multi' : 'single', lines: body, badge: null, total: ne.length };
  }

  /* Whitespace inside a line is not payload — it is the gap between words — so it
     collapses for display. A single-line payload keeps its interior spacing as a
     faithful read of what will be pasted; a wrapped one cannot preserve it anyway,
     so it normalises like prose. Leading indentation is handled by the caller. */
  function collapse(s, multiline) {
    return multiline ? s.replace(/[ \t]{2,}/g, ' ') : s;
  }

  /* The echo shown after Enter: enough of the payload for the user to recognise
     what they just pasted, never the whole thing. */
  function echo(text) {
    var norm = normalize(text);
    if (norm.trim() === '') return '（' + blankCount(text) + '\u00A0个空白字符）';
    var ls = norm.split('\n');
    var first = trimEnd(ls[0]);
    if (!first) first = '(空行)';
    if (first.length > 46) first = first.slice(0, 46) + ELLIPSIS;
    return ls.length > 1 ? first + ' …' : first;
  }

  function esc(s) {
    return String(s).replace(/[&<>"]/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
    });
  }

  /* The payload's own line structure is the one thing a row must not silently
     rewrite: `<br>` for the break, and a `.mc-ln` span whose `white-space` is set
     by CSS so indentation and run-lengths survive exactly as preview() computed
     them.  In a wrapped row the browser must stay free to break inside a long
     token, so `pre` is applied only to the single-line case. */
  function bodyHtml(lines) {
    return lines.map(function (l) {
      return '<span class="mc-ln">' + esc(l) + '</span>';
    }).join('<br>');
  }

  /* -------------------------------------------------------------- pieces -- */
  var ICON_STACK =
    '<svg width="8" height="10" viewBox="0 0 8 10" aria-hidden="true">' +
    '<path d="M2.2 1.4 L2.2 8.6 M0.9 7.3 L2.2 8.6 L3.5 7.3" fill="none" ' +
    'stroke="currentColor" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round"/>' +
    '<path d="M5.8 8.6 L5.8 1.4 M4.5 2.7 L5.8 1.4 L7.1 2.7" fill="none" ' +
    'stroke="currentColor" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round"/>' +
    '</svg>';

  function rowHtml(item, index, selected) {
    var p = preview(item.text);
    var cls = 'mc-row' + (selected ? ' is-selected' : '');
    var txtCls = 'mc-text' + (p.kind === 'multi' ? ' is-multiline' : ' is-oneline');
    var mark = p.kind === 'multi' ? ICON_STACK : '';
    var count = p.kind === 'multi' ? String(p.total) : '';

    var railY = selected ? railPosition(index) : PAD;
    var body;

    if (p.kind === 'blank') {
      body = '<span class="mc-blank">¶</span><span class="mc-badge">' + esc(p.badge) + '</span>';
    } else {
      body = bodyHtml(p.lines);
    }

    var right = p.kind === 'multi'
      ? '<span class="mc-lines" title="共 ' + p.total + ' 行">' + ICON_STACK +
        '<span>' + p.total + '</span></span>'
      : '<span class="mc-lines"></span>';

    return '<div class="' + cls + '" role="option" aria-selected="' + (selected ? 'true' : 'false') +
      '" data-i="' + index + '" data-rail-y="' + railY + '">' +
      '<span class="mc-rail" aria-hidden="true"></span>' +
      '<span class="mc-caret" aria-hidden="true"></span>' +
      '<span class="' + txtCls + '">' + body + '</span>' +
      right +
      '</div>';
  }

  /* Horizontal scroll thumb geometry for a list of `n` items showing `rows`.
     The rail says which row; this says how much of the list is on screen. */
  function railPosition(index, rows) {
    rows = rows || 6;
    if (index <= 0) return PAD + 10;
    if (index >= rows - 1) return PAD + (rows - 1) * ROW_H + 10;
    return PAD + index * ROW_H + 10;
  }

  /* --------------------------------------------------------------- popup -- */
  /* opts: { items, selected, rows=6, foot='hint'|'empty'|'confirm'|'conflict',
            echo, fail, msg, hotkey, delay }
     Only the active band is emitted, so a state can never be composited from two
     half-hidden ones. */
  function popupHtml(opts) {
    opts = opts || {};
    var items = opts.items || [];
    var rows = opts.rows || 6;
    var sel = opts.selected || 0;
    var foot = opts.foot || (items.length ? 'hint' : 'empty');
    var scrollable = items.length > rows;
    var delay = opts.delay == null ? 0 : opts.delay;

    var head;
    if (!items.length) {
      head = '<div class="mc-empty">' +
        '<h3>还没有记录到文本</h3>' +
        '<p>复制一段文本后，再次打开即可选择</p>' +
        '</div>';
    } else {
      head = '<div class="mc-list' + (scrollable ? ' is-scrollable' : '') + '" role="listbox" ' +
        'aria-label="剪贴板历史" style="max-height:' + (rows * ROW_H + PAD * 2) + 'px">' +
        items.map(function (it, i) { return rowHtml(it, i, i === sel); }).join('') +
        '</div>';
    }

    var band;
    if (foot === 'empty') {
      band = '<div class="mc-foot-empty">Esc 关闭</div>';
    } else if (foot === 'confirm') {
      band = '<div class="mc-foot-confirm' + (opts.fail ? ' is-fail' : '') + '">' +
        '<span class="mc-ok">' + (opts.fail ? '未粘贴' : '已粘贴') + '</span>' +
        '<span class="mc-echo">' + esc(opts.echo || '') + '</span>' +
        '<span class="mc-tail">' + esc(opts.msg || (opts.fail ? '' : '已回到原输入位置')) + '</span>' +
        '</div>';
    } else if (foot === 'conflict') {
      band = '<div class="mc-notice">' +
        '<span class="mc-mark">!</span>' +
        '<span class="mc-echo">' + esc(opts.hotkey || 'Alt+V') + '</span>' +
        '<span class="mc-tail">' + esc(opts.msg || '被其他程序占用，请更换') + '</span>' +
        '</div>';
    } else {
      band = '<div class="mc-foot-hint">' +
        '<span class="mc-key"><b>↑↓</b><span>选择</span></span>' +
        '<span class="mc-key"><b>Enter</b><span>粘贴</span></span>' +
        '<span class="mc-key"><b>Esc</b><span>关闭</span></span>' +
        '</div>';
    }

    return '<div class="mc-popup" role="dialog" aria-label="MiniClip 剪贴板候选" ' +
      'style="--mc-delay:' + delay + 'ms">' + head +
      '<div class="mc-foot" data-foot="' + foot + '">' + band + '</div>' +
      '</div>';
  }

  /* ---------------------------------------------------------------- menu -- */
  /* opts: { conflict:bool, hotkey:string, hasHistory:bool, delay } */
  function menuHtml(opts) {
    opts = opts || {};
    var hk = opts.hotkey || 'Alt+V';
    var delay = opts.delay == null ? 0 : opts.delay;
    var status = opts.conflict
      ? '<span class="mc-status is-warn">快捷键 ' + esc(hk) + ' 注册失败</span>'
      : '<span class="mc-status">快捷键 ' + esc(hk) + '</span>';

    var items =
      '<div class="mc-mi' + (opts.hasHistory ? '' : ' is-disabled') + '">' +
      '<span>清空历史</span></div>' +
      (opts.conflict
        ? '<div class="mc-mi"><span class="mc-dot" style="color:var(--mc-caution)"></span>' +
          '<span>更换快捷键</span><span class="mc-mi-hint">' + esc(hk) + '</span></div>'
        : '') +
      '<div class="mc-mi' + (opts.conflict ? ' is-hover' : '') + '">' +
      '<span>快捷键设置</span><span class="mc-mi-hint">' + esc(hk) + '</span></div>' +
      '<div class="mc-sep"></div>' +
      '<div class="mc-mi is-danger' + (opts.conflict ? '' : ' is-hover') + '">' +
      '<span>退出 MiniClip</span></div>';

    return '<div class="mc-menu" role="menu" aria-label="MiniClip 托盘菜单" ' +
      'style="--mc-delay:' + delay + 'ms">' +
      '<div class="mc-menu-head"><span class="mc-name">MiniClip</span>' + status + '</div>' +
      '<div class="mc-sep"></div>' + items + '</div>';
  }

  /* -------------------------------------------------------------- dialog -- */
  /* opts: { state:'idle'|'recording'|'conflict'|'reserved', hotkey, autostart, delay } */
  function dialogHtml(opts) {
    opts = opts || {};
    var state = opts.state || 'idle';
    var hk = opts.hotkey || 'Alt+V';
    var delay = opts.delay == null ? 0 : opts.delay;

    var fieldCls = 'mc-record' + (state === 'recording' ? ' is-recording'
      : (state === 'conflict' || state === 'reserved') ? ' is-conflict' : '');
    var fieldState = state === 'recording' ? '正在录入'
      : (state === 'conflict' || state === 'reserved') ? '未生效' : '';
    var val = state === 'recording'
      ? '<span class="mc-record-val">Alt+</span><span class="mc-cursor"></span>'
      : '<span class="mc-record-val">' + esc(hk) + '</span>';

    var note = '';
    if (state === 'conflict') {
      note = '<div class="mc-inline-note"><span class="mc-mark">!</span>' +
        '<span>该组合已被其他程序占用，MiniClip 无法注册，请换一个组合。</span></div>';
    } else if (state === 'reserved') {
      note = '<div class="mc-inline-note"><span class="mc-mark">!</span>' +
        '<span>Windows 保留该组合，任何程序都无法注册。</span></div>';
    } else if (state === 'recording') {
      note = '<div class="mc-inline-note is-ok"><span class="mc-mark">·</span>' +
        '<span>按下组合键完成录入，Esc 取消。</span></div>';
    } else {
      note = '<div class="mc-inline-note is-ok"><span class="mc-mark">·</span>' +
        '<span>点击输入框可重新录入，恢复默认请按 Backspace。</span></div>';
    }

    var primaryCls = 'mc-btn is-primary' + ((state === 'conflict' || state === 'reserved')
      ? ' is-blocked' : '');

    return '<div class="mc-dialog" role="dialog" aria-modal="true" aria-label="MiniClip 设置" ' +
      'style="--mc-delay:' + delay + 'ms">' +
      '<h2>设置</h2>' +
      '<p class="mc-sub">MiniClip 只要一个快捷键，其余保持默认。</p>' +
      '<span class="mc-field-label">全局快捷键</span>' +
      '<div class="' + fieldCls + '" role="button" tabindex="0" aria-label="全局快捷键，' + esc(hk) + '">' +
      val +
      '<span class="mc-record-state">' + fieldState + '</span>' +
      '</div>' + note +
      '<div class="mc-check' + (opts.autostart ? ' is-on' : '') + '" role="checkbox" tabindex="0" ' +
      'aria-checked="' + (opts.autostart ? 'true' : 'false') + '">' +
      '<span class="mc-box" aria-hidden="true"></span>' +
      '<span>开机自动启动</span>' +
      '<span class="mc-hint">登录后常驻托盘</span>' +
      '</div>' +
      '<div class="mc-actions">' +
      '<span class="mc-btn">关闭</span>' +
      '<span class="' + primaryCls + '" aria-disabled="' +
      ((state === 'conflict' || state === 'reserved') ? 'true' : 'false') + '">保存</span>' +
      '</div></div>';
  }

  /* ------------------------------------------------------------ self-test -- */
  /* Verifies the two numbers the client pinned, in the live DOM.
     Everything is normalised against a 100 DIP calibration box first, because a
     screenshot host can lay the page out at a scale other than 1. */
  function selfTest(root) {
    root = root || document;
    var cal = document.createElement('div');
    cal.style.cssText = 'position:absolute;top:-9999px;width:100px;height:100px';
    document.body.appendChild(cal);
    var k = cal.getBoundingClientRect().width / 100;
    document.body.removeChild(cal);

    function px(v) { return Math.round(v / k * 10) / 10; }
    var out = [];

    var popup = root.querySelector('.mc-popup');
    if (popup) {
      out.push(['popup layout width', getComputedStyle(popup).width, '400px']);
      out.push(['popup painted width', px(popup.getBoundingClientRect().width) + 'px', '400px']);
    }

    var rows = root.querySelectorAll('.mc-popup .mc-row');
    for (var i = 0; i < Math.min(rows.length, 3); i++) {
      out.push(['row ' + i + ' layout height', getComputedStyle(rows[i]).height, '40px']);
    }
    if (rows.length) {
      out.push(['rows in DOM', String(rows.length), rows.length > 6 ? 'scrolls' : 'all visible']);
    }
    var scroller = root.querySelector('.mc-list');
    if (scroller) {
      out.push(['list clientHeight', px(scroller.clientHeight) + 'px', '<= 260']);
      out.push(['list scrollHeight', px(scroller.scrollHeight) + 'px',
        scroller.scrollHeight > scroller.clientHeight ? 'overflows' : 'no overflow']);
    }
    var d = root.querySelector('.mc-dialog');
    if (d) out.push(['dialog width', getComputedStyle(d).width, '360px']);
    var m = root.querySelector('.mc-menu');
    if (m) out.push(['menu width', getComputedStyle(m).width, '224px']);
    if (Math.abs(k - 1) > 0.002) {
      out.push(['host paint scale', k.toFixed(4), 'Layout is CSS-exact; measurements are normalised by this factor']);
    }
    return out;
  }

  global.MC = {
    GUTTER: GUTTER, ROW_H: ROW_H, LH: LH, PAD: PAD,
    BASE: BASE,
    historyShort: historyShort, historyFull: historyFull,
    normalize: normalize, preview: preview, lineCount: lineCount, echo: echo, esc: esc,
    strCols: strCols, clipCols: clipCols, COLS: COLS,
    rowHtml: rowHtml, popupHtml: popupHtml, menuHtml: menuHtml, dialogHtml: dialogHtml,
    railPosition: railPosition, selfTest: selfTest
  };
})(window);
