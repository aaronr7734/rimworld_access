using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Character Development's ITab_Pawn_WantsAndQuirks: wants in the
    /// left panel, quirks in the right, both scroll views of raw boxes, icon buttons and
    /// drag-reorderable rows. Capture can only see the loose labels those panels draw, and the
    /// points bar draws a hardcoded "0", the running total and the target as three separate
    /// labels, so the generic branch read the bar as three bare numbers. This adapter reads the
    /// same data the panels draw and gives every want and quirk a section of its own.
    /// </summary>
    internal sealed class WqWantsTabAdapter : InspectNodeAdapter
    {
        public override bool Ready => WqCompat.Ready;

        // Stable English dispatch token (l10n-exempt: the display names below render the tab's
        // own label).
        public override string CategoryKey => "WQ Wants";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => CompatText.ModText("WQ_Wants");

        public override string CategoryDisplayName(object obj) => CompatText.ModText("WQ_Wants");

        /// <summary>Mirrors the tab's own IsVisible.</summary>
        public override bool CanExpand(object obj)
        {
            if (!WqCompat.Ready || !WqCompat.CharactersMenuEnabled() || !(obj is Pawn pawn))
            {
                return false;
            }
            if (!WqCompat.CanHaveWants(pawn))
            {
                return false;
            }
            return pawn.Faction == Faction.OfPlayer || pawn.IsSlaveOfColony;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0 || !(obj is Pawn pawn))
            {
                return;
            }
            InspectNodeFactory.GuardedBuild("Character Development wants tab", delegate
            {
                Build(categoryItem, pawn, mode);
            });
        }

        private void Build(InspectionTreeItem categoryItem, Pawn pawn, InspectionMode mode)
        {
            object data = WqCompat.WantsData(pawn);
            if (data == null)
            {
                return;
            }

            InspectNodeFactory.DetailLine(categoryItem,
                CompatText.Flatten(CompatText.ModText("WQ_WantsSubtitle")));

            // The points bar and the modifier count are drawn only in per-pawn mode.
            if (WqCompat.PawnSpecificRewardPoints())
            {
                InspectNodeFactory.DetailLine(categoryItem,
                    "RimWorldAccess.Compat.Wq.PointsProgress".Translate(
                        WqCompat.DataCharacterPoints(data), WqCompat.DataPointsNeeded(data)));
                InspectNodeFactory.DetailLine(categoryItem,
                    CompatText.ModArgs("WQ_PawnUnlockedModifiers", WqCompat.DataRewardPoints(data)));
            }

            BuildWants(categoryItem, pawn, data, mode);
            BuildQuirks(categoryItem, pawn, data, mode);
        }

        private void BuildWants(InspectionTreeItem categoryItem, Pawn pawn, object data, InspectionMode mode)
        {
            List<object> wants = WqCompat.Wants(data);
            if (wants.Count == 0)
            {
                InspectNodeFactory.DetailLine(categoryItem,
                    CompatText.Flatten(CompatText.ModText("WQ_NoActiveWants")));
                return;
            }
            foreach (object want in wants)
            {
                object row = want;
                string label = WqCompat.WantLabel(row);
                if (WqCompat.TakeWantIsNew(row))
                {
                    label = CompatText.JoinSentences(new List<string> { label, "RimWorldAccess.Compat.Wq.New".Translate() });
                }
                InspectNodeFactory.Section(categoryItem, label, row,
                    section => BuildWantChildren(categoryItem, section, pawn, data, row, mode));
            }
        }

        private void BuildWantChildren(InspectionTreeItem categoryItem, InspectionTreeItem section,
            Pawn pawn, object data, object want, InspectionMode mode)
        {
            InspectNodeFactory.DetailLines(section, WqCompat.WantDescription(want));

            if (WqCompat.TryWantProgress(pawn, want, out float progress))
            {
                InspectNodeFactory.DetailLine(section,
                    "RimWorldAccess.Compat.Wq.WantProgress".Translate(progress.ToStringPercent()));
            }

            bool mentalBreak = WqCompat.WantIsMentalBreak(want);
            InspectNodeFactory.DetailLine(section, mentalBreak
                ? CompatText.ModText("WQ_CausedByMentalBreak")
                : CompatText.ModText("WQ_OnCompletion") + " "
                    + CompatText.ModArgs("WQ_CharacterPointsReward", WqCompat.WantReward(want)));

            if (mode == InspectionMode.ReadOnly)
            {
                return;
            }

            int rerollsLeft = WqCompat.RerollsPerWant() - WqCompat.WantRerollCount(want);
            if (!mentalBreak && rerollsLeft > 0)
            {
                InspectNodeFactory.ActionRow(section,
                    CompatText.Flatten(CompatText.ModArgs("WQ_RerollWantWithCount", rerollsLeft)), want,
                    () => OnReroll(categoryItem, pawn, data, want, mode));
            }

            InspectNodeFactory.ActionRow(section,
                "RimWorldAccess.Compat.Wq.DismissWant".Translate(), want,
                () => OnDismissWant(categoryItem, pawn, data, want, mode));
        }

        private void BuildQuirks(InspectionTreeItem categoryItem, Pawn pawn, object data, InspectionMode mode)
        {
            InspectNodeFactory.Section(categoryItem, CompatText.ModText("WQ_Quirks"), data,
                section => BuildQuirkChildren(categoryItem, section, pawn, data, mode));
        }

        private void BuildQuirkChildren(InspectionTreeItem categoryItem, InspectionTreeItem section,
            Pawn pawn, object data, InspectionMode mode)
        {
            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(section, CompatText.ModText("WQ_AddQuirks"), pawn,
                    OpenCharactersMenu);
            }

            List<object> quirks = WqCompat.Quirks(data);
            if (quirks.Count == 0)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.Compat.Wq.NoQuirks".Translate());
                return;
            }
            foreach (object quirk in quirks)
            {
                object row = quirk;
                InspectNodeFactory.Section(section, WqCompat.QuirkLabel(row), row,
                    child => BuildQuirkDetail(categoryItem, child, pawn, data, row, mode));
            }
        }

        private void BuildQuirkDetail(InspectionTreeItem categoryItem, InspectionTreeItem child,
            Pawn pawn, object data, object quirk, InspectionMode mode)
        {
            InspectNodeFactory.DetailLines(child, WqCompat.QuirkDescription(quirk));
            if (mode == InspectionMode.ReadOnly)
            {
                return;
            }
            InspectNodeFactory.ActionRow(child,
                "RimWorldAccess.Compat.Wq.RemoveQuirk".Translate(), quirk,
                () => ConfirmRemoveQuirk(categoryItem, pawn, data, quirk, mode), opensOverlayMenu: true);
        }

        private void ConfirmRemoveQuirk(InspectionTreeItem categoryItem, Pawn pawn, object data,
            object quirk, InspectionMode mode)
        {
            // The x button's own confirmation, then its own removal path.
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                CompatText.ModArgs("WQ_ConfirmRemoveQuirk", WqCompat.QuirkLabel(quirk)),
                delegate
                {
                    WqCompat.RemoveQuirk(pawn, data, quirk);
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                    Rebuild(categoryItem, pawn, mode);
                }));
        }

        private void OnReroll(InspectionTreeItem categoryItem, Pawn pawn, object data, object want,
            InspectionMode mode)
        {
            WqCompat.RerollWant(pawn, data, want);
            WqCompat.RerollSound?.PlayOneShotOnCamera(null);
            Rebuild(categoryItem, pawn, mode);
        }

        private void OnDismissWant(InspectionTreeItem categoryItem, Pawn pawn, object data, object want,
            InspectionMode mode)
        {
            WqCompat.RemoveWant(data, want);
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            Rebuild(categoryItem, pawn, mode);
        }

        /// <summary>The Add quirks button's own handler.</summary>
        private static void OpenCharactersMenu()
        {
            MainButtonDef def = WqCompat.CharactersMenuDef;
            if (def != null)
            {
                Find.MainTabsRoot.SetCurrentTab(def);
            }
        }

        private void Rebuild(InspectionTreeItem categoryItem, Pawn pawn, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, pawn, mode, this,
                InspectTabManager.GetSharedInstance(WqCompat.InspectTabType));
        }
    }
}
