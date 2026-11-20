using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The tokens the fishing tab's value rows are identified by, shared
    /// between the manifest below (which emits them) and
    /// <see cref="FishingZoneMenuState"/> (which maps a focused model row onto
    /// one).
    ///
    /// Four sites are SYNTHETIC. The repeat-mode button carries no literal at
    /// all, and the three count labels concatenate their value onto the
    /// translated text, which leaves a bare formatting fragment (" ", " / ",
    /// "F0") nearest the call rather than the translation key — display text,
    /// never an admissible token. All four therefore need a tripwire, which is
    /// what the "#" shape makes mandatory at the ring point.
    /// </summary>
    internal static class FishingRowKeys
    {
        /// <summary>decompiled RimWorld/ITab_Fishing.cs:44.</summary>
        internal const string RepeatModeSite = "ITab_Fishing.FillTab#0";

        /// <summary>:65.</summary>
        internal const string RepeatCountSite = "ITab_Fishing.FillTab#1";

        /// <summary>:70. Vanilla folds the owned count and the target count into this one label, so two model rows share it.</summary>
        internal const string CurrentlyHaveSite = "ITab_Fishing.FillTab#2";

        /// <summary>:81.</summary>
        internal const string PauseWhenSatisfied = "PauseWhenSatisfied";

        /// <summary>:88.</summary>
        internal const string UnpauseAtSite = "ITab_Fishing.FillTab#4";

        /// <summary>:107, whose tooltip key is the literal nearest the call.</summary>
        internal const string FishPopulation = "FishPopulationDesc";

        /// <summary>:109, likewise. The slider under it at :110 carries the same token, so one focus rings both.</summary>
        internal const string MinimumPopulation = "MinimumPopulationDesc";
    }

    /// <summary>
    /// The row calls <c>FillTab</c> makes, resolved by exact signature: plain
    /// ButtonText (decompiled Verse/Listing_Standard.cs:248), both Label
    /// overloads (:95 and :100 — the unpause row passes a TaggedString, every
    /// other label a string), the default CheckboxLabeled (:215) and Slider (:372).
    /// </summary>
    internal static class FishingRowCalls
    {
        internal static readonly MethodInfo ButtonText =
            AccessTools.Method(typeof(Listing_Standard), "ButtonText",
                new[] { typeof(string), typeof(string), typeof(float) });

        internal static readonly MethodInfo Label =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(string), typeof(float), typeof(TipSignal?) });

        internal static readonly MethodInfo TaggedLabel =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(TaggedString), typeof(float), typeof(string) });

        internal static readonly MethodInfo CheckboxLabeled =
            AccessTools.Method(typeof(Listing_Standard), "CheckboxLabeled",
                new[] { typeof(string), typeof(bool).MakeByRefType(), typeof(string), typeof(float), typeof(float) });

        internal static readonly MethodInfo Slider =
            AccessTools.Method(typeof(Listing_Standard), "Slider",
                new[] { typeof(float), typeof(float), typeof(float) });
    }

    /// <summary>
    /// Arms the listing ring for the fishing tab. The tab draws inside the
    /// inspect pane's ImmediateWindow, which vanilla creates internally, so
    /// this scope owns no window and the generic InnerWindowOnGUI arm can
    /// never match it — hence a bespoke bracket on the tab's own body
    /// (decompiled RimWorld/ITab_Fishing.cs:30, the declaring type's override).
    /// A null or empty zone returns early (:33-36), which simply yields an
    /// empty pass.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Fishing), "FillTab")]
    internal static class FishingTabRingPatch
    {
        private static bool openedPass;

        [HarmonyPrefix]
        public static void Prefix()
        {
            if (IsLayoutPass() || !FishingZoneMenuState.IsActive)
            {
                return;
            }
            // A leaked flag means our own postfix never ran; BeginPass clears
            // whatever that pass left behind.
            if (ListingRowCapture.IsPassOpen && !openedPass)
            {
                return;
            }
            openedPass = true;
            ListingRowCapture.BeginPass(FishingZoneMenuState.CurrentListingFocus());
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!openedPass)
            {
                return;
            }
            openedPass = false;
            ListingRowCapture.EndPass();
        }

        private static bool IsLayoutPass()
        {
            return Event.current != null && Event.current.type == EventType.Layout;
        }
    }

    /// <summary>
    /// Each fish row's own <c>ThingDef</c>, straight off vanilla's parameter:
    /// <c>ListFish</c> allocates the row with <c>GetRect</c> as its first act
    /// (decompiled RimWorld/ITab_Fishing.cs:128), so no marker is needed.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Fishing), "ListFish")]
    internal static class FishingFishRowPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ThingDef fish)
        {
            ListingRowCapture.SetPendingRowObject(fish);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.SetPendingRowObject(null);
        }
    }

    /// <summary>
    /// The eight value rows of <c>FillTab</c>, in IL order (decompiled
    /// RimWorld/ITab_Fishing.cs:44, :65, :70, :81, :88, :107, :109, :110). The
    /// repeat-mode switch (:62) and the pause-when-satisfied nesting (:86) need
    /// no mirroring: an unexecuted branch emits no marker.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Fishing), "FillTab")]
    internal static class FishingTabRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[]
            {
                FishingRowCalls.ButtonText, FishingRowCalls.Label,
                FishingRowCalls.TaggedLabel, FishingRowCalls.CheckboxLabeled, FishingRowCalls.Slider,
            },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = FishingRowCalls.ButtonText,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = FishingRowKeys.RepeatModeSite,
                },
                new MarkerSite
                {
                    RowCall = FishingRowCalls.Label,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = FishingRowKeys.RepeatCountSite,
                },
                new MarkerSite
                {
                    RowCall = FishingRowCalls.Label,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = FishingRowKeys.CurrentlyHaveSite,
                },
                new MarkerSite
                {
                    RowCall = FishingRowCalls.CheckboxLabeled,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = FishingRowKeys.PauseWhenSatisfied,
                },
                new MarkerSite
                {
                    RowCall = FishingRowCalls.TaggedLabel,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = FishingRowKeys.UnpauseAtSite,
                },
                new MarkerSite
                {
                    RowCall = FishingRowCalls.Label,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = FishingRowKeys.FishPopulation,
                },
                new MarkerSite
                {
                    RowCall = FishingRowCalls.Label,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = FishingRowKeys.MinimumPopulation,
                },
                new MarkerSite
                {
                    RowCall = FishingRowCalls.Slider,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = FishingRowKeys.MinimumPopulation,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }
}
