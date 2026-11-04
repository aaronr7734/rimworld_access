using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    internal sealed partial class ClLoadoutEditorScope
    {
        // ---- Loadout statistics (Dialog_LoadoutEditor.DrawStatistics) ----

        private void BuildStatistics()
        {
            List<object> carried = ClCompat.Editor.CarriedItemsWith(loadout, shownState);
            IList itemList = ClCompat.Editor.ItemList(carried);
            string weight = "RimWorldAccess.Compat.CompositableLoadouts.Editor.WeightValue".Translate(
                ClCompat.Editor.Mass(pawn, itemList).ToString("0.#"), ClCompat.Editor.Capacity(pawn, itemList).ToStringMass());
            var weightTip = new List<string>();
            if (ClCompat.Editor.UnboundedEncumbrance(pawn, itemList) > 1f)
            {
                weightTip.Add("RimWorldAccess.Compat.CompositableLoadouts.Editor.OverCapacity".Translate());
            }
            weightTip.Add(CompatText.ModText("CompositableLoadouts.WeightOverCapacity"));
            statisticsRows.Add(new Row
            {
                Label = LabelValue(CompatText.ModText("CompositableLoadouts.Weight"), weight),
                Extras = CompatText.JoinSentences(weightTip),
            });

            // The worn apparel, then every carried non-apparel item, each once, as DrawStatistics gathers them.
            var sources = new List<KeyValuePair<object, ThingDef>>();
            foreach (ClCompat.Editor.WornApparel worn in ClCompat.Editor.HypotheticalWornApparel(loadout, shownState, pawn.RaceProps.body))
            {
                AddSource(sources, worn.Item, worn.Def);
            }
            foreach (object item in carried)
            {
                if (!ClCompat.ItemIsApparel(item))
                {
                    AddSource(sources, item, ClCompat.ItemRepresentativeDef(item));
                }
            }

            var stats = new List<StatDef>();
            var contributors = new Dictionary<StatDef, List<KeyValuePair<object, ThingDef>>>();
            foreach (KeyValuePair<object, ThingDef> source in sources)
            {
                if (source.Value?.equippedStatOffsets == null)
                {
                    continue;
                }
                foreach (StatModifier modifier in source.Value.equippedStatOffsets)
                {
                    if (!contributors.TryGetValue(modifier.stat, out List<KeyValuePair<object, ThingDef>> list))
                    {
                        list = new List<KeyValuePair<object, ThingDef>>();
                        contributors.Add(modifier.stat, list);
                        stats.Add(modifier.stat);
                    }
                    list.Add(source);
                }
            }
            if (stats.Count == 0)
            {
                return;
            }

            statisticsRows.Add(new Row
            {
                Label = CompatText.ModText("CompositableLoadouts.EquippedStatOffsets").Trim(),
                Extras = CompatText.ModText("CompositableLoadouts.EquippedStatOffsetsDesc").Trim(),
            });
            foreach (StatDef stat in stats)
            {
                List<KeyValuePair<object, ThingDef>> items = contributors[stat];
                float total = items.Sum(entry => entry.Value.equippedStatOffsets.GetStatOffsetFromList(stat));
                // Icons are laid right to left in ascending offset, so they read left to right descending.
                List<KeyValuePair<object, ThingDef>> ordered = items
                    .OrderBy(entry => entry.Value.equippedStatOffsets.GetStatOffsetFromList(stat)).ToList();
                ordered.Reverse();
                var tips = new List<string>();
                foreach (KeyValuePair<object, ThingDef> entry in ordered)
                {
                    tips.Add("RimWorldAccess.Compat.CompositableLoadouts.Editor.StatContribution".Translate(
                        ClCompat.ItemLabel(entry.Key),
                        stat.Worker.ValueToString(entry.Value.equippedStatOffsets.GetStatOffsetFromList(stat), false)));
                }
                statisticsRows.Add(new Row
                {
                    Label = LabelValue(stat.LabelCap, stat.Worker.ValueToString(total, false)),
                    Extras = CompatText.JoinSentences(tips),
                });
            }
        }

        private static void AddSource(List<KeyValuePair<object, ThingDef>> sources, object item, ThingDef def)
        {
            if (item == null || def == null)
            {
                return;
            }
            foreach (KeyValuePair<object, ThingDef> existing in sources)
            {
                if (existing.Key == item && existing.Value == def)
                {
                    return;
                }
            }
            sources.Add(new KeyValuePair<object, ThingDef>(item, def));
        }

        private static string LabelValue(string label, string value)
        {
            return "RimWorldAccess.Compat.CompositableLoadouts.Editor.LabelValue".Translate(label, value);
        }

        // ---- Pawn stats panel (Panel_PawnStats.Draw) ----

        private void BuildPawnStats()
        {
            pawnStatsRows.Add(new Row
            {
                Label = "RimWorldAccess.Compat.CompositableLoadouts.Editor.PreviousPawn".Translate(),
                Hotkey = ChordDisplay(PreviousPawnActionId),
                Role = ElementRole.Button,
                Activate = () => SwitchPawn(next: false),
            });
            pawnStatsRows.Add(new Row { Label = pawn.LabelShort });
            pawnStatsRows.Add(new Row
            {
                Label = "RimWorldAccess.Compat.CompositableLoadouts.Editor.NextPawn".Translate(),
                Hotkey = ChordDisplay(NextPawnActionId),
                Role = ElementRole.Button,
                Activate = () => SwitchPawn(next: true),
            });

            pawnStatsRows.Add(new Row { Label = CompatText.ModText("CompositableLoadouts.TopFourSkills") });
            if (pawn.skills?.skills != null)
            {
                foreach (SkillRecord skill in pawn.skills.skills.OrderByDescending(s => s.Level).Take(4))
                {
                    pawnStatsRows.Add(new Row
                    {
                        Label = LabelValue(skill.def.skillLabel.CapitalizeFirst(), skill.Level.ToString()),
                        Extras = CompatText.JoinSentences(new List<string>
                        {
                            PawnSkillsTableHelper.PassionLabel(skill.passion),
                            CompatText.Flatten(PawnColumnWorker_AccessSkill.SkillDescription?.Invoke(skill)),
                        }),
                    });
                }
            }

            pawnStatsRows.Add(new Row { Label = CompatText.ModText("CompositableLoadouts.PawnTraits") });
            if (pawn.story?.traits != null)
            {
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    pawnStatsRows.Add(new Row { Label = trait.LabelCap, Extras = CompatText.Flatten(trait.TipString(pawn)) });
                }
            }

            pawnStatsRows.Add(new Row { Label = CompatText.ModText("CompositableLoadouts.ApparelWhichCanBeWorn") });
            BodyDef body = pawn.RaceProps.body;
            List<ThingDef> worn = ClCompat.Editor.HypotheticalWornApparel(loadout, shownState, body)
                .Select(w => w.Def).Where(def => def != null).ToList();
            foreach (ThingDef apparel in ClCompat.Editor.ApparelCanFitOnBody(body, worn))
            {
                pawnStatsRows.Add(new Row { Label = apparel.LabelCap });
            }
        }
    }
}
