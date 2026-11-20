using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static partial class ScenarioBuilderState
    {

        /// <summary>Extracts editable fields from a ScenPart using reflection.</summary>
        private static List<PartField> ExtractPartFields(ScenPart part)
        {
            var fields = new List<PartField>();
            Type partType = part.GetType();

            // ScenPart_PlanetLayer.DoEditInterface wraps its ENTIRE body in `if (!hide)`
            // (ScenPart_PlanetLayer.cs L100-113): a hidden layer draws nothing at all, unlike the
            // CanEdit gate, which only disables the type row.
            if (part is ScenPart_PlanetLayer hiddenLayerCheck && hiddenLayerCheck.hide)
                return fields;

            var factionDefField = partType.GetField("factionDef", BindingFlags.NonPublic | BindingFlags.Instance);
            if (factionDefField != null && factionDefField.FieldType == typeof(FactionDef))
            {
                var currentFaction = (FactionDef)factionDefField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Faction".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentFaction?.LabelCap ?? "None".Translate(),
                    Data = GetPlayerFactionOptions(),
                    // MUTATION-C: mirrors ScenPart_PlayerFaction.DoEditInterface's FloatMenu picker (decompiled RimWorld/ScenPart_PlayerFaction.cs:23-39); no gated setter.
                    SetValue = (val) => factionDefField.SetValue(part, val)
                });
            }

            // ScenPart_PlanetLayerFixed has CanEdit = false.
            var layerField = partType.GetField("layer", BindingFlags.Public | BindingFlags.Instance);
            if (layerField != null && layerField.FieldType == typeof(PlanetLayerDef))
            {
                var canEditProp = partType.GetProperty("CanEdit", BindingFlags.NonPublic | BindingFlags.Instance);
                bool canEdit = canEditProp == null || (bool)canEditProp.GetValue(part);

                if (canEdit)
                {
                    var currentLayer = (PlanetLayerDef)layerField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PlanetLayer".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLayer?.LabelCap ?? "None".Translate(),
                        Data = GetPlanetLayerOptions(),
                        // MUTATION-C: mirrors ScenPart_PlanetLayer's type-picker button (decompiled RimWorld/ScenPart_PlanetLayer.cs:168-186); gated above by CanEdit.
                        SetValue = (val) => layerField.SetValue(part, val)
                    });
                }
            }

            var tagField = VanillaAccess.GetField(partType, "tag");
            if (tagField != null && tagField.FieldType == typeof(string) &&
                part is ScenPart_PlanetLayer planetLayerForTag)
            {
                var currentTag = (string)tagField.GetValue(part) ?? "";
                string tagValidityNote = PlanetLayerTagValidityNote(planetLayerForTag, currentTag);
                string tagDisplay = string.IsNullOrEmpty(currentTag) ? (string)"RimWorldAccess.ScenarioBuilder.Value.Empty".Translate() :
                    (currentTag.Length > 60 ? currentTag.Substring(0, 60) + "..." : currentTag);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.LayerTag".Translate(),
                    Type = FieldType.Text,
                    CurrentValue = string.IsNullOrEmpty(tagValidityNote) ? tagDisplay : $"{tagDisplay} ({tagValidityNote})",
                    Data = currentTag,
                    SetValue = (val) =>
                    {
                        string oldTag = (string)tagField.GetValue(part) ?? "";
                        string newTag = val as string ?? "";
                        // MUTATION-C: mirrors ScenPart_PlanetLayer.DoTagRow's tag field (decompiled RimWorld/ScenPart_PlanetLayer.cs:189-217); no gated setter.
                        tagField.SetValue(part, newTag);
                        // MUTATION-C: mirrors ScenPart_PlanetLayer.DoTagRow (decompiled
                        // RimWorld/ScenPart_PlanetLayer.cs L189-217) — vanilla renames every
                        // OTHER layer's LayerConnection.tag that pointed at the old tag text
                        // whenever a layer's own tag changes, so connections never dangle.
                        // Not a callable vanilla method (DoTagRow only runs inside a draw
                        // pass); the walk below reads the SAME two fields DoTagRow touches.
                        if (!string.IsNullOrEmpty(newTag) && newTag != oldTag && currentScenario != null)
                        {
                            foreach (var otherPart in currentScenario.AllParts)
                            {
                                if (otherPart == part || !(otherPart is ScenPart_PlanetLayer)) continue;
                                var connField = VanillaAccess.GetField(otherPart.GetType(), "connections");
                                var conns = connField?.GetValue(otherPart) as System.Collections.IEnumerable;
                                if (conns == null) continue;
                                foreach (var conn in conns)
                                {
                                    var connTagField = VanillaAccess.GetField(conn.GetType(), "tag");
                                    if (connTagField != null && (connTagField.GetValue(conn) as string) == oldTag)
                                        // MUTATION-C: same DoTagRow's cross-layer connection-tag rename walk (decompiled RimWorld/ScenPart_PlanetLayer.cs:189-217).
                                        connTagField.SetValue(conn, newTag);
                                }
                            }
                        }
                    }
                });
            }

            var settingsDefField = partType.GetField("settingsDef", BindingFlags.Public | BindingFlags.Instance);
            if (settingsDefField != null && part is ScenPart_PlanetLayer)
            {
                var currentSettings = settingsDefField.GetValue(part);
                string currentLabel = (string)"None".Translate();
                if (currentSettings != null)
                {
                    var labelProp = currentSettings.GetType().GetProperty("LabelCap");
                    if (labelProp != null)
                        currentLabel = labelProp.GetValue(currentSettings)?.ToString() ?? "None".Translate();
                }
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.LayerSettings".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentLabel,
                    Data = GetPlanetLayerSettingsOptions(),
                    // MUTATION-C: mirrors ScenPart_PlanetLayer's settings-picker button (decompiled RimWorld/ScenPart_PlanetLayer.cs:223-236).
                    SetValue = (val) => settingsDefField.SetValue(part, val)
                });
            }

            var arriveMethodField = partType.GetField("method", BindingFlags.NonPublic | BindingFlags.Instance);
            if (arriveMethodField != null && arriveMethodField.FieldType == typeof(PlayerPawnsArriveMethod))
            {
                var currentMethod = (PlayerPawnsArriveMethod)arriveMethodField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ArrivalMethod".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentMethod.ToStringHuman(),
                    Data = GetArriveMethodOptions(),
                    // MUTATION-C: mirrors ScenPart_PlayerPawnsArriveMethod.DoEditInterface's FloatMenu-over-enum picker (decompiled RimWorld/ScenPart_PlayerPawnsArriveMethod.cs:19-38).
                    SetValue = (val) => arriveMethodField.SetValue(part, val)
                });
            }

            var thingDefField = partType.GetField("thingDef", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (thingDefField != null && thingDefField.FieldType == typeof(ThingDef))
            {
                var currentThingDef = (ThingDef)thingDefField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Item".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentThingDef?.LabelCap ?? "None".Translate(),
                    Data = GetStartingThingOptions(part),
                    // MUTATION-C: mirrors ScenPart_ThingCount.DoEditInterface's thingDef
                    // FloatMenu delegate (decompiled RimWorld/ScenPart_ThingCount.cs:45-108,
                    // row 1), which resets stuff AND quality in the same delegate — this used
                    // to reset only stuff, silently dropping the quality reset (drift found
                    // and fixed).
                    SetValue = (val) =>
                    {
                        // MUTATION-C: see above.
                        thingDefField.SetValue(part, val);
                        var stuffField = partType.GetField("stuff", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        if (stuffField != null && val is ThingDef td)
                        {
                            // MUTATION-C: see above (stuff = GenStuff.DefaultStuffFor(td)).
                            stuffField.SetValue(part, GenStuff.DefaultStuffFor(td));
                        }
                        var qualityField = partType.GetField("quality", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        if (qualityField != null)
                        {
                            // MUTATION-C: see above (quality = null).
                            qualityField.SetValue(part, null);
                        }
                    }
                });

                if (currentThingDef != null && currentThingDef.MadeFromStuff)
                {
                    var stuffField = partType.GetField("stuff", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (stuffField != null)
                    {
                        var currentStuff = (ThingDef)stuffField.GetValue(part);
                        fields.Add(new PartField
                        {
                            Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Material".Translate(),
                            Type = FieldType.Dropdown,
                            CurrentValue = currentStuff?.LabelCap ?? "Default".Translate(),
                            Data = GetStuffOptions(currentThingDef),
                            // MUTATION-C: mirrors ScenPart_ThingCount.DoEditInterface's stuff FloatMenu, row 2 (decompiled RimWorld/ScenPart_ThingCount.cs:45-108).
                            SetValue = (val) => stuffField.SetValue(part, val)
                        });
                    }
                }

                if (currentThingDef != null && currentThingDef.HasComp(typeof(CompQuality)))
                {
                    var qualityField = partType.GetField("quality", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (qualityField != null)
                    {
                        var currentQuality = qualityField.GetValue(part);
                        string qualityLabel = (string)"Default".Translate();
                        if (currentQuality != null && currentQuality is QualityCategory q)
                        {
                            qualityLabel = q.GetLabel().CapitalizeFirst();
                        }
                        fields.Add(new PartField
                        {
                            Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Quality".Translate(),
                            Type = FieldType.Dropdown,
                            CurrentValue = qualityLabel,
                            Data = GetQualityOptions(),
                            // MUTATION-C: mirrors ScenPart_ThingCount.DoEditInterface's quality FloatMenu, row 3 (decompiled RimWorld/ScenPart_ThingCount.cs:45-108).
                            SetValue = (val) => qualityField.SetValue(part, val)
                        });
                    }
                }
            }

            var countField = partType.GetField("count", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (countField != null && countField.FieldType == typeof(int))
            {
                int currentValue = (int)countField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentValue.ToString(),
                    // MUTATION-C: ScenPart_ThingCount.DoEditInterface (decompiled
                    // RimWorld/ScenPart_ThingCount.cs) edits count via Widgets.TextFieldNumeric
                    // with min=1, max=1E9 — not int.MaxValue, which let values past vanilla's
                    // own cap through. ScenPart_StartingAnimal, the other vanilla part this
                    // generic branch serves, uses the same bounds by its own call site:
                    // Listing_Standard.TextFieldNumeric(ref count, ref countBuf, 1f)
                    // (decompiled RimWorld/ScenPart_StartingAnimal.cs:52), whose max defaults
                    // to 1E9f (decompiled Verse/Listing_Standard.cs:341).
                    Data = new int[] { 1, 1_000_000_000 },
                    // MUTATION-C: mirrors ScenPart_ThingCount.DoEditInterface's count TextFieldNumeric(min=1) row 4 (decompiled RimWorld/ScenPart_ThingCount.cs:45-108); widget's own max default 1E9.
                    SetValue = (val) => countField.SetValue(part, val)
                });
            }

            var chanceField = partType.GetField("chance", BindingFlags.NonPublic | BindingFlags.Instance);
            if (chanceField != null && chanceField.FieldType == typeof(float))
            {
                float currentValue = (float)chanceField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Chance".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = $"{currentValue * 100:F0}%",
                    Data = new float[] { 0f, 1f },
                    // MUTATION-C: mirrors ScenPart_PawnModifier.DoPawnModifierEditInterface's chance TextFieldPercent (decompiled RimWorld/ScenPart_PawnModifier.cs:26-55), default clamp 0..1.
                    SetValue = (val) => chanceField.SetValue(part, val)
                });
            }

            var hediffField = partType.GetField("hediff", BindingFlags.NonPublic | BindingFlags.Instance);
            if (hediffField != null && hediffField.FieldType == typeof(HediffDef))
            {
                var currentHediff = (HediffDef)hediffField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Condition".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentHediff?.LabelCap ?? "None".Translate(),
                    Data = GetHediffOptions(),
                    SetValue = (val) =>
                    {
                        // MUTATION-C: mirrors ScenPart_ForcedHediff.DoEditInterface's hediff FloatMenu, hediffs where scenarioCanAdd (decompiled RimWorld/ScenPart_ForcedHediff.cs:26-46).
                        hediffField.SetValue(part, val);

                        var severityField = partType.GetField("severityRange", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (severityField != null && severityField.FieldType == typeof(FloatRange))
                        {
                            float maxSeverity = GetForcedHediffMaxSeverity(part, partType);
                            var range = (FloatRange)severityField.GetValue(part);
                            if (range.max > maxSeverity) range.max = maxSeverity;
                            if (range.min > maxSeverity) range.min = maxSeverity;
                            // MUTATION-C: mirrors ScenPart_ForcedHediff.DoEditInterface's
                            // hediff-select FloatMenuUtility delegate, which re-clamps
                            // severityRange.max/min inline (not a callable vanilla method).
                            severityField.SetValue(part, range);
                        }
                    }
                });
            }

            var severityRangeField = partType.GetField("severityRange", BindingFlags.NonPublic | BindingFlags.Instance);
            if (severityRangeField != null && severityRangeField.FieldType == typeof(FloatRange))
            {
                var currentRange = (FloatRange)severityRangeField.GetValue(part);
                // Vanilla clamps severityRange to [0, MaxSeverity] (the private
                // ScenPart_ForcedHediff.MaxSeverity: lethalSeverity>0 ? lethalSeverity*0.99f : 1f),
                // harvested by reflection below so past-lethal severities stay unreachable.
                float maxSeverity = GetForcedHediffMaxSeverity(part, partType);

                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumSeverity".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = $"{(currentRange.min * 100):F0}%",
                    Data = new float[] { 0f, maxSeverity },
                    LiveSliderValue = part is ScenPart_ForcedHediff ? () => ((FloatRange)severityRangeField.GetValue(part)).min : (Func<float>)null,
                    SetValue = (val) =>
                    {
                        var range = (FloatRange)severityRangeField.GetValue(part);
                        float curMaxSeverity = GetForcedHediffMaxSeverity(part, partType);
                        float newMin = Mathf.Min(Convert.ToSingle(val), curMaxSeverity);
                        if (newMin > range.max) newMin = range.max;
                        // MUTATION-C: mirrors ScenPart_ForcedHediff's severityRange FloatRange widget, min handle, clamped 0..MaxSeverity (decompiled RimWorld/ScenPart_ForcedHediff.cs:26-46).
                        severityRangeField.SetValue(part, new FloatRange(newMin, range.max));
                    }
                });

                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumSeverity".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = $"{(currentRange.max * 100):F0}%",
                    Data = new float[] { 0f, maxSeverity },
                    LiveSliderValue = part is ScenPart_ForcedHediff ? () => ((FloatRange)severityRangeField.GetValue(part)).max : (Func<float>)null,
                    SliderIsRangeMax = true,
                    SetValue = (val) =>
                    {
                        var range = (FloatRange)severityRangeField.GetValue(part);
                        float curMaxSeverity = GetForcedHediffMaxSeverity(part, partType);
                        float newMax = Mathf.Min(Convert.ToSingle(val), curMaxSeverity);
                        if (newMax < range.min) newMax = range.min;
                        // MUTATION-C: same severityRange FloatRange widget, max handle (decompiled RimWorld/ScenPart_ForcedHediff.cs:26-46).
                        severityRangeField.SetValue(part, new FloatRange(range.min, newMax));
                    }
                });
            }

            var contextField = partType.GetField("context", BindingFlags.NonPublic | BindingFlags.Instance);
            if (contextField != null && contextField.FieldType == typeof(PawnGenerationContext))
            {
                var currentContext = (PawnGenerationContext)contextField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Affects".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentContext.ToStringHuman(),
                    Data = GetPawnGenerationContextOptions(),
                    // MUTATION-C: mirrors ScenPart_PawnModifier.DoPawnModifierEditInterface's context ButtonText, FloatMenu over PawnGenerationContext (decompiled RimWorld/ScenPart_PawnModifier.cs:26-55).
                    SetValue = (val) => contextField.SetValue(part, val)
                });
            }

            var animalKindField = partType.GetField("animalKind", BindingFlags.NonPublic | BindingFlags.Instance);
            if (animalKindField != null && animalKindField.FieldType == typeof(PawnKindDef))
            {
                var currentKind = (PawnKindDef)animalKindField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.AnimalType".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = CurrentAnimalLabel(part, currentKind),
                    Data = GetAnimalOptions(part),
                    // MUTATION-C: mirrors ScenPart_StartingAnimal's animal-kind ButtonText, FloatMenu RandomPet + PossibleAnimals (decompiled RimWorld/ScenPart_StartingAnimal.cs:46-72).
                    SetValue = (val) => animalKindField.SetValue(part, val)
                });
            }

            if (part is ScenPart_ForcedTrait)
            {
                var traitField = partType.GetField("trait", BindingFlags.NonPublic | BindingFlags.Instance);
                var degreeField = partType.GetField("degree", BindingFlags.NonPublic | BindingFlags.Instance);

                if (traitField != null && degreeField != null)
                {
                    var currentTrait = traitField.GetValue(part) as TraitDef;
                    int currentDegree = (int)degreeField.GetValue(part);

                    string currentLabel = (string)"None".Translate();
                    if (currentTrait != null)
                    {
                        var degreeData = currentTrait.DataAtDegree(currentDegree);
                        currentLabel = degreeData?.LabelCap ?? currentTrait.LabelCap;
                    }

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Trait".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLabel,
                        Data = GetTraitWithDegreeOptions(),
                        SetValue = (val) => {
                            if (val is TraitDegreeData tdd)
                            {
                                foreach (var td in DefDatabase<TraitDef>.AllDefs)
                                {
                                    if (td.degreeDatas.Contains(tdd))
                                    {
                                        // MUTATION-C: mirrors ScenPart_ForcedTrait.DoEditInterface's trait ButtonText, FloatMenu over every TraitDef x degree (decompiled RimWorld/ScenPart_ForcedTrait.cs:21-43).
                                        traitField.SetValue(part, td);
                                        // MUTATION-C: same ScenPart_ForcedTrait picker delegate — sets the degree paired with the trait above.
                                        degreeField.SetValue(part, tdd.degree);
                                        break;
                                    }
                                }
                            }
                        }
                    });
                }
            }

            // ScenPart_CreateIncident is `internal` in Assembly-CSharp, so this is a Type
            // reference-equality check rather than the `is` pattern used everywhere else.
            if (partType == ScenPartCreateIncidentType)
            {
                var incidentField = partType.GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance);
                if (incidentField != null)
                {
                    var currentIncident = incidentField.GetValue(part) as IncidentDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Incident".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentIncident?.LabelCap ?? "None".Translate(),
                        Data = GetIncidentDefOptions(),
                        // MUTATION-C: mirrors ScenPart_IncidentBase.DoIncidentEditInterface's incident FloatMenu, all IncidentDefs (decompiled RimWorld/ScenPart_IncidentBase.cs:80-90).
                        SetValue = (val) => incidentField.SetValue(part, val)
                    });
                }

                var minDaysField = partType.GetField("minDays", BindingFlags.NonPublic | BindingFlags.Instance);
                if (minDaysField != null)
                {
                    float currentMin = (float)minDaysField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentMin.ToString("F0"),
                        // MUTATION-C: ScenPart_CreateIncident.DoEditInterface calls
                        // Widgets.TextFieldNumericLabeled with no explicit min/max, so it
                        // takes the widget's own defaults (Verse/Widgets.cs TextFieldNumeric
                        // L1862: 0f..1E9f), not the 1000-day cap this used to hand-pick.
                        Data = new float[] { 0f, 1_000_000_000f },
                        IsIntegerDisplay = true,
                        // MUTATION-C: TextFieldNumericLabeled(minDays) with no explicit bounds (decompiled RimWorld/ScenPart_CreateIncident.cs:59-70); widget's own 0f..1E9f default.
                        SetValue = (val) => minDaysField.SetValue(part, Convert.ToSingle(val))
                    });
                }

                var maxDaysField = partType.GetField("maxDays", BindingFlags.NonPublic | BindingFlags.Instance);
                if (maxDaysField != null)
                {
                    float currentMax = (float)maxDaysField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentMax.ToString("F0"),
                        // MUTATION-C: same TextFieldNumericLabeled default as minDays above.
                        Data = new float[] { 0f, 1_000_000_000f },
                        IsIntegerDisplay = true,
                        SetValue = (val) => maxDaysField.SetValue(part, Convert.ToSingle(val))
                    });
                }

                var repeatField = partType.GetField("repeat", BindingFlags.NonPublic | BindingFlags.Instance);
                if (repeatField != null)
                {
                    bool currentRepeat = (bool)repeatField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Repeat".Translate(),
                        Type = FieldType.Checkbox,
                        CurrentValue = BoolDisplay(currentRepeat),
                        BoolValue = currentRepeat,
                        Data = null,
                        // MUTATION-C: mirrors ScenPart_CreateIncident.DoEditInterface's CheckboxLabeled(repeat) (decompiled RimWorld/ScenPart_CreateIncident.cs:59-70).
                        SetValue = (val) => repeatField.SetValue(part, val is bool b && b)
                    });
                }
            }
            // Generic incident handler; ScenPart_DisableIncident has its own below.
            else if (!(part is ScenPart_DisableIncident))
            {
                var incidentField = partType.GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance);
                if (incidentField != null && incidentField.FieldType == typeof(IncidentDef))
                {
                    var currentIncident = (IncidentDef)incidentField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Incident".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentIncident?.LabelCap ?? "None".Translate(),
                        // ScenPart_IncidentBase.DoIncidentEditInterface (inherited by both
                        // CreateIncident and DisableIncident) offers ALL IncidentDefs with no filter.
                        Data = GetIncidentDefOptions(),
                        // MUTATION-C: mirrors ScenPart_IncidentBase.DoIncidentEditInterface's bare incident FloatMenu, no filter (decompiled RimWorld/ScenPart_IncidentBase.cs:80-90).
                        SetValue = (val) => incidentField.SetValue(part, val)
                    });
                }
            }

            var researchField = partType.GetField("project", BindingFlags.NonPublic | BindingFlags.Instance);
            if (researchField != null && researchField.FieldType == typeof(ResearchProjectDef))
            {
                var currentProject = (ResearchProjectDef)researchField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Research".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentProject?.LabelCap ?? "None".Translate(),
                    Data = GetResearchOptions(part),
                    // MUTATION-C: mirrors ScenPart_StartingResearch.DoEditInterface's FloatMenu over NonRedundantResearchProjects (decompiled RimWorld/ScenPart_StartingResearch.cs:11-21).
                    SetValue = (val) => researchField.SetValue(part, val)
                });
            }

            // pawnCount and pawnChoiceCount are linked: pawnChoiceCount must be >= pawnCount.
            var pawnCountField = partType.GetField("pawnCount", BindingFlags.Public | BindingFlags.Instance);
            var pawnChoiceCountField = partType.GetField("pawnChoiceCount", BindingFlags.Public | BindingFlags.Instance);

            if (pawnCountField != null && pawnCountField.FieldType == typeof(int))
            {
                int currentValue = (int)pawnCountField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.StartingPawns".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentValue.ToString(),
                    Data = new int[] { 1, 10 }, // Game max is 10
                    SetValue = (val) => {
                        int newPawnCount = Convert.ToInt32(val);
                        // MUTATION-C: mirrors ScenPart_ConfigPage_ConfigureStartingPawns.DoEditInterface's pawnCount TextFieldNumeric(1,10) (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns.cs:33).
                        pawnCountField.SetValue(part, newPawnCount);
                        // Game enforces pawnChoiceCount >= pawnCount, so bump it up if needed
                        if (pawnChoiceCountField != null)
                        {
                            int choiceCount = (int)pawnChoiceCountField.GetValue(part);
                            if (choiceCount < newPawnCount)
                            {
                                // MUTATION-C: reproduces the SAME method's pawnChoiceCount TextFieldNumeric(min=pawnCount,max=10) (decompiled :35), which live-reclamps every draw pass; no callable vanilla method.
                                pawnChoiceCountField.SetValue(part, newPawnCount);
                            }
                        }
                    }
                });
            }

            // pawnChoiceCount lives on the SHARED base, so plain GetField also finds it on the three
            // list-based variants (KindDefs/Xenotypes/Mutants) whose own DoEditInterface overrides
            // never draw a widget for it. Gate on pawnCountField, present only on the plain
            // ConfigureStartingPawns variant, so the list variants get no control vanilla lacks.
            if (pawnCountField != null && pawnChoiceCountField != null && pawnChoiceCountField.FieldType == typeof(int))
            {
                int currentValue = (int)pawnChoiceCountField.GetValue(part);
                int pawnCount = 1;
                if (pawnCountField != null)
                    pawnCount = (int)pawnCountField.GetValue(part);

                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PawnChoicePool".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentValue.ToString(),
                    Data = new int[] { pawnCount, 10 }, // Minimum is current pawnCount
                    SetValue = (val) => {
                        int newChoiceCount = Convert.ToInt32(val);
                        int currentPawnCount = pawnCountField != null ? (int)pawnCountField.GetValue(part) : 1;
                        if (newChoiceCount < currentPawnCount)
                            newChoiceCount = currentPawnCount;
                        // MUTATION-C: same pawnChoiceCount TextFieldNumeric(min=pawnCount,max=10) live re-clamp (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns.cs:35).
                        pawnChoiceCountField.SetValue(part, newChoiceCount);
                    }
                });
            }

            // allowedDevelopmentalStages and requiredSkills are deliberately NOT exposed:
            // ScenPart_ConfigPage_ConfigureStartingPawns.DoEditInterface draws only pawnCount and
            // pawnChoiceCount. Exact parity cuts both ways — no omissions, but also no invented
            // controls for something a sighted player cannot touch on this screen.

            var ageRangeField = partType.GetField("allowedAgeRange", BindingFlags.Public | BindingFlags.Instance);
            if (ageRangeField != null && ageRangeField.FieldType == typeof(IntRange))
            {
                var currentRange = (IntRange)ageRangeField.GetValue(part);
                // MUTATION-C: ScenPart_PawnFilter_Age.DoEditInterface edits allowedAgeRange
                // with ONE Widgets.IntRange(min=15, max=120, minWidth=4) — a single two-handle
                // widget vanilla doesn't expose per-field, so it's split into two Quantity
                // fields here. minWidth=4 is enforced live INSIDE Widgets.IntRange while
                // dragging (Verse/Widgets.cs L2387-2409); each field's own bound below is
                // recomputed from the OTHER field's live value every time this list is
                // rebuilt, so a field never independently offers a value the game's own
                // minWidth would reject on the other handle.
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumAge".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentRange.min.ToString(),
                    Data = new int[] { 15, currentRange.max - 4 },
                    LiveSliderValue = part is ScenPart_PawnFilter_Age ? () => ((IntRange)ageRangeField.GetValue(part)).min : (Func<float>)null,
                    SetValue = (val) =>
                    {
                        var range = (IntRange)ageRangeField.GetValue(part);
                        range.min = (int)val;
                        // Ensure min doesn't exceed max - 4 (game requires 4 year gap)
                        if (range.min > range.max - 4)
                            range.min = range.max - 4;
                        // MUTATION-C: mirrors ScenPart_PawnFilter_Age's IntRange widget min handle (decompiled RimWorld/ScenPart_PawnFilter_Age.cs:18-21), minWidth 4.
                        ageRangeField.SetValue(part, range);
                    },
                    GetValue = () => ((IntRange)ageRangeField.GetValue(part)).min
                });
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumAge".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentRange.max.ToString(),
                    Data = new int[] { currentRange.min + 4, 120 },
                    LiveSliderValue = part is ScenPart_PawnFilter_Age ? () => ((IntRange)ageRangeField.GetValue(part)).max : (Func<float>)null,
                    SliderIsRangeMax = true,
                    SetValue = (val) =>
                    {
                        var range = (IntRange)ageRangeField.GetValue(part);
                        range.max = (int)val;
                        // Ensure max is at least min + 4 (game requires 4 year gap)
                        if (range.max < range.min + 4)
                            range.max = range.min + 4;
                        // MUTATION-C: same IntRange widget, max handle (decompiled RimWorld/ScenPart_PawnFilter_Age.cs:18-21).
                        ageRangeField.SetValue(part, range);
                    },
                    GetValue = () => ((IntRange)ageRangeField.GetValue(part)).max
                });
            }

            if (ModsConfig.BiotechActive && part is ScenPart_StartingMech)
            {
                var mechKindField = partType.GetField("mechKind", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mechKindField != null)
                {
                    var currentKind = mechKindField.GetValue(part);
                    string currentLabel = (string)"RandomMech".Translate();
                    if (currentKind != null)
                    {
                        var labelProp = currentKind.GetType().GetProperty("LabelCap");
                        currentLabel = labelProp?.GetValue(currentKind)?.ToString() ?? (string)"RandomMech".Translate();
                    }
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MechType".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLabel,
                        Data = GetStartingMechOptions(),
                        // MUTATION-C: mirrors ScenPart_StartingMech's mechKind ButtonText, FloatMenu RandomMech + PossibleMechs (decompiled RimWorld/ScenPart_StartingMech.cs:23-49).
                        SetValue = (val) => mechKindField.SetValue(part, val)
                    });
                }

                var mechChanceField = partType.GetField("overseenByPlayerPawnChance", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mechChanceField != null)
                {
                    float currentChance = (float)mechChanceField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MechanitorOversightChance".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentChance * 100:F0}%",
                        Data = new float[] { 0f, 1f },
                        LiveSliderValue = () => (float)mechChanceField.GetValue(part),
                        // MUTATION-C: mirrors ScenPart_StartingMech's overseenByPlayerPawnChance HorizontalSlider(0,1,step 0.01) (decompiled RimWorld/ScenPart_StartingMech.cs:23-49).
                        SetValue = (val) => mechChanceField.SetValue(part, Convert.ToSingle(val))
                    });
                }
            }

            if (part is ScenPart_GameStartDialog)
            {
                var textField = partType.GetField("text", BindingFlags.NonPublic | BindingFlags.Instance);
                if (textField != null && textField.FieldType == typeof(string))
                {
                    var fullText = (string)textField.GetValue(part) ?? "";

                    string displayText = fullText;
                    if (fullText.Contains("\n"))
                    {
                        int newlineIndex = fullText.IndexOf('\n');
                        displayText = fullText.Substring(0, Math.Min(newlineIndex, 60)) + "...";
                    }
                    else if (fullText.Length > 60)
                    {
                        displayText = fullText.Substring(0, 60) + "...";
                    }

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.DialogText".Translate(),
                        Type = FieldType.Text,
                        CurrentValue = string.IsNullOrEmpty(displayText) ? (string)"RimWorldAccess.ScenarioBuilder.Value.Empty".Translate() : displayText,
                        Data = fullText, // Store full text for editing
                        MultiLine = true, // Widgets.TextArea at RowHeight*5 (ScenPart_GameStartDialog.cs)
                        // MUTATION-C: mirrors ScenPart_GameStartDialog.DoEditInterface's TextArea(text) (decompiled RimWorld/ScenPart_GameStartDialog.cs:14-18).
                        SetValue = (val) => textField.SetValue(part, val)
                    });
                }
            }

            if (part is ScenPart_DisableIncident)
            {
                var incidentField = partType.GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance);
                if (incidentField != null)
                {
                    var currentIncident = incidentField.GetValue(part) as IncidentDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Incident".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentIncident?.LabelCap ?? "None".Translate(),
                        Data = GetIncidentDefOptions(), // Reuse existing helper
                        // MUTATION-C: mirrors ScenPart_IncidentBase.DoIncidentEditInterface's bare incident FloatMenu, used by ScenPart_DisableIncident (decompiled RimWorld/ScenPart_IncidentBase.cs:80-90).
                        SetValue = (val) => incidentField.SetValue(part, val)
                    });
                }
            }

            if (part is ScenPart_StatFactor)
            {
                var statField = partType.GetField("stat", BindingFlags.NonPublic | BindingFlags.Instance);
                if (statField != null)
                {
                    var currentStat = statField.GetValue(part) as StatDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Stat".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentStat?.LabelCap ?? "None".Translate(),
                        Data = GetStatDefOptions(),
                        // MUTATION-C: mirrors ScenPart_StatFactor's stat ButtonText, FloatMenu stats where !forInformationOnly && CanShowWithLoadedMods (decompiled RimWorld/ScenPart_StatFactor.cs:23-49).
                        SetValue = (val) => statField.SetValue(part, val)
                    });
                }

                // factor is stored as a decimal and displayed as a percentage; the game allows
                // 0-100 raw (0% to 10000%), where 1.0 = 100%.
                var factorField = partType.GetField("factor", BindingFlags.NonPublic | BindingFlags.Instance);
                if (factorField != null)
                {
                    float currentFactor = (float)factorField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Factor".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentFactor * 100:F0}%",
                        Data = new float[] { 0f, 100f }, // 0-10000% range in game
                        // MUTATION-C: mirrors ScenPart_StatFactor's factor TextFieldPercent(0,100) raw units (decompiled RimWorld/ScenPart_StatFactor.cs:48); verified against the widget's own clamp — 100 = 10000%.
                        SetValue = (val) => factorField.SetValue(part, Convert.ToSingle(val)),
                        IsPercentDisplay = true // Display as percentage even though max > 1
                    });
                }
            }

            if (part is ScenPart_PermaGameCondition)
            {
                var conditionField = partType.GetField("gameCondition", BindingFlags.NonPublic | BindingFlags.Instance);
                if (conditionField != null)
                {
                    var currentCondition = conditionField.GetValue(part) as GameConditionDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Condition".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentCondition?.LabelCap ?? "None".Translate(),
                        Data = GetPermanentGameConditionOptions(),
                        // MUTATION-C: mirrors ScenPart_PermaGameCondition's condition ButtonText, FloatMenu conditions where canBePermanent (decompiled RimWorld/ScenPart_PermaGameCondition.cs:15-25).
                        SetValue = (val) => conditionField.SetValue(part, val)
                    });
                }
            }

            if (part is ScenPart_DisallowBuilding)
            {
                var buildingField = partType.GetField("building", BindingFlags.NonPublic | BindingFlags.Instance);
                if (buildingField != null)
                {
                    var currentBuilding = buildingField.GetValue(part) as ThingDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Building".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentBuilding?.LabelCap ?? "None".Translate(),
                        Data = GetBuildableThingDefOptions(),
                        // MUTATION-C: mirrors ScenPart_DisallowBuilding's building ButtonText, FloatMenu BuildableByPlayer ordered by label (decompiled RimWorld/ScenPart_DisallowBuilding.cs:42-60).
                        SetValue = (val) => buildingField.SetValue(part, val)
                    });
                }
            }

            if (part is ScenPart_SetNeedLevel)
            {
                var needField = partType.GetField("need", BindingFlags.NonPublic | BindingFlags.Instance);
                if (needField != null)
                {
                    var currentNeed = needField.GetValue(part) as NeedDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Need".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentNeed?.LabelCap ?? "None".Translate(),
                        Data = GetNeedDefOptions(),
                        // MUTATION-C: mirrors ScenPart_SetNeedLevel's need ButtonText, FloatMenu needs where major (decompiled RimWorld/ScenPart_SetNeedLevel.cs:14-26).
                        SetValue = (val) => needField.SetValue(part, val)
                    });
                }

                var levelRangeField = partType.GetField("levelRange", BindingFlags.NonPublic | BindingFlags.Instance);
                if (levelRangeField != null)
                {
                    var currentRange = (FloatRange)levelRangeField.GetValue(part);

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumLevel".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentRange.min * 100:F0}%",
                        Data = new float[] { 0f, 1f },
                        LiveSliderValue = () => ((FloatRange)levelRangeField.GetValue(part)).min,
                        SetValue = (val) => {
                            var range = (FloatRange)levelRangeField.GetValue(part);
                            float newMin = Convert.ToSingle(val);
                            if (newMin > range.max) newMin = range.max;
                            // MUTATION-C: mirrors ScenPart_SetNeedLevel's levelRange FloatRange widget, min handle, clamped 0..1 (decompiled RimWorld/ScenPart_SetNeedLevel.cs:14-26).
                            levelRangeField.SetValue(part, new FloatRange(newMin, range.max));
                        }
                    });

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumLevel".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentRange.max * 100:F0}%",
                        Data = new float[] { 0f, 1f },
                        LiveSliderValue = () => ((FloatRange)levelRangeField.GetValue(part)).max,
                        SliderIsRangeMax = true,
                        SetValue = (val) => {
                            var range = (FloatRange)levelRangeField.GetValue(part);
                            float newMax = Convert.ToSingle(val);
                            if (newMax < range.min) newMax = range.min;
                            // MUTATION-C: same levelRange FloatRange widget, max handle (decompiled RimWorld/ScenPart_SetNeedLevel.cs:14-26).
                            levelRangeField.SetValue(part, new FloatRange(range.min, newMax));
                        }
                    });
                }
            }

            if (part is ScenPart_GameCondition)
            {
                // The condition type comes from def.gameCondition; only durationDays is editable.
                var durationField = partType.GetField("durationDays", BindingFlags.NonPublic | BindingFlags.Instance);
                if (durationField != null)
                {
                    float currentDuration = (float)durationField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.DurationDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentDuration.ToString("F0"),
                        // MUTATION-C: DoEditInterface calls TextFieldNumericLabeled with no
                        // explicit bounds — the widget's own 0f..1E9f default, not a 1000-day
                        // hand-picked cap.
                        Data = new float[] { 0f, 1_000_000_000f },
                        IsIntegerDisplay = true,
                        // MUTATION-C: TextFieldNumericLabeled(durationDays) with no explicit bounds (decompiled RimWorld/ScenPart_GameCondition.cs:30-33); widget's own 0f..1E9f default.
                        SetValue = (val) => durationField.SetValue(part, Convert.ToSingle(val))
                    });
                }
            }

            if (part is ScenPart_OnPawnDeathExplode)
            {
                var radiusField = partType.GetField("radius", BindingFlags.NonPublic | BindingFlags.Instance);
                if (radiusField != null)
                {
                    float currentRadius = (float)radiusField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ExplosionRadius".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentRadius.ToString("F1"),
                        // MUTATION-C: TextFieldNumericLabeled with no explicit bounds, so
                        // 0f..1E9f — not the {0.1, 50} range this used to hand-pick.
                        Data = new float[] { 0f, 1_000_000_000f },
                        SetValue = (val) => radiusField.SetValue(part, Convert.ToSingle(val))
                    });
                }

                var damageField = partType.GetField("damage", BindingFlags.NonPublic | BindingFlags.Instance);
                if (damageField != null)
                {
                    var currentDamage = damageField.GetValue(part) as DamageDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.DamageType".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentDamage?.LabelCap ?? "None".Translate(),
                        Data = GetExplosionDamageDefOptions(part),
                        // MUTATION-C: mirrors ScenPart_OnPawnDeathExplode's damage ButtonText, FloatMenu Bomb/Flame (decompiled RimWorld/ScenPart_OnPawnDeathExplode.cs:33-45).
                        SetValue = (val) => damageField.SetValue(part, val)
                    });
                }
            }

            if (part is ScenPart_DisableQuest)
            {
                var questDefField = partType.GetField("questDef", BindingFlags.Public | BindingFlags.Instance);
                if (questDefField != null)
                {
                    var currentQuest = questDefField.GetValue(part) as QuestScriptDef;
                    // LabelCap returns null when label is empty, use readable defName
                    string questLabel;
                    if (currentQuest == null)
                    {
                        questLabel = (string)"None".Translate();
                    }
                    else if (currentQuest.label.NullOrEmpty())
                    {
                        questLabel = GenText.SplitCamelCase(currentQuest.defName).Replace("_", " ");
                    }
                    else
                    {
                        questLabel = (string)currentQuest.LabelCap;
                    }
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Quest".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = questLabel,
                        Data = GetQuestScriptDefOptions(),
                        // MUTATION-C: mirrors ScenPart_DisableQuest's questDef ButtonText, FloatMenu all QuestScriptDef (decompiled RimWorld/ScenPart_DisableQuest.cs:17-36).
                        SetValue = (val) => questDefField.SetValue(part, val)
                    });
                }
            }

            if (part is ScenPart_CreateQuest)
            {
                var questDefField = partType.GetField("questDef", BindingFlags.NonPublic | BindingFlags.Instance);
                if (questDefField != null)
                {
                    var currentQuest = questDefField.GetValue(part) as QuestScriptDef;
                    // LabelCap returns null when label is empty, use readable defName
                    string questLabel;
                    if (currentQuest == null)
                    {
                        questLabel = (string)"None".Translate();
                    }
                    else if (currentQuest.label.NullOrEmpty())
                    {
                        questLabel = GenText.SplitCamelCase(currentQuest.defName).Replace("_", " ");
                    }
                    else
                    {
                        questLabel = (string)currentQuest.LabelCap;
                    }
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Quest".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = questLabel,
                        Data = GetQuestScriptDefOptions(),
                        // MUTATION-C: mirrors ScenPart_CreateQuest's questDef ButtonText, FloatMenu all QuestScriptDef (decompiled RimWorld/ScenPart_CreateQuest.cs:17-36).
                        SetValue = (val) => questDefField.SetValue(part, val)
                    });
                }
            }

            if (part is ScenPart_ForcedMap)
            {
                var mapGenField = partType.GetField("mapGenerator", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mapGenField != null)
                {
                    var currentMapGen = mapGenField.GetValue(part) as MapGeneratorDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MapGenerator".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentMapGen?.LabelCap ?? "None".Translate(),
                        Data = GetMapGeneratorDefOptions(),
                        // MUTATION-C: mirrors ScenPart_ForcedMap.DoEditInterface's mapGenerator ButtonText, FloatMenu MapGeneratorDefs where validScenarioMap (decompiled RimWorld/ScenPart_ForcedMap.cs:22-42).
                        SetValue = (val) => mapGenField.SetValue(part, val)
                    });
                }

                var layerDefField = partType.GetField("layerDef", BindingFlags.NonPublic | BindingFlags.Instance);
                if (layerDefField != null)
                {
                    var currentLayer = layerDefField.GetValue(part) as PlanetLayerDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PlanetLayer".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLayer?.LabelCap ?? "None".Translate(),
                        Data = GetPlanetLayerOptions(),
                        // MUTATION-C: same DoEditInterface's layerDef ButtonText, FloatMenu all PlanetLayerDef (decompiled RimWorld/ScenPart_ForcedMap.cs:43-56).
                        SetValue = (val) => layerDefField.SetValue(part, val)
                    });
                }
            }

            if (ModsConfig.AnomalyActive && part is ScenPart_MonolithGeneration)
            {
                var methodField = partType.GetField("method", BindingFlags.NonPublic | BindingFlags.Instance);
                if (methodField != null)
                {
                    var currentMethod = methodField.GetValue(part);
                    string currentLabel = currentMethod?.ToString() ?? "None".Translate();

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.GenerationMethod".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLabel,
                        Data = GetMonolithGenerationMethodOptions(),
                        // MUTATION-C: mirrors ScenPart_MonolithGeneration.DoEditInterface's method ButtonText, FloatMenu over MonolithGenerationMethod (decompiled RimWorld/ScenPart_MonolithGeneration.cs:34-50).
                        SetValue = (val) => methodField.SetValue(part, val)
                    });
                }
            }

            if (ModsConfig.AnomalyActive && part is ScenPart_AutoActivateMonolith)
            {
                var delayTicksField = partType.GetField("delayTicks", BindingFlags.NonPublic | BindingFlags.Instance);
                if (delayTicksField != null)
                {
                    int currentTicks = (int)delayTicksField.GetValue(part);
                    float currentDays = currentTicks / 60000f; // Convert ticks to days

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ActivationDelayDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentDays.ToString("F0"),
                        // MUTATION-C: DoEditInterface edits the DAYS value itself (val =
                        // ticks/60000) via TextFieldNumericLabeled with no explicit bounds,
                        // so the widget's own 0f..1E9f default applies directly to days here.
                        Data = new float[] { 0f, 1_000_000_000f },
                        IsIntegerDisplay = true,
                        SetValue = (val) => {
                            float days = Convert.ToSingle(val);
                            int ticks = (int)(days * 60000f);
                            // MUTATION-C: TextFieldNumericLabeled edits the DAYS value directly with no explicit bounds (decompiled RimWorld/ScenPart_AutoActivateMonolith.cs:17-22); ticks=days*60000 conversion is this adapter's own.
                            delayTicksField.SetValue(part, ticks);
                        }
                    });
                }
            }

            // Best-effort generic tier for THIRD-PARTY ScenPart types. Everything above is a curated,
            // exact-parity adapter for a specific vanilla or official-DLC class, so any type in the
            // `RimWorld` namespace stops here: reflecting more of it risks exposing bookkeeping
            // vanilla's own DoEditInterface never draws (e.g. ScenPart_PlanetLayer.hide). A modded
            // ScenPart has no adapter above by construction, so it falls through rather than going
            // silently read-only.
            if (partType.Namespace != "RimWorld")
            {
                AddGenericModdedFields(fields, part, partType);
            }

            return fields;
        }

        /// <summary>
        /// Reflects a modded ScenPart's own PUBLIC instance fields (DeclaredOnly; inherited
        /// vanilla-base fields are already covered by the FlattenHierarchy lookups above) into the
        /// best presentation available WITHOUT inventing a bound or validator no vanilla widget ever
        /// specified. Skips whichever field ScenPartListItemManager's generic list tier already claims
        /// for this part type, which gives it an Add/Delete-capable list presentation instead.
        /// Per-field rules (in <see cref="BuildGenericField"/>): bool and non-[Flags] enum and
        /// Def-typed references are sound (closed sets, the same operation as this file's pickers);
        /// anything else has no vanilla-sourced bound to cite, so it is presented READ-ONLY rather
        /// than given an invented one — never silently omitted.
        /// </summary>
        private static void AddGenericModdedFields(List<PartField> fields, ScenPart part, Type partType)
        {
            FieldInfo listField = ScenPartListItemManager.GetGenericListField(partType);
            foreach (var fi in partType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (fi == listField) continue;

                var capturedField = fi;
                // MUTATION-C: third-party ScenPart, no vanilla DoEditInterface exists to cite — generic bool/enum/Def adapter for a MODDED field (see AddGenericModdedFields doc above).
                var built = BuildGenericField(part, capturedField, val => capturedField.SetValue(part, val));
                if (built != null) fields.Add(built);
            }
        }

        /// <summary>
        /// Builds one PartField for an arbitrary public instance field under the same
        /// bool/enum/Def/read-only rules as <see cref="AddGenericModdedFields"/>. Factored out so
        /// ScenPartListItemManager's generic list tier can apply them to a LIST ITEM's fields:
        /// <paramref name="writeValue"/> decides how the write lands (directly on
        /// <paramref name="instance"/>, or re-fetched from a live list by index, as every vanilla
        /// list-item closure does rather than closing over a snapshot). Null for a field whose value
        /// cannot be read.
        /// </summary>
        internal static PartField BuildGenericField(object instance, FieldInfo fi, Action<object> writeValue)
        {
            string niceName = GenText.SplitCamelCase(fi.Name).CapitalizeFirst();
            object value;
            try { value = fi.GetValue(instance); }
            catch { return null; }

            if (fi.FieldType == typeof(bool))
            {
                bool boolValue = value is bool bv && bv;
                return new PartField
                {
                    Name = niceName,
                    Type = FieldType.Checkbox,
                    CurrentValue = BoolDisplay(boolValue),
                    BoolValue = boolValue,
                    SetValue = (val) => writeValue(val is bool nb && nb)
                };
            }

            if (fi.FieldType.IsEnum && !fi.FieldType.IsDefined(typeof(FlagsAttribute), false))
            {
                var options = new List<(string label, object value)>();
                foreach (var enumValue in Enum.GetValues(fi.FieldType))
                    options.Add((HumanizeGenericEnumValue(fi.FieldType, enumValue), enumValue));
                return new PartField
                {
                    Name = niceName,
                    Type = FieldType.Dropdown,
                    CurrentValue = HumanizeGenericEnumValue(fi.FieldType, value),
                    Data = options,
                    SetValue = (val) => writeValue(val)
                };
            }

            if (typeof(Def).IsAssignableFrom(fi.FieldType))
            {
                return new PartField
                {
                    Name = niceName,
                    Type = FieldType.Dropdown,
                    CurrentValue = (value as Def)?.LabelCap ?? (string)"None".Translate(),
                    // Reflection cannot tell whether an arbitrary modded field is nullable, but one
                    // observed holding null tolerates null, so offer a way back to it.
                    Data = GetGenericDefOptions(fi.FieldType, includeNone: value == null),
                    SetValue = (val) => writeValue(val)
                };
            }

            string displayValue = value?.ToString() ?? (string)"None".Translate();
            return new PartField
            {
                Name = niceName,
                Type = FieldType.ReadOnly,
                CurrentValue = displayValue.Length > 60 ? displayValue.Substring(0, 60) + "..." : displayValue,
                Data = displayValue
            };
        }

        /// <summary>
        /// Best-effort label for an arbitrary (possibly modded) enum value: a translation under the
        /// "{EnumTypeName}_{ValueName}" convention several vanilla enums use, else the raw name
        /// humanized — never a hardcoded English word, since no translation can be asserted.
        /// </summary>
        private static string HumanizeGenericEnumValue(Type enumType, object value)
        {
            if (value == null) return (string)"None".Translate();
            string key = $"{enumType.Name}_{value}";
            if (key.CanTranslate())
                return key.Translate();
            return GenText.SplitCamelCase(value.ToString()).CapitalizeFirst();
        }

    }
}
