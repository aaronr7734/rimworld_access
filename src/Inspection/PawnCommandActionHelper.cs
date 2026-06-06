using System;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds accessible action tree items for the per-pawn commands vanilla draws on the character
    /// card (<see cref="CharacterCardUtility"/>): Banish and Execute. Shared by the inspection tree's
    /// Character node and the info card's Actions tab so both stay identical and mirror vanilla's own
    /// visibility guards, tooltips, and behavior. All user-facing text comes from the game's own
    /// translation keys.
    /// </summary>
    internal static class PawnCommandActionHelper
    {
        // Mirror CharacterCardUtility's per-button visibility guards. Vanilla draws both buttons only
        // inside its `else if (!pawn.health.Dead)` branch, so a dead pawn (e.g. a guilty colonist's
        // corpse, whose inner pawn is still IsFreeColonist with guilt) must not qualify.
        public static bool CanBanish(Pawn pawn) =>
            IsLivingFreeColonist(pawn) && pawn.Spawned;

        public static bool CanExecute(Pawn pawn) =>
            IsLivingFreeColonist(pawn) && pawn.guilt != null && pawn.guilt.IsGuilty;

        private static bool IsLivingFreeColonist(Pawn pawn) =>
            pawn != null && pawn.IsFreeColonist && !pawn.IsQuestLodger()
            && (pawn.health == null || !pawn.health.Dead);

        public static bool HasAnyAction(Pawn pawn) => CanBanish(pawn) || CanExecute(pawn);

        /// <summary>
        /// Appends Banish and/or Execute action items (whichever the pawn currently qualifies for) as
        /// children of <paramref name="parent"/>. Call before a node's other children are added when
        /// the actions should appear first. <paramref name="closeHost"/> runs before the banish
        /// confirmation dialog opens so a host window (e.g. the info card) can step aside; pass null
        /// where nothing needs closing.
        /// </summary>
        public static void AddPawnCommandActions(InspectionTreeItem parent, Pawn pawn, Action closeHost)
        {
            if (parent == null || pawn == null) return;
            int indent = parent.IndentLevel + 1;

            if (CanBanish(pawn))
                AddChild(parent, BuildBanishItem(pawn, indent, closeHost));

            if (CanExecute(pawn))
                AddChild(parent, BuildExecuteItem(pawn, indent));
        }

        private static InspectionTreeItem BuildBanishItem(Pawn pawn, int indent, Action closeHost)
        {
            // Only the "will be left to die" warning is worth speaking; the base BanishTip is just the
            // word "Banish", which already is the label.
            string description = PawnBanishUtility.WouldBeLeftToDie(pawn, pawn.Tile)
                ? (string)"BanishTipWillDie".Translate(pawn.LabelShort, pawn).CapitalizeFirst()
                : null;

            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "BanishTip".Translate(),
                Description = description,
                Data = pawn,
                IndentLevel = indent,
                IsExpandable = false,
                IsExpanded = false,
            };

            item.OnActivate = () =>
            {
                if (pawn.Downed)
                {
                    TolkHelper.SpeakData(
                        (string)"MessageCantBanishDownedPawn".Translate(pawn.LabelShort, pawn).AdjustedFor(pawn),
                        SpeechPriority.High);
                    return;
                }
                closeHost?.Invoke();
                PawnBanishUtility.ShowBanishPawnConfirmationDialog(pawn);
            };

            return item;
        }

        private static InspectionTreeItem BuildExecuteItem(Pawn pawn, int indent)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "Execute".Translate(),
                Description = ExecuteDescription(pawn),
                Data = pawn,
                IndentLevel = indent,
                IsExpandable = false,
                IsExpanded = false,
            };

            item.OnActivate = () =>
            {
                bool nowMarked = !pawn.guilt.awaitingExecution;
                pawn.guilt.awaitingExecution = nowMarked;
                (nowMarked ? SoundDefOf.Tick_High : SoundDefOf.Tick_Low).PlayOneShotOnCamera();
                item.Description = ExecuteDescription(pawn);
                // Vanilla only messages when marking on; speak the verified marked message there, and
                // a clear "Execute: Off" (both localized) when un-marking.
                TolkHelper.SpeakData(
                    nowMarked
                        ? (string)"MessageColonistMarkedForExecution".Translate(pawn)
                        : (string)("Execute".Translate() + ": " + "Off".Translate()),
                    SpeechPriority.High);
            };

            return item;
        }

        /// <summary>The execute tooltip, flattened to sentence separators, prefixed with the current
        /// "marked for execution" state when the colonist is already scheduled.</summary>
        private static string ExecuteDescription(Pawn pawn)
        {
            string tip = FlattenTip("ExecuteColonist".Translate());
            if (pawn.guilt != null && pawn.guilt.awaitingExecution)
                return (string)"MessageColonistMarkedForExecution".Translate(pawn) + ". " + tip;
            return tip;
        }

        // The screen reader has no use for the game tip's newlines; flatten to ". " sentence
        // separators (matches the mod's "no newlines as separators" convention).
        private static string FlattenTip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder();
            foreach (var raw in text.Split('\n'))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(part);
            }
            return sb.ToString();
        }

        private static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }
    }
}
