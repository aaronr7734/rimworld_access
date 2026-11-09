using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The material button Alpha Memes' funeral framework draws into the ritual dialog
    /// (<c>Dialog_BeginRitual_DoWindowContents_Patch</c>, a DrawQualityFactors postfix), presented
    /// as a Buttons-region row of the ritual scope. The ritual scope reads the dialog's data
    /// model rather than its widgets, so a button a mod paints into it never reaches the player
    /// otherwise.
    /// </summary>
    internal static class AlphaMemesFuneralStuffCompat
    {
        private static readonly FieldInfo ritualField = AccessTools.Field(typeof(Dialog_BeginRitual), "ritual");
        private static readonly Type preceptExtensionType;
        private static readonly Type outcomeExtensionType;
        private static readonly Type behaviorType;
        private static readonly FieldInfo outcomeSpawnersField;
        private static readonly FieldInfo spawnerStuffDefField;
        private static readonly FieldInfo spawnerStuffOptionsField;
        private static readonly FieldInfo spawnerStuffCountField;
        private static readonly MethodInfo findStuffMethod;
        private static readonly FieldInfo behaviorStuffField;
        private static readonly FieldInfo behaviorStuffCountField;
        private static readonly bool ready;

        static AlphaMemesFuneralStuffCompat()
        {
            var surface = new ReflectionSurface("AlphaMemesFuneralStuffCompat");
            preceptExtensionType = surface.Type("AlphaMemes.FuneralPreceptExtension");
            outcomeExtensionType = surface.Type("AlphaMemes.OutcomeEffectExtension");
            behaviorType = surface.Type("AlphaMemes.RitualBehaviorWorker_FuneralFramework");
            Type spawnerType = surface.Type("AlphaMemes.FuneralFramework_ThingToSpawn");
            outcomeSpawnersField = surface.Field(outcomeExtensionType, "outcomeSpawners");
            spawnerStuffDefField = surface.Field(spawnerType, "stuffDefToSpawn");
            spawnerStuffOptionsField = surface.Field(spawnerType, "stuffOptions");
            spawnerStuffCountField = surface.Field(spawnerType, "stuffCount");
            findStuffMethod = surface.Method(spawnerType, "FindStuffForThing", new[] { typeof(bool) });
            behaviorStuffField = surface.Field(behaviorType, "stuffToUse");
            behaviorStuffCountField = surface.Field(behaviorType, "stuffCount");
            ready = surface.Ready && ritualField != null;
        }

        public static void Register()
        {
            if (ready)
                CompatScreenActions.Register("lord-job-dialog", MaterialRow);
        }

        private static ScreenAction MaterialRow()
        {
            if (!(LordJobDialogState.Adapter?.Dialog is Dialog_BeginRitual dialog) || Find.IdeoManager.classicMode)
                return null;
            var ritual = ritualField.GetValue(dialog) as Precept_Ritual;
            if (ritual?.outcomeEffect == null || !behaviorType.IsInstanceOfType(ritual.behavior)
                || !HasExtension(ritual.def, preceptExtensionType))
                return null;
            DefModExtension outcomeExtension = ritual.outcomeEffect.def.modExtensions?.FirstOrDefault(e => outcomeExtensionType.IsInstanceOfType(e));
            if (outcomeExtension == null)
                return null;

            Dictionary<Thing, int> options = StuffOptions(outcomeExtension);
            var stuff = behaviorStuffField.GetValue(ritual.behavior) as ThingDef;
            if (options.Count == 0 || stuff == null)
                return null;

            object behavior = ritual.behavior;
            int count = (int)behaviorStuffCountField.GetValue(behavior);
            string label = "RimWorldAccess.Compat.AlphaMemes.FuneralStuff".Translate(count, stuff.label);
            return new ScreenAction(label, () => OpenMenu(behavior, options));
        }

        private static bool HasExtension(Def def, Type extensionType)
        {
            return def.modExtensions != null && def.modExtensions.Any(e => extensionType.IsInstanceOfType(e));
        }

        /// <summary>The postfix's own option scan: per spawner with a stuff def, refresh its options and take each def's first map stack.</summary>
        private static Dictionary<Thing, int> StuffOptions(DefModExtension outcomeExtension)
        {
            var options = new Dictionary<Thing, int>();
            if (!(outcomeSpawnersField.GetValue(outcomeExtension) is IList spawners) || Find.CurrentMap == null)
                return options;
            foreach (object spawner in spawners)
            {
                if (spawnerStuffDefField.GetValue(spawner) == null)
                    continue;
                findStuffMethod.Invoke(spawner, new object[] { true });
                var allowed = (List<ThingDef>)spawnerStuffOptionsField.GetValue(spawner);
                int stuffCount = (int)spawnerStuffCountField.GetValue(spawner);
                foreach (Thing thing in Find.CurrentMap.listerThings.AllThings.Where(t => allowed.Contains(t.def)))
                {
                    if (!options.Keys.Any(k => k.def == thing.def))
                        options.Add(thing, stuffCount);
                }
            }
            return options;
        }

        private static void OpenMenu(object behavior, Dictionary<Thing, int> options)
        {
            var menu = new List<FloatMenuOption>();
            foreach (KeyValuePair<Thing, int> option in options)
            {
                ThingDef def = option.Key.def;
                int count = option.Value;
                menu.Add(new FloatMenuOption(
                    "RimWorldAccess.Compat.AlphaMemes.FuneralStuff".Translate(count, option.Key.LabelCapNoCount),
                    delegate
                    {
                        // MUTATION-C: mirrors AlphaMemes Dialog_BeginRitual_DoWindowContents_Patch's float-menu
                        // option; its delegate is a closure built inside the draw postfix, unreachable from here.
                        behaviorStuffField.SetValue(behavior, def);
                        behaviorStuffCountField.SetValue(behavior, count);
                    }));
            }
            WindowlessFloatMenuState.Open(menu, colonistOrders: false);
        }
    }
}
