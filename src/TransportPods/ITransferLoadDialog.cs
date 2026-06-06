using System.Collections.Generic;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Abstraction over the two near-identical "load things and send them somewhere" dialogs:
    /// <see cref="Dialog_LoadTransporters"/> (transport pods and shuttles) and
    /// <see cref="Dialog_EnterPortal"/> (map portals: ancient complexes, pit gates, insect
    /// lairs, and pocket-map exits).
    ///
    /// Both dialogs share the same Pawns/Items tab structure, the same
    /// <see cref="TransferableOneWay"/> backing list, and the same accept flow, so a single
    /// navigation and announcement code path in <see cref="TransportPodLoadingState"/> can
    /// drive either one. This interface isolates the handful of dialog-specific details
    /// (reflection field names, mass capacity, change notification, summary stats).
    /// </summary>
    internal interface ITransferLoadDialog
    {
        /// <summary>Full screen-reader announcement spoken when the dialog opens.</summary>
        string OpenAnnouncement { get; }

        /// <summary>Announcement spoken when the dialog closes without accepting.</summary>
        string CancelAnnouncement { get; }

        /// <summary>All transferables (pawns and items) backing the dialog.</summary>
        List<TransferableOneWay> GetAllTransferables();

        /// <summary>The dialog's current tab as an integer (0 = Pawns, 1 = Items).</summary>
        int GameTab { get; set; }

        /// <summary>
        /// Total mass the destination can hold. Returns <see cref="float.MaxValue"/> when the
        /// destination has no limit (map portals impose no mass cap).
        /// </summary>
        float MassCapacity { get; }

        /// <summary>
        /// Notifies the dialog that transfer counts changed so it can recache derived values.
        /// May be a no-op for dialogs whose widgets recompute themselves each frame.
        /// </summary>
        void NotifyTransferablesChanged();

        /// <summary>Triggers the dialog's accept/confirm logic.</summary>
        void TriggerAccept();

        /// <summary>
        /// Whether the dialog presents a stats/summary panel. Transport pods and shuttles show
        /// caravan-style stats; map portals show none, so the Tab summary view is unavailable.
        /// </summary>
        bool HasSummary { get; }

        /// <summary>Populates the summary stat lines shown via the Tab key.</summary>
        /// <param name="outItems">List to fill with formatted stat strings.</param>
        /// <param name="massUsage">Current total mass of selected items, computed by the caller.</param>
        void BuildSummaryItems(List<string> outItems, float massUsage);

        /// <summary>
        /// Returns the (stat name, breakdown explanation) for the summary line at the given
        /// index, or null when no breakdown is available for that line. Indexed (not matched on
        /// the line's display text) so it stays correct in non-English languages: the line text
        /// is localized, but the index maps to a language-independent stat kind the adapter
        /// recorded while building the summary.
        /// </summary>
        (string name, string explanation)? GetStatExplanation(int summaryIndex);
    }
}
