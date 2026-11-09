using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 3
    /// (PsychicEntropyGizmo, matched by GetType().Name: toggles the neural heat
    /// limiter on Enter). Always handled once the name matches — the original
    /// branch has no failure/fallthrough path — and never reaches the shared
    /// epilogue (no Close() call; the menu stays open for further browsing).
    ///
    /// The announcement facets port the legacy ladders' PsychicEntropyGizmo
    /// branches (label switch case and GetPsychicEntropyStatus), upgraded from
    /// per-property reflection to typed reads — Pawn_PsychicEntropyTracker is a
    /// public type; only the gizmo's `tracker` field itself is private. The
    /// description facet is new relative to the legacy ladder: it surfaces the
    /// hover tooltips the vanilla gizmo draws (PsychicEntropyGizmo.GizmoOnGUI:
    /// heat bar, psyfocus bar, limiter button, pain-boost label), all computable
    /// from the tracker without render-time state.
    ///
    /// BuildStatus/BuildDescription/ToggleNeuralHeatLimiter are content builders
    /// shared with the VPE compat shell (VpePsychicStatusHandler): Vanilla
    /// Psycasts Expanded's PsychicStatusGizmo wraps the same public
    /// Pawn_PsychicEntropyTracker, so the two gizmos speak an identical readout.
    /// </summary>
    internal sealed class PsychicEntropyGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// PsychicEntropyGizmo.tracker is private (the tracker type itself is
        /// public); cached once since the registry pins this handler to that
        /// exact type.
        /// </summary>
        private static readonly FieldInfo TrackerField = typeof(PsychicEntropyGizmo)
            .GetField("tracker", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// PsychicEntropyGizmo.targetValue is the gizmo's private drag store for
        /// the psyfocus target bar; vanilla writes it during the drag and commits
        /// via tracker.SetPsyfocusTarget.
        /// </summary>
        private static readonly FieldInfo TargetValueField = typeof(PsychicEntropyGizmo)
            .GetField("targetValue", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            // Tracker-null guard lives here (not in the shared builder): a null
            // tracker still reports "limited" so this branch is always handled,
            // matching the original ladder's no-fallthrough contract.
            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            bool newState = tracker == null || ToggleNeuralHeatLimiter(tracker);
            string stateStr = (newState
                ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NeuralHeatLimiter".Loc(stateStr));
            return true;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is PsychicEntropyGizmo))
                return false;

            label = "RimWorldAccess.Inspection.Gizmo.Type.PsychicEntropy".Translate();
            return true;
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = null;

            if (!(gizmo is PsychicEntropyGizmo) || TargetValueField == null)
                return false;

            // Vanilla only lets the psyfocus target be dragged for
            // colonist-player-controlled pawns; same gate here.
            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            if (tracker?.Pawn == null || !tracker.Pawn.IsColonistPlayerControlled)
                return false;

            try
            {
                adapter = new GizmoSliderAdapter
                {
                    Title = "PsyfocusLabelGizmo".Translate(),
                    Value = (float)TargetValueField.GetValue(gizmo),
                    Min = 0f,
                    Max = 1f,
                    // Vanilla's psyfocus bar drags in 1/16 increments.
                    Step = 1f / 16f,
                    Write = value =>
                    {
                        TargetValueField.SetValue(gizmo, value);
                        tracker.SetPsyfocusTarget(value);
                    },
                    // Enter toggles the neural heat limiter (TryExecute above);
                    // arrows adjust the target directly.
                    InteractionHint = "RimWorldAccess.Inspection.Gizmo.HintPsychicEntropyArrows".Translate(),
                };
                return true;
            }
            catch
            {
                adapter = null;
                return false;
            }
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            if (tracker == null)
                return false;

            status = BuildStatus(tracker);
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            if (tracker == null)
                return false;

            description = BuildDescription(tracker);
            return !string.IsNullOrEmpty(description);
        }

        /// <summary>
        /// Legacy GetPsychicEntropyStatus, value formats preserved: the heat
        /// bar's X / Y readout, both psyfocus bar marks (current fill and the
        /// draggable target indicator), and the limiter button state.
        /// </summary>
        internal static string BuildStatus(Pawn_PsychicEntropyTracker tracker)
        {
            var parts = new List<string>
            {
                "RimWorldAccess.Inspection.Gizmo.Status.NeuralHeat".Translate(
                    tracker.EntropyValue.ToString("F0"), tracker.MaxEntropy.ToString("F0")),
                "RimWorldAccess.Inspection.Gizmo.Status.PsyfocusValue".Translate(
                    (tracker.CurrentPsyfocus * 100f).ToString("F0")),
                "RimWorldAccess.Inspection.Gizmo.Status.PsyfocusTarget".Translate(
                    (tracker.TargetPsyfocus * 100f).ToString("F0")),
                "RimWorldAccess.Inspection.Gizmo.Status.LimiterStatus".Translate(
                    (tracker.limitEntropyAmount
                        ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                        : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate()),
            };

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Surfaces the hover tooltips the vanilla gizmo draws
        /// (PsychicEntropyGizmo.GizmoOnGUI: heat bar, psyfocus bar, limiter
        /// button, pain-boost label), all computable from the tracker without
        /// render-time state.
        /// </summary>
        internal static string BuildDescription(Pawn_PsychicEntropyTracker tracker)
        {
            Pawn pawn = tracker.Pawn;
            var parts = new List<string>();

            // Heat bar tooltip (PsychicEntropyGizmo.GizmoOnGUI, rect7) minus its current/max
            // title, which BuildStatus already speaks.
            string heatTip = "";
            float recoveryRate = tracker.RecoveryRate;
            if (recoveryRate > float.Epsilon)
            {
                // Vanilla computes seconds-until-zero as EntropyValue / RecoveryRate;
                // guarded here so a zero rate can't speak "Infinity".
                heatTip = "PawnTooltipPsychicEntropyStats".Translate(
                    recoveryRate.ToString("0.#"), Mathf.Round(tracker.EntropyValue / recoveryRate)) + "\n\n";
            }
            heatTip += "PawnTooltipPsychicEntropyDesc".Translate();
            parts.Add(heatTip);

            // Psyfocus bar tooltip (rect10); vanilla passes no target override (its selectedPsyfocusTarget
            // stays -1). The first paragraph is the current/desired values BuildStatus already speaks.
            string psyfocusTip = tracker.PsyfocusTipString();
            int valuesEnd = psyfocusTip?.IndexOf("\n\n") ?? -1;
            if (valuesEnd >= 0)
                psyfocusTip = psyfocusTip.Substring(valuesEnd + 2);
            if (!string.IsNullOrEmpty(psyfocusTip))
                parts.Add(psyfocusTip);

            if (pawn != null)
            {
                // Limiter button tooltip: vanilla only draws the button (and its
                // tip) for colonist-player-controlled pawns.
                if (pawn.IsColonistPlayerControlled)
                    parts.Add("PawnTooltipPsychicEntropyLimit".Translate());

                // Pain-boost label tooltip, mirroring the vanilla precondition
                // (TryGetPainMultiplier): shown when the recovery-rate stat has a
                // StatPart_Pain, with the pawn's pain level and recovery bonus.
                List<StatPart> statParts = StatDefOf.PsychicEntropyRecoveryRate?.parts;
                if (statParts != null && pawn.health?.hediffSet != null)
                {
                    for (int i = 0; i < statParts.Count; i++)
                    {
                        if (statParts[i] is StatPart_Pain statPartPain)
                        {
                            string recoveryBonus = (statPartPain.PainFactor(pawn) - 1f).ToStringPercent("F0");
                            parts.Add("PawnTooltipPsychicEntropyPainFocus".Translate(
                                pawn.health.hediffSet.PainTotal.ToStringPercent("F0"), recoveryBonus));
                            break;
                        }
                    }
                }
            }

            return GizmoTextUtility.FlattenNewlines(string.Join("\n\n", parts).StripTags());
        }

        /// <summary>
        /// Toggles the neural heat limiter, returning the new state (true =
        /// limited). Pawn_PsychicEntropyTracker.limitEntropyAmount is a public
        /// field, so the write is typed; the sounds mirror the vanilla limiter
        /// button (low tick when enabling the limit, high when lifting it).
        /// </summary>
        internal static bool ToggleNeuralHeatLimiter(Pawn_PsychicEntropyTracker tracker)
        {
            bool newValue = !tracker.limitEntropyAmount;
            tracker.limitEntropyAmount = newValue;

            if (newValue)
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            else
                SoundDefOf.Tick_High.PlayOneShotOnCamera();

            return newValue;
        }

        /// <summary>
        /// Resolves the gizmo's private tracker field and casts it to the public
        /// Pawn_PsychicEntropyTracker for typed reads. Null when the field is
        /// missing (game structure changed) or unset.
        /// </summary>
        private static Pawn_PsychicEntropyTracker GetTracker(Gizmo gizmo)
        {
            if (!(gizmo is PsychicEntropyGizmo) || TrackerField == null)
                return null;

            return TrackerField.GetValue(gizmo) as Pawn_PsychicEntropyTracker;
        }
    }
}
