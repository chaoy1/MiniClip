// -----------------------------------------------------------------------------
//  MiniClip - Windows minimalist text-clipboard tool
//
//  Input/HotkeyGesture.cs
//  Pure model, parser and display formatting for a global hotkey gesture.
//
//  This file deliberately contains no P/Invoke, no Win32 interop and no I/O.
//  The caller owns registration (RegisterHotKey / UnregisterHotKey) and needs
//  only the virtual-key code and the MOD_* bits that are exposed here.
//
//  Design notes
//  ------------
//  * HotkeyModifiers values are bit-identical to the Win32 MOD_* constants, so
//    HotkeyGesture.Win32Modifiers can be handed straight to RegisterHotKey.
//  * A gesture is stored as (modifier flags, Win32 virtual-key code). WPF types
//    appear only in the two thin adapters FromWpf / ToWpf.
//  * Only Parse throws (FormatException, by contract). GetName, TryParse,
//    TryParseName and every property getter are total functions: no input can
//    make them throw.
// -----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;
using System.Windows.Input;

namespace MiniClip.Input;

/// <summary>
/// Modifier-key flags for a global hotkey. The numeric values are the Win32
/// MOD_* constants so that they can be passed to RegisterHotKey unchanged.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    /// <summary>No modifier key.</summary>
    None = 0x0000,

    /// <summary>Win32 MOD_ALT.</summary>
    Alt = 0x0001,

    /// <summary>Win32 MOD_CONTROL.</summary>
    Ctrl = 0x0002,

    /// <summary>Win32 MOD_SHIFT.</summary>
    Shift = 0x0004,

    /// <summary>Win32 MOD_WIN.</summary>
    Win = 0x0008,

    /// <summary>
    /// Win32 MOD_NOREPEAT: while the key is held down the hotkey fires only once.
    /// This flag is owned by the caller, not by the gesture's identity; see
    /// <see cref="HotkeyGesture.Win32Modifiers"/>.
    /// </summary>
    NoRepeat = 0x4000,
}

/// <summary>
/// An immutable global hotkey: a set of modifier keys plus exactly one Win32
/// virtual-key code.
/// </summary>
/// <param name="Modifiers">
/// The modifier keys, as Win32 MOD_* flags. May carry <see cref="HotkeyModifiers.NoRepeat"/>,
/// which is ignored by equality and by every formatting member.
/// </param>
/// <param name="VirtualKey">
/// The Win32 virtual-key code of the single key, for example 0x56 for V. Zero means
/// "no key", which is never <see cref="IsValid"/>.
/// </param>
/// <remarks>
/// <para>
/// The type is pure data plus parsing and formatting so that it can be tested
/// without a message loop, a window or the registry.
/// </para>
/// <para>
/// Identity: <see cref="HotkeyModifiers.NoRepeat"/> is a transport flag for
/// RegisterHotKey rather than part of "which key combination is this", so
/// <see cref="Equals(HotkeyGesture)"/> and <see cref="GetHashCode"/> mask it out,
/// and no formatting member ever prints it. A record struct synthesizes
/// <c>Equals</c> over all instance fields, which would have made
/// <c>Alt+V</c> and <c>Alt+V|NoRepeat</c> unequal, so the two equality members are
/// declared explicitly here. They cannot be declared as operators:
/// on a record struct it is a compile-time error to declare <c>operator ==</c>,
/// <c>operator !=</c> or <c>Equals(object)</c>, and the synthesized <c>==</c> is
/// defined in terms of <see cref="Equals(HotkeyGesture)"/>, so it automatically
/// follows the same rule.
/// </para>
/// <para>
/// Because the struct is immutable, the caller tags a gesture with NoRepeat via
/// <c>gesture with { Modifiers = gesture.Modifiers | HotkeyModifiers.NoRepeat }</c>.
/// </para>
/// </remarks>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>Modifier bits that Win32 itself understands.</summary>
    private const HotkeyModifiers Win32ModifierMask =
        HotkeyModifiers.Alt | HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    /// <summary>Every flag this type defines. Any other bit is a caller defect.</summary>
    private const HotkeyModifiers KnownModifierMask = Win32ModifierMask | HotkeyModifiers.NoRepeat;

    /// <summary>Hard limit on the raw input length accepted by <see cref="TryParse"/>.</summary>
    private const int MaxRawTextLength = 256;

    /// <summary>Hard limit on the input length once whitespace has been removed.</summary>
    private const int MaxCondensedLength = 64;

    /// <summary>Virtual-key code of the V key (0x56).</summary>
    private const uint VirtualKeyV = 0x56;

    /// <summary>
    /// The hotkey MiniClip uses when the user has not configured one: Alt + V.
    /// Always valid (<see cref="IsValid"/> is <see langword="true"/>).
    /// </summary>
    public static HotkeyGesture Default { get; } = new HotkeyGesture(HotkeyModifiers.Alt, VirtualKeyV);

    /// <summary>
    /// The modifier bits to pass to RegisterHotKey: <see cref="Modifiers"/> masked to
    /// Alt, Ctrl, Shift and Win and cast to <c>uint</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="HotkeyModifiers.NoRepeat"/> is intentionally not included, because it is
    /// a caller-owned flag and not part of the gesture's identity. A caller that wants
    /// MOD_NOREPEAT must OR it in explicitly, for example
    /// <c>(uint)(gesture.Modifiers | HotkeyModifiers.NoRepeat)</c> or
    /// <c>gesture.Win32Modifiers | (uint)HotkeyModifiers.NoRepeat</c>.
    /// </remarks>
    public uint Win32Modifiers => (uint)NormalizedModifiers;

    /// <summary>Modifiers reduced to the four real modifier keys.</summary>
    private HotkeyModifiers NormalizedModifiers => Modifiers & Win32ModifierMask;

    /// <summary>
    /// Whether MiniClip may register this gesture as a global hotkey.
    /// </summary>
    /// <remarks>
    /// <para>The gesture is valid when all of the following hold:</para>
    /// <list type="bullet">
    /// <item><description>the key is supported: A-Z, 0-9, F1-F24 or a named key such as Space, Enter, Delete or Minus;</description></item>
    /// <item><description>no undefined modifier bit is set;</description></item>
    /// <item><description>at least one of Ctrl, Alt, Shift or Win is present, so that MiniClip never swallows plain typing;</description></item>
    /// <item><description>Shift is not the only modifier (it breaks normal typing);</description></item>
    /// <item><description>Win is not the only modifier (Win + key collides with Windows shell shortcuts), so Win+Ctrl+X, Win+Alt+X and Win+Shift+X are accepted while Win+X is not;</description></item>
    /// <item><description>the combination is not reserved by Windows: Alt+F4, Alt+Tab, Alt+Esc, Alt+Space, Ctrl+Escape, Ctrl+Alt+Delete, Ctrl+Alt+Esc, Ctrl+Shift+Escape, Win+L, Win+D or Win+Tab. The modifier set is compared as a set, so the order the user typed does not matter, and an extra modifier makes the gesture acceptable again (for example Ctrl+Alt+Shift+Delete is allowed).</description></item>
    /// </list>
    /// </remarks>
    public bool IsValid
    {
        get
        {
            if (VirtualKey == 0) return false;
            if ((Modifiers & ~KnownModifierMask) != HotkeyModifiers.None) return false;
            if (!VirtualKeyNames.IsSupportedKey(VirtualKey)) return false;

            HotkeyModifiers modifiers = NormalizedModifiers;
            if (modifiers == HotkeyModifiers.None) return false;
            if (modifiers == HotkeyModifiers.Shift) return false;
            if (modifiers == HotkeyModifiers.Win) return false;

            return !IsReservedCombination(modifiers, VirtualKey);
        }
    }

    /// <summary>
    /// Parses a hotkey such as <c>"Alt+V"</c>, <c>"alt + v"</c>, <c>"V+Alt"</c>,
    /// <c>"Ctrl+Shift+F5"</c> or <c>"Win+Space"</c>.
    /// </summary>
    /// <param name="text">Text to parse. Null, empty and unknown tokens are rejected.</param>
    /// <param name="gesture">
    /// The parsed gesture, or <c>default</c> when parsing fails. Parsing is
    /// syntactic only; it does not apply the <see cref="IsValid"/> rules, so a
    /// syntactically correct but unsafe gesture such as <c>"F5"</c> parses successfully
    /// and must be rejected by the caller through <see cref="IsValid"/>.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a hotkey; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Parsing is case-insensitive and whitespace-tolerant, modifiers may appear in any
    /// order and may follow the key, and modifier aliases (Control, Windows, Meta) are
    /// accepted. This member never throws.
    /// </remarks>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;

        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > MaxRawTextLength) return false;

        string condensed = RemoveWhitespace(text);
        if (condensed.Length == 0 || condensed.Length > MaxCondensedLength) return false;

        // '+' is both the token separator and the name of a key, so "Ctrl++" (Ctrl plus the
        // '+' key) would otherwise split into an empty token. Rewrite a trailing "++".
        if (condensed.EndsWith("++", StringComparison.Ordinal))
        {
            condensed = condensed.Substring(0, condensed.Length - 2) + "+Plus";
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;

        foreach (string token in condensed.Split('+'))
        {
            if (token.Length == 0) return false;

            HotkeyModifiers modifier = ParseModifierToken(token);
            if (modifier != HotkeyModifiers.None)
            {
                modifiers |= modifier;
                continue;
            }

            // Not a modifier, so it must be the one and only key.
            if (virtualKey != 0) return false;
            if (!VirtualKeyNames.TryParseName(token, out virtualKey)) return false;
        }

        // Modifiers with no key, for example "Ctrl+Shift".
        if (virtualKey == 0) return false;

        gesture = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }

    /// <summary>
    /// Parses a hotkey such as <c>"Alt+V"</c>, using the same syntax as
    /// <see cref="TryParse"/>.
    /// </summary>
    /// <param name="text">Text to parse. A null value fails parsing like any other invalid input.</param>
    /// <returns>The parsed gesture.</returns>
    /// <exception cref="FormatException">
    /// <paramref name="text"/> is null, empty or not a hotkey gesture.
    /// </exception>
    public static HotkeyGesture Parse(string text)
    {
        if (!TryParse(text, out HotkeyGesture gesture))
        {
            throw new FormatException(
                $"'{text}' is not a valid hotkey gesture. Use modifiers (Ctrl, Alt, Shift, Win) " +
                "followed by a key, for example 'Ctrl+Shift+V', 'Alt+V', 'Win+Ctrl+Space' or 'F5'.");
        }

        return gesture;
    }

    /// <summary>
    /// Formats the gesture for storage, using the fixed modifier order
    /// Ctrl, Alt, Shift, Win and an uppercase key name, for example <c>"Ctrl+Alt+V"</c>.
    /// </summary>
    /// <returns>
    /// A canonical string. The result of a valid gesture round-trips through
    /// <see cref="TryParse"/>; a gesture that has no key formats as the
    /// <c>VK_0x00</c> placeholder and does not round-trip.
    /// </returns>
    public string ToCanonicalString()
    {
        var builder = new StringBuilder(24);
        AppendModifierNames(builder, "+", "Ctrl");
        AppendKeyName(builder, "+");
        return builder.ToString();
    }

    /// <summary>
    /// Formats the gesture for the user interface, for example <c>"Ctrl + Alt + V"</c>.
    /// </summary>
    /// <returns>A human-readable string; never null.</returns>
    public string ToDisplayString()
    {
        var builder = new StringBuilder(24);
        AppendModifierNames(builder, " + ", "Ctrl");
        AppendKeyName(builder, " + ");
        return builder.ToString();
    }

    /// <summary>
    /// Formats the gesture for the Chinese-language settings dialog, for example
    /// <c>"控制 + Alt + V"</c>.
    /// </summary>
    /// <returns>
    /// The same string as <see cref="ToDisplayString"/> except that Ctrl is rendered as
    /// <c>控制</c>. Key names stay in their Latin form because they are printed on the
    /// physical keycaps and because that keeps a single key-name table authoritative.
    /// </returns>
    public string ToChineseDisplayString()
    {
        var builder = new StringBuilder(24);
        AppendModifierNames(builder, " + ", "控制");
        AppendKeyName(builder, " + ");
        return builder.ToString();
    }

    /// <summary>Returns <see cref="ToCanonicalString"/>, so logs and debugging show the storage form.</summary>
    /// <returns>The canonical string.</returns>
    public override string ToString() => ToCanonicalString();

    /// <summary>
    /// Determines whether two gestures describe the same hotkey, ignoring
    /// <see cref="HotkeyModifiers.NoRepeat"/>.
    /// </summary>
    /// <param name="other">The gesture to compare with.</param>
    /// <returns><see langword="true"/> when modifiers and key match.</returns>
    public bool Equals(HotkeyGesture other) =>
        NormalizedModifiers == other.NormalizedModifiers && VirtualKey == other.VirtualKey;

    /// <summary>
    /// Returns a hash code consistent with <see cref="Equals(HotkeyGesture)"/>:
    /// gestures that differ only in <see cref="HotkeyModifiers.NoRepeat"/> hash alike.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine((uint)NormalizedModifiers, VirtualKey);

    /// <summary>
    /// Creates a gesture from WPF input state, for example while the settings dialog
    /// records a new shortcut.
    /// </summary>
    /// <param name="modifiers">Currently pressed modifier keys, typically <c>Keyboard.Modifiers</c>.</param>
    /// <param name="key">
    /// The pressed key. When Alt is held WPF reports <see cref="Key.System"/> in
    /// <c>KeyEventArgs.Key</c> and the real key in <c>KeyEventArgs.SystemKey</c>; the caller
    /// must pass the latter, otherwise this method returns a gesture with a zero key.
    /// </param>
    /// <returns>
    /// The gesture. Unsupported keys produce a gesture whose <see cref="VirtualKey"/> is zero
    /// and whose <see cref="IsValid"/> is <see langword="false"/>.
    /// </returns>
    public static HotkeyGesture FromWpf(ModifierKeys modifiers, Key key)
    {
        HotkeyModifiers result = HotkeyModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0) result |= HotkeyModifiers.Ctrl;
        if ((modifiers & ModifierKeys.Alt) != 0) result |= HotkeyModifiers.Alt;
        if ((modifiers & ModifierKeys.Shift) != 0) result |= HotkeyModifiers.Shift;
        if ((modifiers & ModifierKeys.Windows) != 0) result |= HotkeyModifiers.Win;

        return new HotkeyGesture(result, VirtualKeyFromWpfKey(key));
    }

    /// <summary>
    /// Converts the gesture back to WPF input types, for example to test whether a
    /// <c>KeyEventArgs</c> matches the configured hotkey.
    /// </summary>
    /// <returns>
    /// The modifier set and the key. The key is <see cref="Key.None"/> when
    /// <see cref="VirtualKey"/> has no WPF equivalent.
    /// </returns>
    public (ModifierKeys Modifiers, Key Key) ToWpf()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if ((Modifiers & HotkeyModifiers.Ctrl) != 0) modifiers |= ModifierKeys.Control;
        if ((Modifiers & HotkeyModifiers.Alt) != 0) modifiers |= ModifierKeys.Alt;
        if ((Modifiers & HotkeyModifiers.Shift) != 0) modifiers |= ModifierKeys.Shift;
        if ((Modifiers & HotkeyModifiers.Win) != 0) modifiers |= ModifierKeys.Windows;

        return (modifiers, WpfKeyFromVirtualKey(VirtualKey));
    }

    /// <summary>Appends the canonical key name, preceded by its separator when needed.</summary>
    private void AppendKeyName(StringBuilder builder, string separator)
    {
        if (builder.Length > 0) builder.Append(separator);
        builder.Append(VirtualKeyNames.GetName(VirtualKey));
    }

    /// <summary>Appends the pressed modifiers in the fixed order Ctrl, Alt, Shift, Win.</summary>
    private void AppendModifierNames(StringBuilder builder, string separator, string ctrlName)
    {
        AppendModifierName(builder, HotkeyModifiers.Ctrl, ctrlName, separator);
        AppendModifierName(builder, HotkeyModifiers.Alt, "Alt", separator);
        AppendModifierName(builder, HotkeyModifiers.Shift, "Shift", separator);
        AppendModifierName(builder, HotkeyModifiers.Win, "Win", separator);
    }

    /// <summary>Appends one modifier name when that modifier is set.</summary>
    private void AppendModifierName(StringBuilder builder, HotkeyModifiers flag, string name, string separator)
    {
        if ((Modifiers & flag) == HotkeyModifiers.None) return;

        if (builder.Length > 0) builder.Append(separator);
        builder.Append(name);
    }

    /// <summary>Maps a modifier token such as <c>"control"</c> to its flag, or None when it is not a modifier.</summary>
    private static HotkeyModifiers ParseModifierToken(string token) => token.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
        "ALT" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" or "WINDOWS" or "META" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    /// <summary>Removes every whitespace character, returning the original instance when there is none.</summary>
    private static string RemoveWhitespace(string text)
    {
        int index = 0;
        while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
        if (index == text.Length) return text;

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, index);
        for (int i = index; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsWhiteSpace(c)) builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Modifier sets and keys that Windows itself reserves, or that cannot be received
    /// through RegisterHotKey. The modifier set is compared as a whole.
    /// </summary>
    private static bool IsReservedCombination(HotkeyModifiers modifiers, uint virtualKey) =>
        (modifiers, virtualKey) switch
        {
            (HotkeyModifiers.Alt, 0x73u) => true,                           // Alt+F4
            (HotkeyModifiers.Alt, 0x09u) => true,                           // Alt+Tab
            (HotkeyModifiers.Alt, 0x1Bu) => true,                           // Alt+Esc
            (HotkeyModifiers.Alt, 0x20u) => true,                           // Alt+Space
            (HotkeyModifiers.Ctrl, 0x1Bu) => true,                          // Ctrl+Esc
            (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x2Eu) => true,    // Ctrl+Alt+Delete
            (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x1Bu) => true,    // Ctrl+Alt+Esc
            (HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x1Bu) => true,  // Ctrl+Shift+Escape
            (HotkeyModifiers.Win, 0x4Cu) => true,                           // Win+L
            (HotkeyModifiers.Win, 0x44u) => true,                           // Win+D
            (HotkeyModifiers.Win, 0x09u) => true,                           // Win+Tab
            _ => false,
        };

    /// <summary>
    /// Translates a WPF key to a Win32 virtual-key code, by enum name only.
    /// </summary>
    /// <remarks>
    /// WPF numbers Key sequentially, not as virtual keys: Key.Back is 2, Key.A is 44 and
    /// Key.F1 is 90, while VK_A is 0x41 and VK_F1 is 0x70. Casting the enum would silently
    /// produce wrong codes (Key.A would become VK_SNAPSHOT), so every supported key is
    /// listed explicitly. Key aliases that share a value (Key.Enter/Key.Return,
    /// Key.PrintScreen/Key.Snapshot, ...) appear only once, which is required because a
    /// duplicated constant pattern in a switch expression does not compile.
    /// </remarks>
    private static uint VirtualKeyFromWpfKey(Key key) => key switch
    {
        // Digits and letters.
        Key.D0 => 0x30u, Key.D1 => 0x31u, Key.D2 => 0x32u, Key.D3 => 0x33u, Key.D4 => 0x34u,
        Key.D5 => 0x35u, Key.D6 => 0x36u, Key.D7 => 0x37u, Key.D8 => 0x38u, Key.D9 => 0x39u,
        Key.A => 0x41u, Key.B => 0x42u, Key.C => 0x43u, Key.D => 0x44u, Key.E => 0x45u,
        Key.F => 0x46u, Key.G => 0x47u, Key.H => 0x48u, Key.I => 0x49u, Key.J => 0x4Au,
        Key.K => 0x4Bu, Key.L => 0x4Cu, Key.M => 0x4Du, Key.N => 0x4Eu, Key.O => 0x4Fu,
        Key.P => 0x50u, Key.Q => 0x51u, Key.R => 0x52u, Key.S => 0x53u, Key.T => 0x54u,
        Key.U => 0x55u, Key.V => 0x56u, Key.W => 0x57u, Key.X => 0x58u, Key.Y => 0x59u,
        Key.Z => 0x5Au,

        // Function keys: VK_F1 = 0x70 to VK_F24 = 0x87.
        Key.F1 => 0x70u, Key.F2 => 0x71u, Key.F3 => 0x72u, Key.F4 => 0x73u, Key.F5 => 0x74u,
        Key.F6 => 0x75u, Key.F7 => 0x76u, Key.F8 => 0x77u, Key.F9 => 0x78u, Key.F10 => 0x79u,
        Key.F11 => 0x7Au, Key.F12 => 0x7Bu, Key.F13 => 0x7Cu, Key.F14 => 0x7Du, Key.F15 => 0x7Eu,
        Key.F16 => 0x7Fu, Key.F17 => 0x80u, Key.F18 => 0x81u, Key.F19 => 0x82u, Key.F20 => 0x83u,
        Key.F21 => 0x84u, Key.F22 => 0x85u, Key.F23 => 0x86u, Key.F24 => 0x87u,

        // Navigation, editing and lock keys.
        Key.Back => 0x08u,
        Key.Tab => 0x09u,
        Key.Return => 0x0Du,
        Key.Pause => 0x13u,
        Key.CapsLock => 0x14u,
        Key.Escape => 0x1Bu,
        Key.Space => 0x20u,
        Key.PageUp => 0x21u,
        Key.PageDown => 0x22u,
        Key.End => 0x23u,
        Key.Home => 0x24u,
        Key.Left => 0x25u,
        Key.Up => 0x26u,
        Key.Right => 0x27u,
        Key.Down => 0x28u,
        Key.PrintScreen => 0x2Cu,
        Key.Insert => 0x2Du,
        Key.Delete => 0x2Eu,
        Key.NumLock => 0x90u,
        Key.Scroll => 0x91u,

        // OEM punctuation, matching the VK_OEM_* layout used by Windows itself.
        Key.OemSemicolon => 0xBAu,       // VK_OEM_1
        Key.OemPlus => 0xBBu,            // VK_OEM_PLUS
        Key.OemComma => 0xBCu,           // VK_OEM_COMMA
        Key.OemMinus => 0xBDu,           // VK_OEM_MINUS
        Key.OemPeriod => 0xBEu,          // VK_OEM_PERIOD
        Key.OemQuestion => 0xBFu,        // VK_OEM_2, the slash key
        Key.OemTilde => 0xC0u,           // VK_OEM_3, the grave accent key
        Key.OemOpenBrackets => 0xDBu,    // VK_OEM_4
        Key.OemPipe => 0xDCu,            // VK_OEM_5, the backslash key
        Key.OemCloseBrackets => 0xDDu,   // VK_OEM_6
        Key.OemQuotes => 0xDEu,          // VK_OEM_7

        _ => 0u,
    };

    /// <summary>
    /// Translates a Win32 virtual-key code to a WPF key, by name only. Codes without a
    /// supported WPF key return <see cref="Key.None"/>.
    /// </summary>
    private static Key WpfKeyFromVirtualKey(uint virtualKey) => virtualKey switch
    {
        // Digits and letters.
        0x30u => Key.D0, 0x31u => Key.D1, 0x32u => Key.D2, 0x33u => Key.D3, 0x34u => Key.D4,
        0x35u => Key.D5, 0x36u => Key.D6, 0x37u => Key.D7, 0x38u => Key.D8, 0x39u => Key.D9,
        0x41u => Key.A, 0x42u => Key.B, 0x43u => Key.C, 0x44u => Key.D, 0x45u => Key.E,
        0x46u => Key.F, 0x47u => Key.G, 0x48u => Key.H, 0x49u => Key.I, 0x4Au => Key.J,
        0x4Bu => Key.K, 0x4Cu => Key.L, 0x4Du => Key.M, 0x4Eu => Key.N, 0x4Fu => Key.O,
        0x50u => Key.P, 0x51u => Key.Q, 0x52u => Key.R, 0x53u => Key.S, 0x54u => Key.T,
        0x55u => Key.U, 0x56u => Key.V, 0x57u => Key.W, 0x58u => Key.X, 0x59u => Key.Y,
        0x5Au => Key.Z,

        // Function keys.
        0x70u => Key.F1, 0x71u => Key.F2, 0x72u => Key.F3, 0x73u => Key.F4, 0x74u => Key.F5,
        0x75u => Key.F6, 0x76u => Key.F7, 0x77u => Key.F8, 0x78u => Key.F9, 0x79u => Key.F10,
        0x7Au => Key.F11, 0x7Bu => Key.F12, 0x7Cu => Key.F13, 0x7Du => Key.F14, 0x7Eu => Key.F15,
        0x7Fu => Key.F16, 0x80u => Key.F17, 0x81u => Key.F18, 0x82u => Key.F19, 0x83u => Key.F20,
        0x84u => Key.F21, 0x85u => Key.F22, 0x86u => Key.F23, 0x87u => Key.F24,

        // Navigation, editing and lock keys.
        0x08u => Key.Back,
        0x09u => Key.Tab,
        0x0Du => Key.Return,
        0x13u => Key.Pause,
        0x14u => Key.CapsLock,
        0x1Bu => Key.Escape,
        0x20u => Key.Space,
        0x21u => Key.PageUp,
        0x22u => Key.PageDown,
        0x23u => Key.End,
        0x24u => Key.Home,
        0x25u => Key.Left,
        0x26u => Key.Up,
        0x27u => Key.Right,
        0x28u => Key.Down,
        0x2Cu => Key.PrintScreen,
        0x2Du => Key.Insert,
        0x2Eu => Key.Delete,
        0x90u => Key.NumLock,
        0x91u => Key.Scroll,

        // OEM punctuation.
        0xBAu => Key.OemSemicolon,
        0xBBu => Key.OemPlus,
        0xBCu => Key.OemComma,
        0xBDu => Key.OemMinus,
        0xBEu => Key.OemPeriod,
        0xBFu => Key.OemQuestion,
        0xC0u => Key.OemTilde,
        0xDBu => Key.OemOpenBrackets,
        0xDCu => Key.OemPipe,
        0xDDu => Key.OemCloseBrackets,
        0xDEu => Key.OemQuotes,

        _ => Key.None,
    };
}

/// <summary>
/// Names for the Win32 virtual-key codes that MiniClip accepts in a global hotkey,
/// and the inverse lookup used by the parser.
/// </summary>
/// <remarks>
/// Dynamic hotkeys can be chosen by the user, so hotkeys can only be stored safely if
/// they are stored by name rather than by identifying a control by its index.
/// The table is intentionally closed: a code that is not listed here is not offered to
/// the user, and <see cref="TryParseName"/> rejects its name.
/// </remarks>
public static class VirtualKeyNames
{
    /// <summary>
    /// Returns the canonical name of a virtual-key code, for example <c>0x56</c> gives
    /// <c>"V"</c>, <c>0x70</c> gives <c>"F1"</c> and <c>0x20</c> gives <c>"Space"</c>.
    /// </summary>
    /// <param name="virtualKey">The virtual-key code.</param>
    /// <returns>
    /// A human-readable key name, or <c>"VK_0x%02X"</c> when the code has no name in this
    /// table. This member never throws.
    /// </returns>
    public static string GetName(uint virtualKey)
    {
        if (virtualKey >= 'A' && virtualKey <= 'Z') return ((char)virtualKey).ToString();
        if (virtualKey >= '0' && virtualKey <= '9') return ((char)virtualKey).ToString();
        if (virtualKey >= 0x70 && virtualKey <= 0x87)
        {
            return "F" + (virtualKey - 0x6Fu).ToString(CultureInfo.InvariantCulture);
        }

        return GetNamedKeyName(virtualKey) ?? "VK_0x" + virtualKey.ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a key name such as <c>"V"</c>, <c>"F24"</c>, <c>"Space"</c>, <c>"Esc"</c> or
    /// <c>"Minus"</c>.
    /// </summary>
    /// <param name="name">The name to parse. Matching is case-insensitive and surrounding whitespace is ignored.</param>
    /// <param name="virtualKey">The matching virtual-key code, or zero when parsing fails.</param>
    /// <returns><see langword="true"/> when the name is known; otherwise <see langword="false"/>.</returns>
    /// <remarks>This member never throws.</remarks>
    public static bool TryParseName(string name, out uint virtualKey)
    {
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(name)) return false;

        string token = name.Trim().ToUpperInvariant();
        if (token.Length == 0) return false;

        if (token.Length == 1)
        {
            char c = token[0];
            if (c >= 'A' && c <= 'Z') { virtualKey = c; return true; }
            if (c >= '0' && c <= '9') { virtualKey = c; return true; }

            uint punctuation = c switch
            {
                '-' => 0xBDu,               // Minus
                '+' or '=' => 0xBBu,        // Plus
                ',' => 0xBCu,               // Comma
                '.' => 0xBEu,               // Period
                '/' => 0xBFu,               // Slash
                '\\' => 0xDCu,              // Backslash
                ';' => 0xBAu,               // Semicolon
                '\'' => 0xDEu,              // Quote
                '[' => 0xDBu,               // BracketLeft
                ']' => 0xDDu,               // BracketRight
                '`' or '~' => 0xC0u,        // Grave
                _ => 0u,
            };

            if (punctuation == 0) return false;

            virtualKey = punctuation;
            return true;
        }

        if (token[0] == 'F' && TryParseFunctionKeyNumber(token, out int number))
        {
            // VK_F1 = 0x70 ... VK_F24 = 0x87.
            virtualKey = 0x6Fu + (uint)number;
            return true;
        }

        uint named = GetNamedKeyVirtualKey(token);
        if (named == 0) return false;

        virtualKey = named;
        return true;
    }

    /// <summary>
    /// Whether a virtual-key code may be used in a global hotkey: A-Z, 0-9, F1-F24 or one of
    /// the named keys.
    /// </summary>
    /// <param name="virtualKey">The virtual-key code.</param>
    /// <returns><see langword="true"/> when the code is supported.</returns>
    /// <remarks>
    /// This is the key whitelist behind <see cref="HotkeyGesture.IsValid"/>. Codes outside it
    /// (numpad keys, media keys, IME keys, mouse buttons) are deliberately refused so that a
    /// hotkey can always be shown to the user by name. This member never throws.
    /// </remarks>
    public static bool IsSupportedKey(uint virtualKey)
    {
        if (virtualKey >= 'A' && virtualKey <= 'Z') return true;
        if (virtualKey >= '0' && virtualKey <= '9') return true;
        if (virtualKey >= 0x70 && virtualKey <= 0x87) return true;

        return GetNamedKeyName(virtualKey) is not null;
    }

    /// <summary>Returns the number of a name of the form F1 to F24, validating the range.</summary>
    private static bool TryParseFunctionKeyNumber(string token, out int number)
    {
        number = 0;

        // "F1" to "F24" only; NumberStyles.None rejects signs, separators and whitespace.
        if (token.Length < 2 || token.Length > 3) return false;

        return int.TryParse(token.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && number >= 1
            && number <= 24;
    }

    /// <summary>Name of a key that is neither a letter, a digit nor a function key.</summary>
    private static string? GetNamedKeyName(uint virtualKey) => virtualKey switch
    {
        0x08u => "Backspace",
        0x09u => "Tab",
        0x0Du => "Enter",
        0x13u => "Pause",
        0x14u => "CapsLock",
        0x1Bu => "Escape",
        0x20u => "Space",
        0x21u => "PageUp",
        0x22u => "PageDown",
        0x23u => "End",
        0x24u => "Home",
        0x25u => "Left",
        0x26u => "Up",
        0x27u => "Right",
        0x28u => "Down",
        0x2Cu => "PrintScreen",
        0x2Du => "Insert",
        0x2Eu => "Delete",
        0x90u => "NumLock",
        0x91u => "ScrollLock",
        0xBAu => "Semicolon",
        0xBBu => "Plus",
        0xBCu => "Comma",
        0xBDu => "Minus",
        0xBEu => "Period",
        0xBFu => "Slash",
        0xC0u => "Grave",
        0xDBu => "BracketLeft",
        0xDCu => "Backslash",
        0xDDu => "BracketRight",
        0xDEu => "Quote",
        _ => null,
    };

    /// <summary>Virtual-key code of an upper-case key name, or zero when the name is unknown.</summary>
    private static uint GetNamedKeyVirtualKey(string upperToken) => upperToken switch
    {
        "SPACE" => 0x20u,
        "ENTER" or "RETURN" => 0x0Du,
        "ESCAPE" or "ESC" => 0x1Bu,
        "TAB" => 0x09u,
        "BACKSPACE" or "BACK" => 0x08u,
        "DELETE" => 0x2Eu,
        "INSERT" => 0x2Du,
        "HOME" => 0x24u,
        "END" => 0x23u,
        "PAGEUP" or "PGUP" => 0x21u,
        "PAGEDOWN" or "PGDN" => 0x22u,
        "UP" => 0x26u,
        "DOWN" => 0x28u,
        "LEFT" => 0x25u,
        "RIGHT" => 0x27u,
        "PRINTSCREEN" => 0x2Cu,
        "PAUSE" => 0x13u,
        "CAPSLOCK" => 0x14u,
        "NUMLOCK" => 0x90u,
        "SCROLLLOCK" or "SCROLL" => 0x91u,
        "MINUS" => 0xBDu,
        "PLUS" => 0xBBu,
        "COMMA" => 0xBCu,
        "PERIOD" or "DOT" => 0xBEu,
        "SLASH" => 0xBFu,
        "BACKSLASH" => 0xDCu,
        "SEMICOLON" => 0xBAu,
        "QUOTE" or "APOSTROPHE" => 0xDEu,
        "BRACKETLEFT" or "LEFTBRACKET" or "OPENBRACKET" => 0xDBu,
        "BRACKETRIGHT" or "RIGHTBRACKET" or "CLOSEBRACKET" => 0xDDu,
        "GRAVE" or "TILDE" or "BACKTICK" => 0xC0u,
        _ => 0u,
    };
}
