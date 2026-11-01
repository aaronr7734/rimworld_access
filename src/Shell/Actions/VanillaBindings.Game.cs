using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Live consultation of vanilla's rebindable key table (KeyPrefs.KeyPrefsData).
    /// Deliberately uncached: the table mutates when the player rebinds and exposes no
    /// change event, so every answer is read fresh. Modifier-blind by design — vanilla
    /// KeyBindingData carries bare KeyCodes only; chord/modifier matching (including the
    /// macOS Option-for-Ctrl substitution) stays in the KeyChord layer.
    /// </summary>
    internal static class VanillaBindings
    {
        /// <summary>True when <paramref name="key"/> is live-bound to <paramref name="def"/>
        /// in either binding slot.</summary>
        internal static bool IsBoundTo(KeyBindingDef def, KeyCode key)
        {
            if (def == null || key == KeyCode.None)
                return false;
            return KeyPrefs.KeyPrefsData.keyPrefs.TryGetValue(def, out KeyBindingData data)
                && (key == data.keyBindingA || key == data.keyBindingB);
        }

        /// <summary>The binding's primary live key: slot A, else slot B, else None.</summary>
        internal static KeyCode BoundKey(KeyBindingDef def)
        {
            return def == null ? KeyCode.None : def.MainKey;
        }

        /// <summary>Readable label for the binding's primary live key; null when unbound, so callers never say "None".</summary>
        internal static string HotkeyLabel(KeyBindingDef def)
        {
            KeyCode key = BoundKey(def);
            return key == KeyCode.None ? null : key.ToStringReadable();
        }

        /// <summary>As <see cref="HotkeyLabel"/>, but null when any claim beneath every modal takes the bare key, whatever its when-guard.</summary>
        internal static string ReachableHotkeyLabel(KeyBindingDef def)
        {
            KeyCode key = BoundKey(def);
            return key == KeyCode.None || FocusStack.ClaimedBelowModals(KeyChord.Of(key)) ? null : key.ToStringReadable();
        }
    }
}
