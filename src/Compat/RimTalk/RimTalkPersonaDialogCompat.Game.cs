using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for <c>RimTalk.UI.PersonaEditorWindow</c> plus its two openers
    /// (<c>BioTabPersonalityPatch</c>'s Bio-tab chip, <c>HealthCardUtilityPatch</c>'s vocal-link
    /// health row): resolves the window's constructor and its private <c>_pawn</c>/
    /// <c>_isGenerating</c> fields once behind <see cref="Ready"/> (missing type or member =
    /// RimTalk not loaded, or its shipped DLL drifted — silent decline), installs the scope
    /// registration and draw-pass Harmony patch, and registers the two inspection-tree opener
    /// rows via <see cref="InspectNodeRegistry.RegisterCategoryExtender"/>.
    /// </summary>
    internal static class RimTalkPersonaDialogCompat
    {
        private const string RimTalkPackageId = "cj.rimtalk";
        private const string VocalLinkHediffDefName = "VocalLinkImplant";

        private static readonly Type dialogType;
        private static readonly ConstructorInfo dialogCtor;
        private static readonly FieldInfo pawnField;
        private static readonly FieldInfo isGeneratingField;
        private static readonly bool ready;

        private static readonly Type pawnUtilType;
        private static readonly MethodInfo hasVocalLinkMethod;
        private static readonly HediffDef vocalLinkDef;

        public static bool Ready => ready;

        static RimTalkPersonaDialogCompat()
        {
            var surface = new ReflectionSurface("RimTalk persona editor compat");

            dialogType = surface.Type("RimTalk.UI.PersonaEditorWindow");
            pawnField = surface.Field(dialogType, "_pawn");
            isGeneratingField = surface.Field(dialogType, "_isGenerating");
            dialogCtor = dialogType != null
                ? AccessTools.Constructor(dialogType, new[] { typeof(Pawn) })
                : null;

            ready = surface.Ready && dialogCtor != null;

            // OPTIONAL. BioTabPersonalityPatch.AddPersonaElement's own eligibility check also
            // consults the mod's AllowNonHumanToTalk setting, so its extension method is invoked
            // rather than replicated by hand; without it BioOpenerEligible falls back to the
            // colonist/prisoner half of the same gate.
            pawnUtilType = AccessTools.TypeByName("RimTalk.Util.PawnUtil");
            hasVocalLinkMethod = pawnUtilType != null
                ? AccessTools.Method(pawnUtilType, "HasVocalLink", new[] { typeof(Pawn) })
                : null;

            // HealthCardUtilityPatch's own gate is just "the pawn carries this hediff def", a
            // plain DefDatabase lookup that needs no reflection.
            vocalLinkDef = DefDatabase<HediffDef>.GetNamedSilentFail(VocalLinkHediffDefName);
        }

        internal static Pawn GetPawn(Window w)
        {
            return pawnField != null ? pawnField.GetValue(w) as Pawn : null;
        }

        internal static bool IsGenerating(Window w)
        {
            return isGeneratingField != null && (bool)isGeneratingField.GetValue(w);
        }

        /// <summary>
        /// Vehicle A: the exact constructor call both RimTalk openers make
        /// (<c>BioTabPersonalityPatch.AddPersonaElement</c>'s chip click,
        /// <c>HealthCardUtilityPatch.Prefix</c>'s vocal-link row) —
        /// <c>Find.WindowStack.Add(new PersonaEditorWindow(pawn))</c> — reproduced by reflection
        /// since the type is never referenced at compile time.
        /// </summary>
        internal static void OpenPersonaEditor(Pawn pawn)
        {
            if (!ready || pawn == null)
            {
                return;
            }
            try
            {
                object instance = dialogCtor.Invoke(new object[] { pawn });
                Find.WindowStack.Add((Window)instance);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk persona editor guard", ex);
            }
        }

        /// <summary>Mirrors BioTabPersonalityPatch.AddPersonaElement's own gate exactly.</summary>
        internal static bool BioOpenerEligible(Pawn pawn)
        {
            if (!ready || pawn == null)
            {
                return false;
            }
            if (pawn.IsColonist || pawn.IsPrisonerOfColony)
            {
                return true;
            }
            if (hasVocalLinkMethod == null)
            {
                return false;
            }
            try
            {
                return (bool)hasVocalLinkMethod.Invoke(null, new object[] { pawn });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk persona editor guard", ex);
                return false;
            }
        }

        /// <summary>Mirrors HealthCardUtilityPatch's own gate exactly: the pawn carries RimTalk's VocalLinkImplant hediff.</summary>
        internal static bool HealthOpenerEligible(Pawn pawn)
        {
            return ready && vocalLinkDef != null && pawn?.health?.hediffSet != null
                && pawn.health.hediffSet.HasHediff(vocalLinkDef);
        }

        /// <summary>
        /// Registers the scope, its draw-pass patch, and the two inspection-tree opener rows.
        /// No-op unless both RimTalk is active and this window's reflection surface resolved.
        /// </summary>
        public static void Register()
        {
            if (!ready || !ModsConfig.IsActive(RimTalkPackageId))
            {
                return;
            }

            ScopeForWindow.Register(dialogType, delegate (Window w)
            {
                return new RimTalkPersonaScope(w);
            });
            PatchDrawPass();

            // Bio tab chip opener (BioTabPersonalityPatch's own "Character" tab / Bio tab).
            InspectNodeRegistry.RegisterCategoryExtender("Character", AddBioOpenerRow);
            // Health tab vocal-link row opener (HealthCardUtilityPatch): our health reader
            // builds its own hediff tree from pawn.health.hediffSet directly rather than
            // driving vanilla's HealthCardUtility.EntryClicked, so that patch's own click
            // handling is unreachable from here by construction — this extender is the
            // genuine opener, not a parity no-op.
            InspectNodeRegistry.RegisterCategoryExtender("Health", AddHealthOpenerRow);

        }

        private static void AddBioOpenerRow(InspectionTreeItem categoryItem, object obj)
        {
            if (!(obj is Pawn pawn) || !BioOpenerEligible(pawn))
            {
                return;
            }
            string label = "RimWorldAccess.Compat.RimTalk.PersonaEditor.OpenerRow".Translate();
            InspectNodeFactory.ActionRow(categoryItem, label, pawn, () => OpenPersonaEditor(pawn));
        }

        private static void AddHealthOpenerRow(InspectionTreeItem categoryItem, object obj)
        {
            if (!(obj is Pawn pawn) || !HealthOpenerEligible(pawn))
            {
                return;
            }
            string label = "RimWorldAccess.Compat.RimTalk.PersonaEditor.OpenerRow".Translate();
            InspectNodeFactory.ActionRow(categoryItem, label, pawn, () => OpenPersonaEditor(pawn));
        }

        private static void PatchDrawPass()
        {
            try
            {
                MethodInfo doWindowContents = AccessTools.Method(dialogType, "DoWindowContents");
                if (doWindowContents == null)
                {
                    ModLogger.Error("RimTalk persona editor compat: could not resolve PersonaEditorWindow.DoWindowContents; declining draw-pass patch.");
                    return;
                }
                RimWorldAccessMod.HarmonyInstance.Patch(doWindowContents,
                    prefix: new HarmonyMethod(typeof(RimTalkPersonaDialogCompat), nameof(DrawPrefix)),
                    finalizer: new HarmonyMethod(typeof(RimTalkPersonaDialogCompat), nameof(DrawFinalizer)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk persona editor compat: PatchDrawPass failed: {ex.Message}");
            }
        }

        public static void DrawPrefix(object __instance, Rect inRect, out bool __state)
        {
            __state = false;
            try
            {
                Window window = __instance as Window;
                RimTalkPersonaScope scope = RimTalkTextDialogScopeBase.OwningScope<RimTalkPersonaScope>(window);
                if (scope != null)
                {
                    __state = true;
                    scope.BeginDrawPass(inRect);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk persona editor guard", ex);
            }
        }

        public static void DrawFinalizer(object __instance, Exception __exception, bool __state)
        {
            if (!__state)
            {
                return;
            }
            try
            {
                RimTalkPersonaScope scope = __exception == null
                    ? RimTalkTextDialogScopeBase.OwningScope<RimTalkPersonaScope>(__instance as Window)
                    : null;
                if (scope != null)
                {
                    scope.OnGuiPass();
                }
                else
                {
                    WidgetCapture.EndPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk persona editor guard", ex);
            }
        }
    }
}
