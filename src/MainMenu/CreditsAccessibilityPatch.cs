using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announces the end-credits / victory screen (Screen_Credits). RimWorld scrolls the credits
    /// visually with no spoken output, so a screen-reader player who just won — e.g. the Archonexus
    /// relocation finale — would otherwise hear only the music and never the victory text.
    ///
    /// We speak only strings the game itself shows: the screen's own text records (the victory
    /// message for a won game, plus the closing line) and its "Skip credits" button label. No
    /// invented instructions. Escape already works — Screen_Credits keeps preventCameraMotion true,
    /// which suppresses the mod's pause-menu Escape, so Escape reaches the screen's own
    /// skip → close → main-menu handling (OnCancelKeyPressed).
    /// </summary>
    [HarmonyPatch(typeof(Screen_Credits), "PreOpen")]
    public static class CreditsAccessibilityPatch
    {
        private static readonly FieldInfo CredsField = AccessTools.Field(typeof(Screen_Credits), "creds");

        [HarmonyPostfix]
        static void Postfix(Screen_Credits __instance)
        {
            try
            {
                var sb = new StringBuilder();

                // Only the text records carry readable prose (the victory message and the closing
                // line); the role/name records are the scrolling contributor list, left to scroll.
                if (CredsField?.GetValue(__instance) is IEnumerable creds)
                {
                    foreach (object entry in creds)
                    {
                        if (!(entry is CreditRecord_Text textRecord) || textRecord.text.NullOrEmpty())
                            continue;

                        // Newlines aren't spoken; flatten the record's lines into clauses.
                        string line = string.Join(". ", textRecord.text.StripTags()
                            .Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0));
                        if (line.Length == 0)
                            continue;

                        if (sb.Length > 0)
                            sb.Append(". ");
                        sb.Append(line);
                    }
                }

                if (sb.Length == 0)
                    return;

                // The game's own on-screen skip control (label verbatim, no invented key hint).
                sb.Append(". ").Append("SkipCredits".Translate().ToString());

                TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error announcing Screen_Credits: {ex}");
            }
        }
    }
}
