using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over the Character Editor mod's <c>DialogCapsuleUI</c>, with its OWN Ready
    /// family so a rename inside this one dialog cannot take down the main editor screen or any
    /// other dialog family.
    ///
    /// SCOPE: the CAPSULE view only (<c>bInterstellarView == false</c>) -- the three scenario-part
    /// containers (own/taken things, scattered map things, animals) and the numbered capsule slots.
    /// The Interstellar view (Unload/Load/Lift-off, the <c>bStartNewGame</c>/<c>bGamePlus</c>
    /// world-restart chain) is deliberately unbound: <c>lPartsInterstellar</c>, <c>Entladen</c>,
    /// <c>Beladen</c>, <c>NeuenPlanetSuchen</c>, <c>InsOrbit</c>, <c>AStart</c>.
    ///
    /// ADD rides the mod's own opener methods (vehicle A: <c>AAddParts</c>/<c>AAddPartsMap</c>/
    /// <c>AAddPartsAnimal</c>), which construct <c>DialogObjects(DialogType.Object, this, null,
    /// addAnimals)</c> -- the SAME dialog type <c>ObjectsAdapter</c>/<c>CharEditorBrowserScope</c>
    /// already serves, so Add needs no dialog scope of its own. Opening through the capsule's own
    /// constructor call is what makes <c>DialogObjects.mCapsuleUI</c> non-null, and that dialog's
    /// <c>OnAccept()</c> routes to <c>mCapsuleUI.ExternalAddThing</c> when it is.
    ///
    /// REMOVE is MUTATION-C. Each container's per-row delete is an ANONYMOUS closure passed into
    /// <c>SZWidgets.FullListviewScenPart</c> (DialogCapsuleUI.cs:356-406), not a named method -- there
    /// is no vehicle A/B to invoke. <see cref="RemoveFromTaken"/>/<see cref="RemoveFromScatter"/>/
    /// <see cref="RemoveFromAnimals"/> mirror each closure's own two-list removal exactly (for
    /// example Taken: `lPartsTaken.Remove(p); lParts.Remove(p);`), reading and mutating the SAME
    /// static <c>List&lt;ScenPart&gt;</c> fields those closures capture -- `lParts` IS
    /// <c>Find.Scenario</c>'s own live parts list (<c>ScenarioTool.ScenarioParts</c>, assigned once
    /// by <c>UpdateLists()</c>), so removing from it is the real, persisted mutation; removing from
    /// the per-category list keeps THIS scope's own rows and the dialog's own next draw pass in sync,
    /// exactly as the mod's own closures do.
    ///
    /// SHIFT rides the mod's own named handlers (vehicle A): <c>AMoveToMap(ScenPart)</c> for Taken
    /// to Scatter, <c>AMoveToContainer(ScenPart)</c> for the reverse; both call <c>UpdateLists()</c>
    /// themselves. Animals have no shift in the mod's own UI, so this facade offers none either.
    ///
    /// SLOTS: <see cref="Save"/>/<see cref="Load"/> invoke <c>AOnSaveSlot(int)</c>/
    /// <c>AOnLoadSlot(int)</c> (vehicle A). <see cref="SlotStoredText"/> relays the mod's own
    /// <c>dicSlots</c> string verbatim: <c>SaveCapsuleSetup</c>'s format concatenates the parameter
    /// header and the scenario-parts string with no separator before the first pawn entry, too
    /// fragile to hand-parse.
    /// </summary>
    internal static class CharEditorCapsuleCompat
    {
        internal enum ContainerKind
        {
            Taken,
            Scatter,
            Animal,
        }

        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorCapsuleCompat", Resolve);

        private static Type dialogType;
        private static Type ceditorType;

        private static MethodInfo numCapsuleSlotsGetter;

        private static FieldInfo lPartsTakenField;
        private static FieldInfo lPartsScatterField;
        private static FieldInfo lPartsAnimalField;
        private static FieldInfo lPartsField;
        private static FieldInfo dicSlotsField;

        private static MethodInfo aAddPartsMethod;
        private static MethodInfo aAddPartsMapMethod;
        private static MethodInfo aAddPartsAnimalMethod;
        private static MethodInfo aMoveToMapMethod;
        private static MethodInfo aMoveToContainerMethod;
        private static MethodInfo aOnSaveSlotMethod;
        private static MethodInfo aOnLoadSlotMethod;

        private static Type fLabelType;
        private static MethodInfo scenPartLabelGetter;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get { gate.Ensure(); return gate.Ensure(); }
        }

        public static Type DialogType
        {
            get { gate.Ensure(); return dialogType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            dialogType = surface.Type("CharacterEditor.DialogCapsuleUI");
            ceditorType = surface.Supplied("CharacterEditor.CEditor", CharEditorCompat.EditorCore.CEditorType);
            fLabelType = surface.Type("CharacterEditor.FLabel");

            lPartsTakenField = surface.Field(dialogType, "lPartsTaken");
            lPartsScatterField = surface.Field(dialogType, "lPartsScatter");
            lPartsAnimalField = surface.Field(dialogType, "lPartsAnimal");
            lPartsField = surface.Field(dialogType, "lParts");
            dicSlotsField = surface.Field(dialogType, "dicSlots");

            aAddPartsMethod = surface.Method(dialogType, "AAddParts", Type.EmptyTypes);
            aAddPartsMapMethod = surface.Method(dialogType, "AAddPartsMap", Type.EmptyTypes);
            aAddPartsAnimalMethod = surface.Method(dialogType, "AAddPartsAnimal", Type.EmptyTypes);
            aMoveToMapMethod = surface.Method(dialogType, "AMoveToMap", new[] { typeof(ScenPart) });
            aMoveToContainerMethod = surface.Method(dialogType, "AMoveToContainer", new[] { typeof(ScenPart) });
            aOnSaveSlotMethod = surface.Method(dialogType, "AOnSaveSlot", new[] { typeof(int) });
            aOnLoadSlotMethod = surface.Method(dialogType, "AOnLoadSlot", new[] { typeof(int) });

            numCapsuleSlotsGetter = surface.Property(ceditorType, "NumCapsuleSlots")?.GetGetMethod(true);
            scenPartLabelGetter = surface.Property(fLabelType, "ScenPartLabel")?.GetGetMethod(true);

            return surface.Ready && CharEditorCompat.EditorCore.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorCapsuleCompat." + member + " failed: " + ex.Message);
        }

        private static object Api()
        {
            return CharEditorCompat.EditorCore.Api();
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        public static int NumCapsuleSlots
        {
            get
            {
                if (!Ready)
                    return 0;
                try
                {
                    object api = Api();
                    return api != null ? (int)numCapsuleSlotsGetter.Invoke(api, null) : 0;
                }
                catch (Exception ex)
                {
                    Fail("NumCapsuleSlots", ex);
                    return 0;
                }
            }
        }

        /// <summary>The mod's own formatted label for a scenario part, read live from <c>FLabel.ScenPartLabel</c>.</summary>
        public static string PartLabel(ScenPart part)
        {
            if (!Ready || part == null)
                return "";
            try
            {
                var func = scenPartLabelGetter.Invoke(null, null) as Func<ScenPart, string>;
                return func != null ? func(part) ?? "" : "";
            }
            catch (Exception ex)
            {
                Fail("PartLabel", ex);
                return "";
            }
        }

        public static List<ScenPart> Container(Window dlg, ContainerKind kind)
        {
            if (!Ready || dlg == null)
                return new List<ScenPart>();
            try
            {
                FieldInfo field;
                switch (kind)
                {
                    case ContainerKind.Taken:
                        field = lPartsTakenField;
                        break;
                    case ContainerKind.Scatter:
                        field = lPartsScatterField;
                        break;
                    default:
                        field = lPartsAnimalField;
                        break;
                }
                // The three lists are STATIC (shared across the type, meaningful because the mod
                // enforces onlyOneOfTypeAllowed for this window) -- read with GetValue(null), not
                // the instance, matching the fields themselves.
                return field.GetValue(null) as List<ScenPart> ?? new List<ScenPart>();
            }
            catch (Exception ex)
            {
                Fail("Container", ex);
                return new List<ScenPart>();
            }
        }

        /// <summary>Slot N's stored text verbatim (empty when unoccupied) -- see class remarks for why this is not re-parsed.</summary>
        public static string SlotStoredText(Window dlg, int index)
        {
            if (!Ready || dlg == null)
                return "";
            try
            {
                var dict = dicSlotsField.GetValue(dlg) as Dictionary<int, string>;
                return dict != null && dict.TryGetValue(index, out string value) ? value ?? "" : "";
            }
            catch (Exception ex)
            {
                Fail("SlotStoredText", ex);
                return "";
            }
        }

        // ------------------------------------------------------------------
        // Add (vehicle A -- opens DialogObjects; the browser scope routes Confirm to
        // ExternalAddThing, see class remarks).
        // ------------------------------------------------------------------

        public static void AddOwnThing(Window dlg) => InvokeNoArg(dlg, aAddPartsMethod, "AddOwnThing");
        public static void AddScatteredThing(Window dlg) => InvokeNoArg(dlg, aAddPartsMapMethod, "AddScatteredThing");
        public static void AddAnimal(Window dlg) => InvokeNoArg(dlg, aAddPartsAnimalMethod, "AddAnimal");

        private static void InvokeNoArg(Window dlg, MethodInfo method, string caller)
        {
            if (!Ready || dlg == null || method == null)
                return;
            try
            {
                method.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // ------------------------------------------------------------------
        // Remove (MUTATION-C -- see class remarks: mirrors each container's own anonymous
        // removeAction closure exactly).
        // ------------------------------------------------------------------

        public static void RemoveFromTaken(ScenPart part) => RemoveFrom(lPartsTakenField, part);
        public static void RemoveFromScatter(ScenPart part) => RemoveFrom(lPartsScatterField, part);
        public static void RemoveFromAnimals(ScenPart part) => RemoveFrom(lPartsAnimalField, part);

        private static void RemoveFrom(FieldInfo categoryField, ScenPart part)
        {
            if (!Ready || part == null)
                return;
            try
            {
                (categoryField.GetValue(null) as List<ScenPart>)?.Remove(part);
                (lPartsField.GetValue(null) as List<ScenPart>)?.Remove(part);
            }
            catch (Exception ex)
            {
                Fail("RemoveFrom", ex);
            }
        }

        // ------------------------------------------------------------------
        // Shift (vehicle A).
        // ------------------------------------------------------------------

        /// <summary>Taken -&gt; Scattered map things (AMoveToMap, the mod's own "bmoveright" handler).</summary>
        public static void ShiftToScatter(Window dlg, ScenPart part)
        {
            InvokeOneArg(dlg, aMoveToMapMethod, part, "ShiftToScatter");
        }

        /// <summary>Scattered map things -&gt; Taken (AMoveToContainer, the mod's own "bmoveleft" handler).</summary>
        public static void ShiftToTaken(Window dlg, ScenPart part)
        {
            InvokeOneArg(dlg, aMoveToContainerMethod, part, "ShiftToTaken");
        }

        private static void InvokeOneArg(Window dlg, MethodInfo method, ScenPart part, string caller)
        {
            if (!Ready || dlg == null || method == null || part == null)
                return;
            try
            {
                method.Invoke(dlg, new object[] { part });
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // ------------------------------------------------------------------
        // Slots (vehicle A).
        // ------------------------------------------------------------------

        public static void Save(Window dlg, int index) => InvokeIntArg(dlg, aOnSaveSlotMethod, index, "Save");
        public static void Load(Window dlg, int index) => InvokeIntArg(dlg, aOnLoadSlotMethod, index, "Load");

        private static void InvokeIntArg(Window dlg, MethodInfo method, int index, string caller)
        {
            if (!Ready || dlg == null || method == null)
                return;
            try
            {
                method.Invoke(dlg, new object[] { index });
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }
    }
}
