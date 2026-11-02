using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for JecsTools' AbilityUser framework (assembly name varies,
    /// "JecsLite" in some packs; RimWorld of Magic's spells and every other
    /// AbilityUser-based mod's abilities all ride it). Four gaps:
    ///
    /// 1. Cast correctness: <see cref="TryCastViaAbility"/> gives
    /// <see cref="TargetingPatch"/> a Category-B vehicle
    /// (PawnAbility.TryCastAbility, which runs CanCastPowerCheck then starts
    /// the cast job) so Enter-to-target on a JecsTools ability no longer
    /// bypasses the mod's own cooldown/eligibility gate via
    /// Verb.OrderForceTarget.
    ///
    /// 2. Targeting announcement: AbilityUser.Command_PawnAbility.ProcessInput
    /// writes Targeter's private fields directly via FieldRef
    /// (BeginTargetingWithVerb) instead of calling vanilla's own
    /// Targeter.BeginTargeting, so our AbilityTargetingPatch postfixes never
    /// fire and the targeting session opens silently. <see cref="ProcessInputPostfix"/>
    /// detects the now-active session afterward and opens
    /// <see cref="GenericTargetingState"/> itself, with an explicit label/AoE
    /// override (see its remarks for why the generic extraction is wrong here).
    ///
    /// 3. Hotkey shift gate: Command_PawnAbility.GizmoOnGUIInt (and RimWorld
    /// of Magic's TorannMagic.TM_Action.DrawAutoCastForGizmo, which reroutes
    /// autocast-enabled ability gizmos through its own hand-rolled redraw) are
    /// both hand-rolled copies of Command.GizmoOnGUIInt reading
    /// `hotKey.KeyDownEvent`/`ToStringReadable` inline, so
    /// <see cref="GizmoHotkeyShiftPatch"/>'s transpiler on the vanilla method
    /// never touches them. Its existing Transpiler is reused verbatim
    /// (manually applied here) against both — same IL shape
    /// (`ldfld Command::hotKey` immediately precedes the KeyDownEvent getter
    /// in both), no duplicated IL-walk logic.
    ///
    /// 4. Status/description: <see cref="JecsAbilityCommandHandler"/>
    /// (registered from <see cref="RegisterGizmoHandlers"/>) reads the
    /// gizmo's live cooldown and full (already-built) description.
    /// </summary>
    internal static class JecsAbilityCompat
    {
        // ---- AbilityUser members (required; missing any = decline) ----
        private static Type verbUseAbilityType;      // AbilityUser.Verb_UseAbility
        private static Type pawnAbilityType;         // AbilityUser.PawnAbility
        private static Type abilityContextType;       // AbilityUser.AbilityContext (enum)
        private static Type commandPawnAbilityType;   // AbilityUser.Command_PawnAbility

        private static PropertyInfo abilityProperty;           // Verb_UseAbility.Ability -> PawnAbility
        private static PropertyInfo pawnAbilityVerbProperty;   // PawnAbility.Verb (getter; the NRE-trap lazy builder)
        private static MethodInfo canCastPowerCheckMethod;     // PawnAbility.CanCastPowerCheck(AbilityContext, out string)
        private static MethodInfo tryCastAbilityMethod;        // PawnAbility.TryCastAbility(AbilityContext, LocalTargetInfo)
        private static PropertyInfo cooldownTicksLeftProperty; // PawnAbility.CooldownTicksLeft
        private static PropertyInfo maxCastingTicksProperty;   // PawnAbility.MaxCastingTicks
        private static FieldInfo commandVerbField;             // Command_PawnAbility.verb
        private static FieldInfo commandPawnAbilityField;      // Command_PawnAbility.pawnAbility

        // ---- Optional members (AoE-radius announcement only; absence just silences that one clause) ----
        private static FieldInfo targetAoEPropertiesField; // VerbProperties_Ability.TargetAoEProperties
        private static FieldInfo aoeRangeField;            // TargetAoEProperties.range

        private static object playerContext; // AbilityContext.Player, boxed (enum value 0)

        private static bool ready;

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("JecsAbilityCompat");
                verbUseAbilityType = surface.Type("AbilityUser.Verb_UseAbility");
                abilityContextType = surface.Type("AbilityUser.AbilityContext");
                commandPawnAbilityType = surface.Type("AbilityUser.Command_PawnAbility");

                // Verb_UseAbility.Ability's own PropertyType is PawnAbility.
                abilityProperty = surface.Property(verbUseAbilityType, "Ability");
                pawnAbilityType = abilityProperty?.PropertyType;

                pawnAbilityVerbProperty = surface.Property(pawnAbilityType, "Verb");
                cooldownTicksLeftProperty = surface.Property(pawnAbilityType, "CooldownTicksLeft");
                maxCastingTicksProperty = surface.Property(pawnAbilityType, "MaxCastingTicks");
                if (abilityContextType != null)
                {
                    canCastPowerCheckMethod = surface.Method(pawnAbilityType, "CanCastPowerCheck",
                        new[] { abilityContextType, typeof(string).MakeByRefType() });
                    tryCastAbilityMethod = surface.Method(pawnAbilityType, "TryCastAbility",
                        new[] { abilityContextType, typeof(LocalTargetInfo) });
                    playerContext = Enum.ToObject(abilityContextType, 0); // AbilityContext.Player == 0
                }

                commandVerbField = surface.Field(commandPawnAbilityType, "verb");
                commandPawnAbilityField = surface.Field(commandPawnAbilityType, "pawnAbility");

                ResolveOptionalAoeMembers();

                ready = surface.Ready;
                if (!ready)
                {
                    return;
                }

                MethodInfo processInputMethod = AccessTools.Method(commandPawnAbilityType, "ProcessInput");
                if (processInputMethod != null)
                {
                    harmony.Patch(processInputMethod,
                        postfix: new HarmonyMethod(typeof(JecsAbilityCompat), nameof(ProcessInputPostfix)));
                }
                else
                {
                    ModLogger.Error("JecsAbilityCompat: AbilityUser.Command_PawnAbility.ProcessInput did not " +
                        "resolve; ability targeting sessions will open silently.");
                }

                RegisterHotkeyShiftGate(harmony);

            }
            catch (Exception ex)
            {
                ModLogger.Error("JecsAbilityCompat registration failed: " + ex.Message);
                ready = false;
            }
        }

        private static void ResolveOptionalAoeMembers()
        {
            try
            {
                Type verbPropertiesAbilityType = AccessTools.TypeByName("AbilityUser.VerbProperties_Ability");
                Type targetAoEPropertiesType = AccessTools.TypeByName("AbilityUser.TargetAoEProperties");

                targetAoEPropertiesField = verbPropertiesAbilityType != null
                    ? AccessTools.Field(verbPropertiesAbilityType, "TargetAoEProperties")
                    : null;
                aoeRangeField = targetAoEPropertiesType != null
                    ? AccessTools.Field(targetAoEPropertiesType, "range")
                    : null;

                if (verbPropertiesAbilityType != null && targetAoEPropertiesField == null)
                    ModLogger.Error("JecsAbilityCompat: AbilityUser.VerbProperties_Ability.TargetAoEProperties " +
                        "did not resolve; AoE radius will not be announced during ability targeting.");
                if (targetAoEPropertiesType != null && aoeRangeField == null)
                    ModLogger.Error("JecsAbilityCompat: AbilityUser.TargetAoEProperties.range did not resolve; " +
                        "AoE radius will not be announced during ability targeting.");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCompat: optional AoE-radius members failed to resolve: {ex.Message}");
            }
        }

        /// <summary>
        /// Reuses GizmoHotkeyShiftPatch's existing Transpiler verbatim against
        /// two more hand-rolled GizmoOnGUIInt copies that never go through
        /// vanilla Command.GizmoOnGUIInt: JecsTools' own Command_PawnAbility,
        /// and (when RimWorld of Magic is also installed) TorannMagic's
        /// autocast redraw, which reroutes TM_* ability gizmos ahead of both
        /// copies via its own GizmoOnGUIInt prefix — irrelevant here since a
        /// transpiler rewrites the method body itself, not which body runs.
        /// Each target is resolved and patched independently; either or both
        /// may be absent.
        /// </summary>
        private static void RegisterHotkeyShiftGate(Harmony harmony)
        {
            try
            {
                MethodInfo jecsGizmoOnGui = AccessTools.Method(commandPawnAbilityType, "GizmoOnGUIInt");
                if (jecsGizmoOnGui != null)
                {
                    harmony.Patch(jecsGizmoOnGui,
                        transpiler: new HarmonyMethod(typeof(GizmoHotkeyShiftPatch), nameof(GizmoHotkeyShiftPatch.Transpiler)));
                }
                else
                {
                    ModLogger.Error("JecsAbilityCompat: AbilityUser.Command_PawnAbility.GizmoOnGUIInt did not " +
                        "resolve; its hotkey will not require Shift.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCompat: hotkey shift gate for Command_PawnAbility failed: {ex.Message}");
            }

            try
            {
                Type tmActionType = AccessTools.TypeByName("TorannMagic.TM_Action");
                if (tmActionType == null)
                    return; // RimWorld of Magic isn't loaded; nothing to do.

                MethodInfo drawAutoCast = AccessTools.Method(tmActionType, "DrawAutoCastForGizmo");
                if (drawAutoCast != null)
                {
                    harmony.Patch(drawAutoCast,
                        transpiler: new HarmonyMethod(typeof(GizmoHotkeyShiftPatch), nameof(GizmoHotkeyShiftPatch.Transpiler)));
                }
                else
                {
                    ModLogger.Error("JecsAbilityCompat: TorannMagic.TM_Action.DrawAutoCastForGizmo did not " +
                        "resolve; RimWorld of Magic's autocast gizmo hotkeys will not require Shift.");
                }
            }
            catch (Exception ex) when (ex is TypeLoadException || ex.InnerException is TypeLoadException)
            {
                ModLogger.Warning($"JecsAbilityCompat: TM_Action references a type its own assemblies lack, so the installed RimWorld of Magic does not match this game version; hotkey shift gate skipped. {ex.Message}");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCompat: hotkey shift gate for TM_Action failed: {ex.Message}");
            }
        }

        public static void RegisterGizmoHandlers()
        {
            if (!ready)
                return;

            try
            {
                GizmoHandlerRegistry.Register(commandPawnAbilityType,
                    new JecsAbilityCommandHandler(commandPawnAbilityType, commandPawnAbilityField,
                        cooldownTicksLeftProperty, maxCastingTicksProperty));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCompat.RegisterGizmoHandlers failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Vehicle B: consults PawnAbility.CanCastPowerCheck (the same gate
        /// TryCastAbility runs internally, called separately here only to
        /// recover its refusal reason, which TryCastAbility itself discards)
        /// and then PawnAbility.TryCastAbility — never a hand-copied
        /// reimplementation of either. Returns false when
        /// <paramref name="targetingSource"/> isn't a JecsTools ability verb
        /// at all (the caller falls back to its own vanilla path); true
        /// otherwise, with <paramref name="cast"/>/<paramref name="refusalReason"/>
        /// reporting the outcome.
        /// </summary>
        public static bool TryCastViaAbility(ITargetingSource targetingSource, LocalTargetInfo target,
            out bool cast, out string refusalReason)
        {
            cast = false;
            refusalReason = null;

            if (!ready || targetingSource == null || !verbUseAbilityType.IsInstanceOfType(targetingSource))
                return false;

            try
            {
                object pawnAbility = abilityProperty.GetValue(targetingSource);
                if (pawnAbility == null)
                    return true; // It IS a JecsTools ability verb, but has no PawnAbility to cast.

                // SHARP TRAP: touch the Verb property getter (which lazily builds the
                // private verb field) BEFORE CanCastPowerCheck, which reads that private
                // field directly and NREs if it was never built.
                pawnAbilityVerbProperty.GetValue(pawnAbility);

                object[] canCastArgs = { playerContext, null };
                bool canCast = (bool)canCastPowerCheckMethod.Invoke(pawnAbility, canCastArgs);
                string reason = canCastArgs[1] as string;

                if (!canCast)
                {
                    refusalReason = string.IsNullOrEmpty(reason) ? null : reason;
                    return true;
                }

                bool result = (bool)tryCastAbilityMethod.Invoke(pawnAbility, new object[] { playerContext, target });
                cast = result;
                if (!result)
                {
                    refusalReason = string.IsNullOrEmpty(reason)
                        ? (string)"RimWorldAccess.Compat.JecsAbility.CannotCastNow".Translate()
                        : reason;
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCompat.TryCastViaAbility failed: {ex.Message}");
                cast = false;
                refusalReason = null;
                return false; // Fall back to the caller's own vanilla path rather than eat the input.
            }
        }

        /// <summary>
        /// Postfix on Command_PawnAbility.ProcessInput: the mod's own body
        /// wrote Targeter's private fields directly (BeginTargetingWithVerb),
        /// so vanilla's Targeter.BeginTargeting never ran and
        /// AbilityTargetingPatch's postfixes never fired — the targeting
        /// session (if one started) is otherwise completely silent. Opens
        /// GenericTargetingState itself with an explicit label/AoE override:
        /// GenericTargetingState.ExtractLabel would read the ability verb's
        /// (normally blank) verbProps.label and fall back to the CASTER's own
        /// name, and ExtractAoeRadius would call the verb's
        /// HighlightFieldRadiusAroundTarget override, which always returns a
        /// non-zero fallback (defaultProjectile explosion radius, or 1) even
        /// for abilities that aren't AoE at all.
        /// </summary>
        public static void ProcessInputPostfix(object __instance)
        {
            if (!ready || __instance == null || !commandPawnAbilityType.IsInstanceOfType(__instance))
                return;

            try
            {
                Targeter targeter = Find.Targeter;
                if (targeter == null || !targeter.IsTargeting)
                    return; // TargetSelf immediate-cast case, or a disabled gizmo: nothing to announce.

                object verbObj = commandVerbField.GetValue(__instance);
                if (verbObj == null || !ReferenceEquals(targeter.targetingSource, verbObj))
                    return; // Not (or no longer) our targeting session.

                if (!(verbObj is Verb verb))
                    return;

                string label = ((Command)__instance).LabelCap;
                float aoeRadius = ExtractAoeRadius(verb);

                if (GenericTargetingState.IsActive)
                    GenericTargetingState.Close();
                GenericTargetingState.Open(verb, label, aoeRadius);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCompat.ProcessInputPostfix failed: {ex.Message}");
            }
        }

        private static float ExtractAoeRadius(Verb verb)
        {
            try
            {
                if (targetAoEPropertiesField == null || aoeRangeField == null)
                    return 0f;

                object verbPropsObj = verb.verbProps;
                if (verbPropsObj == null)
                    return 0f;

                object targetAoEProps = targetAoEPropertiesField.GetValue(verbPropsObj);
                if (targetAoEProps == null)
                    return 0f;

                return (int)aoeRangeField.GetValue(targetAoEProps);
            }
            catch
            {
                return 0f;
            }
        }
    }
}
