using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// S10b' -- promotes the S10a def editor's remaining fields (the ThingDef ballistics/ammo/sound
    /// cluster, both defs' tag-list fields, GeneDef's raw float/bool sliders, and both defs' label
    /// fields) from absent-or-read-only to fully editable, WITHOUT any raw def field write.
    ///
    /// THE VEHICLE: <c>CharacterEditor.PresetObject</c>/<c>PresetGene</c> are the mod's own
    /// persistence classes -- their <c>PresetObject(ThingDef)</c>/<c>PresetGene(GeneDef)</c> ctor
    /// CAPTURES every parameter of the live def into <c>dicParams</c> (a
    /// <c>SortedDictionary&lt;Param, string&gt;</c> keyed by the mod's own nested <c>Param</c>
    /// enum), and the private instance method <c>FromDictionary()</c> APPLIES the whole dictionary
    /// back onto the def -- the exact path the mod runs at every game start to reapply saved
    /// modifications (<c>PresetObject(string)</c>/<c>PresetGene(string)</c>). One shared write path
    /// implements every field promoted here: capture the def into a fresh preset instance, overwrite
    /// exactly one dictionary entry (resolved BY ENUM NAME via <see cref="Enum.Parse"/>, never by
    /// ordinal, so a future reordering of the mod's enum cannot silently target the wrong field),
    /// invoke <c>FromDictionary</c>, report its bool result. Persistence rides the S10a "Save
    /// modifications" action unchanged (<see cref="CharEditorDefEditorCompat.SaveDefModification"/>
    /// -&gt; <c>PresetObject(def).SaveCustom()</c> captures the now-edited live def).
    ///
    /// <c>PresetObject.Param</c>/<c>PresetGene.Param</c> are simple sequential enums (no explicit
    /// underlying values), so <see cref="Enum.Parse"/> on the literal member name always succeeds
    /// and is immune to the mod reordering members between versions -- <see cref="Preset.LoadModification"/>
    /// itself (mirrored below only for drift detection, never invoked) reconstructs by ordinal
    /// position, which is exactly why the SAVE format must stay in enum-declaration order; capture
    /// via the ctor already guarantees that, and we only ever overwrite ONE already-captured entry.
    /// </summary>
    internal static partial class CharEditorDefEditorCompat
    {
        private static readonly LazyReflectionGate objectPresetGate =
            new LazyReflectionGate("CharEditorDefEditorCompat object preset", ResolveObjectPreset);
        private static readonly LazyReflectionGate genePresetGate =
            new LazyReflectionGate("CharEditorDefEditorCompat gene preset", ResolveGenePreset);

        private static Type presetObjectType;
        private static Type presetGeneType;
        private static Type presetObjectParamType;
        private static Type presetGeneParamType;

        private static ConstructorInfo presetObjectCtor;
        private static ConstructorInfo presetGeneCtor;
        private static MethodInfo presetObjectFromDictionary;
        private static MethodInfo presetGeneFromDictionary;
        private static FieldInfo presetObjectDicParamsField;
        private static FieldInfo presetGeneDicParamsField;

        // ThingTool's own candidate HashSet properties for the ballistics/sound/ammo cluster --
        // the mod's own decision objects (DialogObjects.cs binds these same properties for the
        // sighted widgets), bound here rather than recomputed.
        private static PropertyInfo thingAllBulletsProp, thingAllDamageDefsProp, thingAllEffecterDefsProp,
            thingAllGunRelatedSoundsProp, thingAllGunShotSoundsProp, thingAllGasTypesProp,
            thingAllTechLevelsProp, thingAllTradeabilitiesProp;

        // Optional (not gating ObjectPresetReady): CEditor.IsCombatExtendedActive gates the CE fuel
        // capacity/reload time rows the same way DialogObjects.cs:956/964 gates its own widgets.
        private static PropertyInfo ceditorIsCombatExtendedActiveProp;

        private const BindingFlags PresetNestedFlags = BindingFlags.NonPublic | BindingFlags.Public;

        public static bool ObjectPresetReady { get { EnsurePresetInit(); return objectPresetGate.Ensure(); } }
        public static bool GenePresetReady { get { EnsurePresetInit(); return genePresetGate.Ensure(); } }

        private static void EnsurePresetInit()
        {
            objectPresetGate.Ensure();
            genePresetGate.Ensure();
        }

        // Two independent capabilities, each with its own gate: a reshaped PresetGene must not
        // disable the object-preset writes, or vice versa. Both anchor on CEditor so a single
        // missing preset type still reports instead of reading as "mod absent".
        private static bool ResolveObjectPreset(ReflectionSurface objectSurface)
        {
            if (!CharEditorCompat.ModPresent) return false;
            Type ceditorType2 = CharEditorCompat.EditorCore.CEditorType;
            objectSurface.Supplied("CharacterEditor.CEditor", ceditorType2);

            presetObjectType = objectSurface.Type("CharacterEditor.PresetObject");
            presetObjectParamType = objectSurface.Supplied("PresetObject.Param",
                presetObjectType?.GetNestedType("Param", PresetNestedFlags));
            presetObjectCtor = objectSurface.Constructor(presetObjectType, new[] { typeof(ThingDef) });
            presetObjectFromDictionary = objectSurface.Method(presetObjectType, "FromDictionary", Type.EmptyTypes);
            presetObjectDicParamsField = objectSurface.Field(presetObjectType, "dicParams");

            Type thingToolType = objectSurface.Type("CharacterEditor.ThingTool");
            thingAllBulletsProp = objectSurface.Property(thingToolType, "AllBullets");
            thingAllDamageDefsProp = objectSurface.Property(thingToolType, "AllDamageDefs");
            thingAllEffecterDefsProp = objectSurface.Property(thingToolType, "AllEffecterDefs");
            thingAllGunRelatedSoundsProp = objectSurface.Property(thingToolType, "AllGunRelatedSounds");
            thingAllGunShotSoundsProp = objectSurface.Property(thingToolType, "AllGunShotSounds");
            thingAllGasTypesProp = objectSurface.Property(thingToolType, "AllGasTypes");
            thingAllTechLevelsProp = objectSurface.Property(thingToolType, "AllTechLevels");
            thingAllTradeabilitiesProp = objectSurface.Property(thingToolType, "AllTradeabilities");

            // Version-optional, deliberately outside both surfaces: a non-CE install simply lacks
            // it, so its absence must not disable the object-preset writes.
            ceditorIsCombatExtendedActiveProp = ceditorType2 == null ? null
                : AccessTools.Property(ceditorType2, "IsCombatExtendedActive");

            return objectSurface.Ready;
        }

        private static bool ResolveGenePreset(ReflectionSurface geneSurface)
        {
            if (!CharEditorCompat.ModPresent) return false;
            Type ceditorType2 = CharEditorCompat.EditorCore.CEditorType;
            geneSurface.Supplied("CharacterEditor.CEditor", ceditorType2);

            presetGeneType = geneSurface.Type("CharacterEditor.PresetGene");
            presetGeneParamType = geneSurface.Supplied("PresetGene.Param",
                presetGeneType?.GetNestedType("Param", PresetNestedFlags));
            presetGeneCtor = geneSurface.Constructor(presetGeneType, new[] { typeof(GeneDef) });
            presetGeneFromDictionary = geneSurface.Method(presetGeneType, "FromDictionary", Type.EmptyTypes);
            presetGeneDicParamsField = geneSurface.Field(presetGeneType, "dicParams");

#if DEBUG
            objectPresetGate.Ensure();
            RegisterPresetMirrors();
#endif
            return geneSurface.Ready;
        }

#if DEBUG
        private static void RegisterPresetMirrors()
        {
            if (presetObjectType != null)
                ShellDev.RegisterMirror("chared-presetobject-fromdictionary", presetObjectType, "FromDictionary", null,
                    "76b871b58b6a1c039aea4e60a86a0ebea917981184d5cb0a9d2c4878ed59a15c");
            if (presetGeneType != null)
                ShellDev.RegisterMirror("chared-presetgene-fromdictionary", presetGeneType, "FromDictionary", null,
                    "0eee4b226a92b871493a1524da4b4e7ab30c452b4b03e06897153c82c25452cd");
            Type presetType = AccessTools.TypeByName("CharacterEditor.Preset");
            if (presetType != null)
                ShellDev.RegisterMirror("chared-preset-loadmodification", presetType, "LoadModification", null,
                    "5d4f0b59527036ec708d82c44697fbee3817978ccb556c0a123d4b899190a39f");
        }
#endif

        /// <summary>
        /// Writes ONE field: capture <paramref name="def"/> into a fresh <c>PresetObject</c>
        /// (repopulating every OTHER param exactly as the mod's own ctor would), overwrite the
        /// named param, invoke the mod's own <c>FromDictionary</c> to apply the whole dictionary
        /// back (vehicle A -- byte-for-byte the mod's own save/reload path). Returns its bool.
        /// </summary>
        public static bool ApplyObjectParam(ThingDef def, string paramName, string newValue)
        {
            if (!ObjectPresetReady || def == null) return false;
            try
            {
                object preset = presetObjectCtor.Invoke(new object[] { def });
                if (!(presetObjectDicParamsField.GetValue(preset) is IDictionary dict) || dict.Count == 0) return false;
                object key = Enum.Parse(presetObjectParamType, paramName);
                dict[key] = newValue ?? "";
                return presetObjectFromDictionary.Invoke(preset, null) is bool ok && ok;
            }
            catch (Exception ex) { Fail("ApplyObjectParam:" + paramName, ex); return false; }
        }

        /// <summary>Symmetric write for GeneDef via <c>PresetGene</c>.</summary>
        public static bool ApplyGeneParam(GeneDef def, string paramName, string newValue)
        {
            if (!GenePresetReady || def == null) return false;
            try
            {
                object preset = presetGeneCtor.Invoke(new object[] { def });
                if (!(presetGeneDicParamsField.GetValue(preset) is IDictionary dict) || dict.Count == 0) return false;
                object key = Enum.Parse(presetGeneParamType, paramName);
                dict[key] = newValue ?? "";
                return presetGeneFromDictionary.Invoke(preset, null) is bool ok && ok;
            }
            catch (Exception ex) { Fail("ApplyGeneParam:" + paramName, ex); return false; }
        }

        /// <summary>Reads one captured param in the mod's own canonical string form (capture-time snapshot, not a live re-read of the def).</summary>
        public static string ReadObjectParam(ThingDef def, string paramName)
        {
            if (!ObjectPresetReady || def == null) return "";
            try
            {
                object preset = presetObjectCtor.Invoke(new object[] { def });
                if (!(presetObjectDicParamsField.GetValue(preset) is IDictionary dict)) return "";
                object key = Enum.Parse(presetObjectParamType, paramName);
                return dict.Contains(key) ? (dict[key] as string ?? "") : "";
            }
            catch (Exception ex) { Fail("ReadObjectParam:" + paramName, ex); return ""; }
        }

        /// <summary>Symmetric read for GeneDef.</summary>
        public static string ReadGeneParam(GeneDef def, string paramName)
        {
            if (!GenePresetReady || def == null) return "";
            try
            {
                object preset = presetGeneCtor.Invoke(new object[] { def });
                if (!(presetGeneDicParamsField.GetValue(preset) is IDictionary dict)) return "";
                object key = Enum.Parse(presetGeneParamType, paramName);
                return dict.Contains(key) ? (dict[key] as string ?? "") : "";
            }
            catch (Exception ex) { Fail("ReadGeneParam:" + paramName, ex); return ""; }
        }

        private static HashSet<T> ReadCandidateSet<T>(PropertyInfo prop)
        {
            if (!ObjectPresetReady || prop == null) return null;
            try { return prop.GetValue(null, null) as HashSet<T>; }
            catch (Exception ex) { Fail("ReadCandidateSet:" + prop.Name, ex); return null; }
        }

        public static HashSet<ThingDef> ThingAllBullets() => ReadCandidateSet<ThingDef>(thingAllBulletsProp);
        public static HashSet<DamageDef> ThingAllDamageDefs() => ReadCandidateSet<DamageDef>(thingAllDamageDefsProp);
        public static HashSet<EffecterDef> ThingAllEffecterDefs() => ReadCandidateSet<EffecterDef>(thingAllEffecterDefsProp);
        public static HashSet<SoundDef> ThingAllGunRelatedSounds() => ReadCandidateSet<SoundDef>(thingAllGunRelatedSoundsProp);
        public static HashSet<SoundDef> ThingAllGunShotSounds() => ReadCandidateSet<SoundDef>(thingAllGunShotSoundsProp);
        public static HashSet<GasType?> ThingAllGasTypes() => ReadCandidateSet<GasType?>(thingAllGasTypesProp);
        public static HashSet<TechLevel> ThingAllTechLevels() => ReadCandidateSet<TechLevel>(thingAllTechLevelsProp);
        public static HashSet<Tradeability> ThingAllTradeabilities() => ReadCandidateSet<Tradeability>(thingAllTradeabilitiesProp);

        /// <summary>Best-effort; false (rather than faulting the whole facade) if CEditor cannot be resolved.</summary>
        public static bool IsCombatExtendedActive()
        {
            EnsurePresetInit();
            try { return ceditorIsCombatExtendedActiveProp?.GetValue(null, null) is bool b && b; }
            catch (Exception ex) { Fail("IsCombatExtendedActive", ex); return false; }
        }
    }
}
