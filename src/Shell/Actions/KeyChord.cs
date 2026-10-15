using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One key plus an exact modifier set — the unit a binding is made of.
    /// "Alt+M" matches only when Alt is down and Ctrl/Shift are not; a chord with
    /// no modifiers matches only the bare key. Vanilla KeyBindingData cannot
    /// represent modifiers at all, which is why the shell owns its own model.
    ///
    /// PURE: no Verse/Unity dependency beyond the KeyCode enum, so this links
    /// into the test project. Platform- and language-aware display goes through
    /// the KeyChordFormat hooks rather than game APIs.
    /// </summary>
    public readonly struct KeyChord : IEquatable<KeyChord>
    {
        public readonly KeyCode Key;
        public readonly bool Ctrl;
        public readonly bool Shift;
        public readonly bool Alt;

        public KeyChord(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            if (key == KeyCode.None)
                throw new ArgumentException("A key chord requires a real key.", nameof(key));
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        public static KeyChord Of(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            return new KeyChord(key, ctrl, shift, alt);
        }

        /// <summary>
        /// The six modifier-held variants of <paramref name="key"/> — Shift, Ctrl, Alt,
        /// Ctrl+Shift, Shift+Alt, Ctrl+Shift+Alt — excluding the bare key and excluding
        /// Ctrl+Alt, which the ambient map scope reserves for colonist-bar inspection.
        /// Used where a confirm key must still confirm while the player holds a modifier
        /// that some mod reads off the physical keyboard.
        /// </summary>
        public static IReadOnlyList<KeyChord> ModifierHeldVariants(KeyCode key)
        {
            return new List<KeyChord>
            {
                new KeyChord(key, shift: true),
                new KeyChord(key, ctrl: true),
                new KeyChord(key, alt: true),
                new KeyChord(key, ctrl: true, shift: true),
                new KeyChord(key, shift: true, alt: true),
                new KeyChord(key, ctrl: true, shift: true, alt: true),
            };
        }

        /// <summary>
        /// Exact-match test against a snapshot: same key AND modifier-for-modifier
        /// equality. Alt+M does not fire plain M; plain M does not fire while Alt
        /// is held. This is the whole matching contract — handlers never inspect
        /// modifiers themselves.
        /// </summary>
        public bool Matches(KeyEventSnapshot e)
        {
            return e.Key == Key && e.Ctrl == Ctrl && e.Shift == Shift && e.Alt == Alt;
        }

        /// <summary>
        /// Canonical machine form used for persistence: modifiers in fixed
        /// Ctrl, Shift, Alt order joined to the KeyCode enum name, e.g.
        /// "Ctrl+Shift+G", "Alt+M", "UpArrow". Round-trips through Parse.
        /// </summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            if (Ctrl)
                sb.Append("Ctrl+");
            if (Shift)
                sb.Append("Shift+");
            if (Alt)
                sb.Append("Alt+");
            sb.Append(Key.ToString());
            return sb.ToString();
        }

        /// <summary>
        /// Parses the Serialize format. Modifier tokens may appear in any order
        /// and any case ("control" is accepted for "Ctrl"); the final token must
        /// be a KeyCode enum name (case-insensitive).
        /// </summary>
        public static bool TryParse(string text, out KeyChord chord)
        {
            chord = default(KeyChord);
            if (string.IsNullOrEmpty(text))
                return false;

            string[] tokens = text.Split('+');
            bool ctrl = false, shift = false, alt = false;
            KeyCode key = KeyCode.None;

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.Length == 0)
                    return false;

                bool isLast = i == tokens.Length - 1;
                if (!isLast)
                {
                    if (!TryParseModifier(token, ref ctrl, ref shift, ref alt))
                        return false;
                    continue;
                }

                // The last token is the key itself — but tolerate a trailing
                // modifier-only string ("Ctrl+") as malformed.
                if (TryParseModifier(token, ref ctrl, ref shift, ref alt))
                    return false;
                KeyCode parsed;
                if (!TryParseKeyCode(token, out parsed) || parsed == KeyCode.None)
                    return false;
                key = parsed;
            }

            chord = new KeyChord(key, ctrl, shift, alt);
            return true;
        }

        public static KeyChord Parse(string text)
        {
            KeyChord chord;
            if (!TryParse(text, out chord))
                throw new FormatException("Not a valid key chord: '" + text + "'");
            return chord;
        }

        private static bool TryParseModifier(string token, ref bool ctrl, ref bool shift, ref bool alt)
        {
            if (string.Equals(token, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "Control", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
                return true;
            }
            if (string.Equals(token, "Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
                return true;
            }
            if (string.Equals(token, "Alt", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                return true;
            }
            return false;
        }

        private static bool TryParseKeyCode(string token, out KeyCode key)
        {
            // A bare digit ("5") is the shorthand spelling of the top-row number
            // keys. Enum.Parse would read it as the raw underlying value
            // ((KeyCode)5), NOT KeyCode.Alpha5, so map digits explicitly first.
            if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
            {
                key = KeyCode.Alpha0 + (token[0] - '0');
                return true;
            }
            try
            {
                // Reject any other purely-numeric token: Enum.Parse casts a
                // numeric string to a raw KeyCode value instead of failing,
                // silently accepting bogus chords. Chords are always spelled by
                // enum name (e.g. "Alpha5"), never by number.
                if (int.TryParse(token, out _))
                {
                    key = KeyCode.None;
                    return false;
                }
                key = (KeyCode)Enum.Parse(typeof(KeyCode), token, ignoreCase: true);
                return true;
            }
            catch (ArgumentException)
            {
                key = KeyCode.None;
                return false;
            }
            catch (OverflowException)
            {
                key = KeyCode.None;
                return false;
            }
        }

        /// <summary>
        /// Human/speech-facing form: platform-aware modifier words (via
        /// KeyChordFormat hooks) plus a humanized key name, e.g. "Alt+Up Arrow",
        /// "Option+M" on macOS once the game side installs its hooks.
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                var sb = new StringBuilder();
                if (Ctrl)
                {
                    sb.Append(KeyChordFormat.CtrlLabel(Key));
                    sb.Append('+');
                }
                if (Shift)
                {
                    sb.Append(KeyChordFormat.ShiftLabel());
                    sb.Append('+');
                }
                if (Alt)
                {
                    sb.Append(KeyChordFormat.AltLabel());
                    sb.Append('+');
                }
                sb.Append(KeyChordFormat.KeyLabel(Key));
                return sb.ToString();
            }
        }

        public bool Equals(KeyChord other)
        {
            return Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyChord && Equals((KeyChord)obj);
        }

        public override int GetHashCode()
        {
            int hash = (int)Key;
            if (Ctrl)
                hash |= 1 << 24;
            if (Shift)
                hash |= 1 << 25;
            if (Alt)
                hash |= 1 << 26;
            return hash;
        }

        public static bool operator ==(KeyChord a, KeyChord b) { return a.Equals(b); }
        public static bool operator !=(KeyChord a, KeyChord b) { return !a.Equals(b); }

        public override string ToString()
        {
            return Serialize();
        }
    }

    /// <summary>
    /// Display hooks for KeyChord.DisplayLabel. Pure defaults here; the game
    /// side installs platform- and language-aware providers at startup
    /// (ShellBootstrap.InstallChordDisplayHooks: macOS names Ctrl as the key that
    /// fires it, see KeyboardHelper.IsCtrlHeld, and every worded key name comes from
    /// the Shell.Key.* Keyed table) without the struct ever touching a game
    /// API. The English defaults below are the exact strings those keys carry,
    /// so unhooked contexts — the test project above all — read identically.
    /// </summary>
    public static class KeyChordFormat
    {
        public static Func<KeyCode, string> CtrlLabel = key => "Ctrl";
        public static Func<string> ShiftLabel = () => "Shift";
        public static Func<string> AltLabel = () => "Alt";

        /// <summary>Optional per-key override; return null to use the default humanized name.</summary>
        public static Func<KeyCode, string> KeyLabelOverride = null;

        private static readonly Dictionary<KeyCode, string> FriendlyNames = new Dictionary<KeyCode, string>
        {
            { KeyCode.UpArrow, "Up Arrow" },
            { KeyCode.DownArrow, "Down Arrow" },
            { KeyCode.LeftArrow, "Left Arrow" },
            { KeyCode.RightArrow, "Right Arrow" },
            { KeyCode.Return, "Enter" },
            { KeyCode.KeypadEnter, "Numpad Enter" },
            { KeyCode.PageUp, "Page Up" },
            { KeyCode.PageDown, "Page Down" },
            { KeyCode.BackQuote, "Backtick" },
            { KeyCode.LeftBracket, "Left Bracket" },
            { KeyCode.RightBracket, "Right Bracket" },
            { KeyCode.KeypadPlus, "Numpad Plus" },
            { KeyCode.KeypadMinus, "Numpad Minus" },
            { KeyCode.KeypadMultiply, "Numpad Star" },
            { KeyCode.KeypadDivide, "Numpad Slash" },
            { KeyCode.KeypadPeriod, "Numpad Period" },
        };

        public static string KeyLabel(KeyCode key)
        {
            var overrideHook = KeyLabelOverride;
            if (overrideHook != null)
            {
                string custom = overrideHook(key);
                if (!string.IsNullOrEmpty(custom))
                    return custom;
            }

            string friendly;
            if (FriendlyNames.TryGetValue(key, out friendly))
                return friendly;

            string name = key.ToString();

            // Alpha0..Alpha9 are the number row; speak just the digit.
            if (name.Length == 6 && name.StartsWith("Alpha", StringComparison.Ordinal) && char.IsDigit(name[5]))
                return name[5].ToString();
            if (name.StartsWith("Keypad", StringComparison.Ordinal) && name.Length == 7 && char.IsDigit(name[6]))
                return "Numpad " + name[6];

            return SplitCamelCase(name);
        }

        /// <summary>"CapsLock" → "Caps Lock"; leaves runs of digits/capitals like "F12" intact.</summary>
        private static string SplitCamelCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
