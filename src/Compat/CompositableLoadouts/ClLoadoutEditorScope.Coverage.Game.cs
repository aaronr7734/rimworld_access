using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    internal sealed partial class ClLoadoutEditorScope
    {
        // ---- Coverage panel (Panel_ShowCoverage): its two tabs, the info icon, then the shown tab's grid ----

        private void BuildCoverage()
        {
            bool bodyParts = ClCompat.Editor.CoverageOnBodyParts(dialog);
            coverageRows.Add(TabRow(CompatText.ModText("CompositableLoadouts.ApparelTab"), selected: !bodyParts, bodyParts: false, position: 1));
            coverageRows.Add(TabRow(CompatText.ModText("CompositableLoadouts.BodyPartsTab"), selected: bodyParts, bodyParts: true, position: 2));
            coverageRows.Add(new Row
            {
                Label = "RimWorldAccess.Shell.Glyph.Info".Translate(),
                Extras = CompatText.Flatten(CompatText.ModText(bodyParts
                    ? "CompositableLoadouts.BodyPartCoverageExplanation"
                    : "CompositableLoadouts.CoverageExplanation")),
            });
            if (bodyParts)
            {
                BuildBodyPartRows();
            }
            else
            {
                BuildApparelRows();
            }
        }

        private Row TabRow(string label, bool selected, bool bodyParts, int position)
        {
            return new Row
            {
                Label = label,
                Role = ElementRole.Tab,
                Selected = selected,
                PositionIndex = position,
                PositionCount = 2,
                Activate = () =>
                {
                    ClCompat.Editor.SetCoverageOnBodyParts(dialog, bodyParts);
                    RefreshModel();
                    AnnounceCurrentItem();
                },
            };
        }

        // ---- Apparel tab (ApparelCoverage): one row per body part group, its slots in the grid's column order ----

        private void BuildApparelRows()
        {
            BodyDef body = pawn.RaceProps.body;
            List<ClCompat.Editor.WornApparel> worn = ClCompat.Editor.HypotheticalWornApparel(loadout, shownState, body);
            List<object> activeElements = ClCompat.Editor.ElementsWith(loadout, shownState);
            List<List<object>> categories = ClCompat.Editor.GroupCategories(body)
                .Where(category => category.Count > 0)
                .OrderByDescending(category => ClCompat.Editor.GroupDef(category[0])?.listOrder ?? 0)
                .ToList();
            foreach (List<object> category in categories)
            {
                foreach (object group in category)
                {
                    AddGroupRows(body, group, worn, activeElements);
                }
            }
        }

        private void AddGroupRows(BodyDef body, object group, List<ClCompat.Editor.WornApparel> worn, List<object> activeElements)
        {
            BodyPartGroupDef def = ClCompat.Editor.GroupDef(group);
            if (def != null)
            {
                var slots = new List<string>();
                foreach (ApparelLayerDef layer in ClCompat.Editor.GroupLayers(group).OrderByDescending(l => l.drawOrder))
                {
                    slots.Add(DescribeSlot(body, def, layer, worn, activeElements));
                }
                coverageRows.Add(new Row { Label = def.LabelCap, Extras = CompatText.JoinSentences(slots) });
            }
            foreach (object child in ClCompat.Editor.GroupChildren(group))
            {
                AddGroupRows(body, child, worn, activeElements);
            }
        }

        /// <summary>
        /// One slot box, as ApparelCoverage.DrawBodyPartGroup draws it: empty, the apparel worn there
        /// (with what it blocks), or red with the apparel of every tag that wants it but is blocked.
        /// </summary>
        private static string DescribeSlot(BodyDef body, BodyPartGroupDef group, ApparelLayerDef layer,
            List<ClCompat.Editor.WornApparel> worn, List<object> activeElements)
        {
            ClCompat.Editor.WornApparel wearing = worn.FirstOrDefault(w => w.Def != null && ClCompat.Editor.SlotCovers(body, w.Def, group, layer));
            var wanting = new List<KeyValuePair<object, object>>();
            foreach (object element in activeElements)
            {
                object tag = ClCompat.ElementTag(element);
                if (wearing != null && tag == wearing.Tag)
                {
                    continue;
                }
                foreach (object item in ClCompat.TagItems(tag))
                {
                    if (ClCompat.ItemIsApparel(item)
                        && ClCompat.Editor.ItemDefs(item).Any(d => ClCompat.Editor.SlotCovers(body, d, group, layer)))
                    {
                        wanting.Add(new KeyValuePair<object, object>(tag, item));
                    }
                }
            }
            string layerLabel = layer.LabelCap;
            if (wearing == null && wanting.Count == 0)
            {
                return "RimWorldAccess.Compat.CompositableLoadouts.Editor.SlotEmpty".Translate(layerLabel);
            }
            string wanted = string.Join(", ", wanting.Select(w =>
                "RimWorldAccess.Compat.CompositableLoadouts.Editor.ItemFromTag".Translate(
                    ClCompat.ItemShortLabel(w.Value), ClCompat.TagName(w.Key)).ToString()));
            if (wearing == null)
            {
                return "RimWorldAccess.Compat.CompositableLoadouts.Editor.SlotBlocked".Translate(layerLabel, wanted);
            }
            var parts = new List<string>
            {
                "RimWorldAccess.Compat.CompositableLoadouts.Editor.SlotWorn".Translate(
                    layerLabel, wearing.Def.LabelCap, ClCompat.TagName(wearing.Tag)),
            };
            if (ClCompat.ItemHasAlternatives(wearing.Item))
            {
                parts.Add(AlternativesBadge(wearing.Item));
            }
            if (wanting.Count > 0)
            {
                parts.Add("RimWorldAccess.Compat.CompositableLoadouts.Editor.SlotBlocks".Translate(wanted));
            }
            return CompatText.JoinSentences(parts);
        }

        // ---- Body parts tab (BodyPartCoverage): each part a prosthetic would take or is blocked from ----

        private void BuildBodyPartRows()
        {
            BodyDef body = pawn.RaceProps.body;
            object slots = ClCompat.Editor.HypotheticalProsthetics(loadout, shownState, body);
            var parts = new List<BodyPartRecord>();
            foreach (BodyPartRecord part in body.AllParts)
            {
                if (ClCompat.Editor.PlacementsOn(slots, part).Any(p => !p.Replaced) || ClCompat.Editor.BlockedOn(slots, part).Count > 0)
                {
                    parts.Add(part);
                }
            }
            if (parts.Count == 0)
            {
                coverageRows.Add(new Row { Label = CompatText.ModText("CompositableLoadouts.NoProstheticParts") });
                return;
            }
            foreach (BodyPartRecord part in parts)
            {
                coverageRows.Add(DescribePart(slots, part));
            }
        }

        /// <summary>The part's box (the prosthetics' names, or the first blocked one's) and its tooltip lines, as BodyPartCoverage.DrawCell builds them.</summary>
        private Row DescribePart(object slots, BodyPartRecord part)
        {
            List<ClCompat.Editor.Placement> placements = ClCompat.Editor.PlacementsOn(slots, part);
            List<ClCompat.Editor.WornApparel> blocked = ClCompat.Editor.BlockedOn(slots, part);
            var lines = new List<string>();
            string shown;
            if (placements.Count > 0)
            {
                shown = string.Join(", ", placements.Where(p => p.Def != null).Select(p => p.Def.LabelCap.ToString()).Distinct());
                foreach (ClCompat.Editor.Placement placement in placements)
                {
                    lines.Add(PlacementLine(placement));
                    if (placement.Replaced)
                    {
                        continue;
                    }
                    List<string> covered = Below(part)
                        .Where(p => ClCompat.Editor.PlacementsOn(slots, p).Any(o => o.Replaced && o.Def == placement.Def))
                        .Select(p => p.Label).ToList();
                    if (covered.Count > 0)
                    {
                        lines.Add(CompatText.ModArgs("CompositableLoadouts.ProstheticCovers", string.Join(", ", covered)));
                    }
                }
            }
            else
            {
                shown = "RimWorldAccess.Compat.CompositableLoadouts.Editor.PartBlocked".Translate(
                    ClCompat.ItemRepresentativeDef(blocked[0].Item)?.LabelCap.ToString() ?? ClCompat.ItemLabel(blocked[0].Item));
            }
            if (blocked.Count > 0)
            {
                lines.Add(CompatText.ModText("CompositableLoadouts.ProstheticsBlocked"));
                lines.Add(string.Join(", ", blocked.Select(b =>
                    "RimWorldAccess.Compat.CompositableLoadouts.Editor.BlockedProsthetic".Translate(
                        ClCompat.ItemLabel(b.Item), ClCompat.TagName(b.Tag)).ToString())));
            }

            List<Hediff> current = pawn.health.hediffSet.hediffs
                .Where(h => h.Part == part && (h is Hediff_AddedPart || h is Hediff_Implant || h is Hediff_MissingPart))
                .ToList();
            var planned = new HashSet<HediffDef>(placements.Where(p => p.Installed && p.Def != null)
                .Select(p => ClCompat.Editor.InstalledHediff(p.Def)));
            if (!new HashSet<HediffDef>(current.Select(h => h.def)).SetEquals(planned))
            {
                string labels = current.Count > 0
                    ? string.Join(", ", current.Select(h => h.LabelCap))
                    : CompatText.ModText("CompositableLoadouts.NaturalPart");
                lines.Add(CompatText.ModArgs("CompositableLoadouts.CurrentlyOnPart", labels));
            }
            return new Row { Label = LabelValue(part.LabelCap, shown), Extras = CompatText.JoinSentences(lines) };
        }

        private static string PlacementLine(ClCompat.Editor.Placement placement)
        {
            string def = placement.Def?.LabelCap.ToString() ?? "";
            string tag = ClCompat.TagName(placement.Tag) ?? "";
            if (placement.Installed)
            {
                return CompatText.ModArgs("CompositableLoadouts.ProstheticInstalled", def, tag);
            }
            return placement.Replaced
                ? CompatText.ModArgs("CompositableLoadouts.ProstheticReplaced", def, tag)
                : CompatText.ModArgs("CompositableLoadouts.ProstheticReserved", def, tag);
        }

        private static IEnumerable<BodyPartRecord> Below(BodyPartRecord part)
        {
            foreach (BodyPartRecord child in part.parts)
            {
                yield return child;
                foreach (BodyPartRecord descendant in Below(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
