using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Prefix on <c>PawnRosterList.DrawSelection</c>. The roster's selection box is a bare
    /// <c>Widgets.CheckboxDraw</c> answered only by raw mouse presses, so each row records a
    /// captioned checkbox over that cell. A keyboard flip lands in the same set the mouse paints.
    /// </summary>
    internal static class PersonaDirectorRosterSelectionPatch
    {
        public static void Prefix(object __instance, Rect row, Pawn pawn)
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(__instance);
            if (selected == null || pawn == null)
            {
                return;
            }
            bool on = selected.Contains(pawn);
            bool before = on;
            WidgetCapture.RecordAndMaybeToggleCheckbox(new Rect(row.x, row.y, 30f, row.height),
                "RimWorldAccess.Compat.RimTalk.PersonaDirector.SelectPawn".Translate(pawn.LabelShortCap), ref on, false);
            if (on == before)
            {
                return;
            }
            // MUTATION-C: mirrors DrawSelection's single-row paint (`_selectedPawns.Add`/`Remove`); its only trigger is a raw mouse press.
            if (on) selected.Add(pawn); else selected.Remove(pawn);
        }
    }
}
