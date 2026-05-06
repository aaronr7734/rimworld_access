using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Confirmation dialog for shelf linking when items are already in different groups.
    /// Uses Dialog_MessageBox for proper accessibility via MessageBoxAccessibilityPatch.
    /// </summary>
    public static class ShelfLinkingConfirmDialog
    {
        /// <summary>
        /// Shows a confirmation dialog for already-linked items using Dialog_MessageBox.
        /// The dialog is automatically accessible via MessageBoxAccessibilityPatch.
        /// </summary>
        /// <param name="alreadyLinked">List of items already in different groups</param>
        /// <param name="onYes">Action to execute if user confirms</param>
        /// <param name="onNo">Action to execute if user cancels</param>
        public static void Show(List<IStorageGroupMember> alreadyLinked, Action onYes, Action onNo)
        {
            // Build message with group names
            var groupDetails = alreadyLinked
                .Select(item => (string)"RimWorldAccess.Building.Shelf.DialogItem".Translate(
                    ShelfLinkingHelper.GetStorageLabel(item),
                    ShelfLinkingHelper.GetGroupName(item)))
                .ToList();

            string itemWord = alreadyLinked.Count == 1
                ? (string)"RimWorldAccess.Building.Shelf.DialogItemWordOne".Translate()
                : (string)"RimWorldAccess.Building.Shelf.DialogItemWordMany".Translate();
            string message = "RimWorldAccess.Building.Shelf.DialogMessage".Translate(
                alreadyLinked.Count, itemWord, string.Join("\n", groupDetails));

            Find.WindowStack.Add(new Dialog_MessageBox(
                message,
                "RimWorldAccess.Building.Shelf.DialogYesLinkAnyway".Translate(),
                onYes,
                "RimWorldAccess.Building.Shelf.DialogDontLink".Translate(),
                onNo,
                "RimWorldAccess.Building.Shelf.DialogTitle".Translate(),
                false
            ));
        }
    }
}
