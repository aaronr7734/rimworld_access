using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The Health tab's mutation vehicles: <c>EditorUI+BlockHealth</c>'s title-bar controls, its
    /// lower action row and its hediff-row handlers. Gated by its own <see cref="HealthReady"/> so
    /// a rename here cannot take down the sibling slices. Every mutator invokes BlockHealth's own
    /// handler (vehicle A), so the mod's own clipboard, dialogs and flags stay shared with a
    /// sighted player's clicks — <see cref="ToggleShowHidden"/>'s handler also writes vanilla's
    /// private <c>HealthCardUtility.showAllHediffs</c>, which the health-summary panel beside it
    /// reads.
    ///
    /// Three handlers branch on <c>Event.current.alt</c> inline, so
    /// <see cref="HealthInvokeWithSimulatedModifiers"/> sets the modifier around the invocation and
    /// restores it, with its own copy of the idiom gated on <see cref="HealthReady"/> rather than a
    /// sibling's readiness. <c>AHurt()</c>'s gate is <c>Event.current.alt || InStartingScreen</c>,
    /// so Alt always selects the random-injuries branch and <see cref="DamageUntilDeath"/> must be
    /// gated absent in world generation by its caller, matching what a sighted player can trigger.
    /// <see cref="Resurrect"/> rides the mod's own misspelled <c>ARessurect()</c> and heals
    /// nothing, distinct from the Actions section's "Resurrect and heal".
    ///
    /// <see cref="VisibleHediffsForListing"/> deliberately does not reflect BlockHealth's private
    /// VisibleHediffs/GetListPriority: both are trivial formulas over public vanilla data that
    /// <see cref="HealthTabHelper"/> already exposes. This method only adds the mod's own
    /// show-hidden branch on top.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static bool healthInitialized;
        private static bool healthReady;

        private static Type blockHealthType;
        private static Type healthToolType;

        private static MethodInfo getBlockHealth;

        private static PropertyInfo healthShowHiddenGetter;
        private static MethodInfo healthToggleShowHiddenMethod;
        private static MethodInfo healthCopyMethod;
        private static MethodInfo healthPasteMethod;
        private static FieldInfo healthClipboardField;
        private static MethodInfo healthRandomMethod;
        private static MethodInfo healthFullHealMethod;
        private static MethodInfo healthMedicateMethod;
        private static MethodInfo healthAnaesthetizeMethod;
        private static MethodInfo healthHurtMethod;
        private static MethodInfo healthResurrectMethod;
        private static MethodInfo healthAddHediffMethod;
        private static MethodInfo healthEditHediffMethod;
        private static MethodInfo healthRemoveHediffMethod;
        private static MethodInfo healthToolIsHediffWithLevelMethod;

        /// <summary>True when every member this slice's Health section needs resolved.</summary>
        public static bool HealthReady
        {
            get
            {
                EnsureInit();
                return healthReady;
            }
        }

        /// <summary>Called from the main <c>EnsureInit</c> once EditorUI and TabType are known; idempotent, like the sibling binders.</summary>
        private static void BindHealth()
        {
            if (healthInitialized)
                return;
            healthInitialized = true;

            var surface = new ReflectionSurface("CharEditorCompat health");
            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            blockHealthType = surface.Supplied("EditorUI.BlockHealth", editorUIType.GetNestedType("BlockHealth", NestedFlags));
            healthToolType = surface.Type("CharacterEditor.HealthTool");

            getBlockHealth = surface.Required("EditorUI.Get<BlockHealth>(TabType) closed",
                CloseGeneric(editorUIType, "Get", blockHealthType));

            healthShowHiddenGetter = surface.Property(blockHealthType, "ShowHidden");
            healthToggleShowHiddenMethod = surface.Method(blockHealthType, "AHiddenChanged", new[] { typeof(bool) });
            healthCopyMethod = surface.Method(blockHealthType, "ACopyHealth", Type.EmptyTypes);
            healthPasteMethod = surface.Method(blockHealthType, "APasteHealth", Type.EmptyTypes);
            healthClipboardField = surface.Field(blockHealthType, "lCopyHealth");
            healthRandomMethod = surface.Method(blockHealthType, "ARandomHealth", Type.EmptyTypes);
            healthFullHealMethod = surface.Method(blockHealthType, "AFullHeal", Type.EmptyTypes);
            healthMedicateMethod = surface.Method(blockHealthType, "AMedicate", Type.EmptyTypes);
            healthAnaesthetizeMethod = surface.Method(blockHealthType, "AAnaesthetize", Type.EmptyTypes);
            healthHurtMethod = surface.Method(blockHealthType, "AHurt", Type.EmptyTypes);
            healthResurrectMethod = surface.Method(blockHealthType, "ARessurect", Type.EmptyTypes);
            healthAddHediffMethod = surface.Method(blockHealthType, "AAddHediff", Type.EmptyTypes);
            healthEditHediffMethod = surface.Method(blockHealthType, "AEditHediff", new[] { typeof(Hediff) });
            healthRemoveHediffMethod = surface.Method(blockHealthType, "BRemoveHediff", new[] { typeof(Hediff) });
            healthToolIsHediffWithLevelMethod = surface.Method(healthToolType, "IsHediffWithLevel", new[] { typeof(HediffDef) });

            healthReady = surface.Ready;
        }

        private static object HealthBlock(Window editorUI)
        {
            return Block(editorUI, getBlockHealth, "BlockHealth");
        }

        private static void InvokeOnHealthBlock(Window editorUI, MethodInfo method, object[] args, string caller)
        {
            if (!HealthReady || method == null)
                return;
            try
            {
                object block = HealthBlock(editorUI);
                if (block != null)
                    method.Invoke(block, args);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        /// <summary>This slice's own copy of the simulated-modifier idiom, gated on <see cref="HealthReady"/> rather than a sibling's readiness.</summary>
        private static void HealthInvokeWithSimulatedModifiers(MethodInfo method, object instance, EventModifiers modifiers, string caller)
        {
            if (!HealthReady || method == null)
                return;
            Event evt = Event.current;
            EventModifiers original = evt != null ? evt.modifiers : EventModifiers.None;
            try
            {
                if (evt != null)
                    KeyboardHelper.SetEventModifiers(evt, modifiers);
                method.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
            finally
            {
                if (evt != null)
                    KeyboardHelper.SetEventModifiers(evt, original);
            }
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        /// <summary>Mirrors vanilla's private static <c>HealthCardUtility.showAllHediffs</c> via the mod's own property.</summary>
        public static bool ShowHiddenHediffs(Window editorUI)
        {
            if (!HealthReady)
                return false;
            try
            {
                object block = HealthBlock(editorUI);
                return block != null && (bool)healthShowHiddenGetter.GetValue(block);
            }
            catch (Exception ex)
            {
                Fail("ShowHiddenHediffs", ex);
                return false;
            }
        }

        /// <summary>True when a Copy Health clipboard exists (BlockHealth.lCopyHealth non-empty) -- the Paste row's enablement gate.</summary>
        public static bool HasHealthClipboard(Window editorUI)
        {
            if (!HealthReady)
                return false;
            try
            {
                object block = HealthBlock(editorUI);
                var list = block != null ? healthClipboardField.GetValue(block) as List<Hediff> : null;
                return !list.NullOrEmpty();
            }
            catch (Exception ex)
            {
                Fail("HasHealthClipboard", ex);
                return false;
            }
        }

        /// <summary>
        /// The hediff rows a listing should show: <see cref="HealthTabHelper.GetVisibleHediffs"/>
        /// while hidden hediffs are not shown, and every hediff including hidden ones while they
        /// are — the mod's own <c>bShowHidden</c> branch, which the plain-data helper has no
        /// equivalent for.
        /// </summary>
        public static List<Hediff> VisibleHediffsForListing(Pawn pawn, bool showHidden)
        {
            if (pawn?.health?.hediffSet == null)
                return new List<Hediff>();
            if (showHidden)
                return pawn.health.hediffSet.hediffs.ToList();
            return HealthTabHelper.GetVisibleHediffs(pawn, showBloodLoss: true).ToList();
        }

        /// <summary>HealthTool.IsHediffWithLevel, a mod-internal extension method gating the AddHediff dialog's Level row.</summary>
        public static bool HediffDefHasLevel(HediffDef def)
        {
            if (!HealthReady || def == null)
                return false;
            try
            {
                return (bool)healthToolIsHediffWithLevelMethod.Invoke(null, new object[] { def });
            }
            catch (Exception ex)
            {
                Fail("HediffDefHasLevel", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Mutators -- each rides BlockHealth's own handler (vehicle A; see class remarks).
        // ------------------------------------------------------------------

        public static void ToggleShowHidden(Window editorUI)
        {
            bool current = ShowHiddenHediffs(editorUI);
            InvokeOnHealthBlock(editorUI, healthToggleShowHiddenMethod, new object[] { !current }, "ToggleShowHidden");
        }

        public static void CopyHealth(Window editorUI) => InvokeOnHealthBlock(editorUI, healthCopyMethod, null, "CopyHealth");

        public static void PasteHealth(Window editorUI) => InvokeOnHealthBlock(editorUI, healthPasteMethod, null, "PasteHealth");

        public static void RandomHealth(Window editorUI) => InvokeOnHealthBlock(editorUI, healthRandomMethod, null, "RandomHealth");

        /// <summary>Opens DialogAddHediff in add mode -- BlockHealth's own title-bar Add icon.</summary>
        public static void AddHediff(Window editorUI) => InvokeOnHealthBlock(editorUI, healthAddHediffMethod, null, "AddHediff");

        /// <summary>Opens DialogAddHediff in edit mode for <paramref name="hediff"/> -- BlockHealth's own row-label button.</summary>
        public static void EditHediff(Window editorUI, Hediff hediff)
        {
            if (hediff == null)
                return;
            InvokeOnHealthBlock(editorUI, healthEditHediffMethod, new object[] { hediff }, "EditHediff");
        }

        /// <summary>Removes one hediff through BlockHealth's own per-row Delete icon; Delete here always removes, with no remove-MODE toggle.</summary>
        public static void RemoveHediff(Window editorUI, Hediff hediff)
        {
            if (hediff == null)
                return;
            InvokeOnHealthBlock(editorUI, healthRemoveHediffMethod, new object[] { hediff }, "RemoveHediff");
        }

        /// <summary>Opens DialogFullheal -- BlockHealth's own AFullHeal() with no Alt simulated.</summary>
        public static void OpenFullHeal(Window editorUI)
        {
            if (!HealthReady)
                return;
            object block = HealthBlock(editorUI);
            if (block != null)
                HealthInvokeWithSimulatedModifiers(healthFullHealMethod, block, EventModifiers.None, "OpenFullHeal");
        }

        /// <summary>AFullHeal()'s Alt branch -- vanilla HealthUtility.HealNonPermanentInjuriesAndRestoreLegs, invoked through the mod's own handler with Alt simulated.</summary>
        public static void InstantFullHeal(Window editorUI)
        {
            if (!HealthReady)
                return;
            object block = HealthBlock(editorUI);
            if (block != null)
                HealthInvokeWithSimulatedModifiers(healthFullHealMethod, block, EventModifiers.Alt, "InstantFullHeal");
        }

        public static void Medicate(Window editorUI) => InvokeOnHealthBlock(editorUI, healthMedicateMethod, null, "Medicate");

        public static void Anaesthetize(Window editorUI) => InvokeOnHealthBlock(editorUI, healthAnaesthetizeMethod, null, "Anaesthetize");

        /// <summary>AHurt()'s Alt-or-world-gen branch (adds one or two random bad injuries) -- Alt simulated always selects it regardless of InStartingScreen.</summary>
        public static void HurtPawn(Window editorUI)
        {
            if (!HealthReady)
                return;
            object block = HealthBlock(editorUI);
            if (block != null)
                HealthInvokeWithSimulatedModifiers(healthHurtMethod, block, EventModifiers.Alt, "HurtPawn");
        }

        /// <summary>
        /// AHurt()'s in-game, non-Alt branch, damaging until downed or dead. The caller must gate
        /// this absent during world generation: the mod's inline condition is
        /// <c>Event.current.alt || InStartingScreen</c>, so a non-Alt invocation there would
        /// silently run the same branch <see cref="HurtPawn"/> already covers.
        /// </summary>
        public static void DamageUntilDeath(Window editorUI)
        {
            if (!HealthReady)
                return;
            object block = HealthBlock(editorUI);
            if (block != null)
                HealthInvokeWithSimulatedModifiers(healthHurtMethod, block, EventModifiers.None, "DamageUntilDeath");
        }

        /// <summary>Plain resurrect through BlockHealth's own dead-only button: vanilla TryResurrect plus a respawn if needed, no healing.</summary>
        public static void Resurrect(Window editorUI) => InvokeOnHealthBlock(editorUI, healthResurrectMethod, null, "Resurrect");
    }
}
