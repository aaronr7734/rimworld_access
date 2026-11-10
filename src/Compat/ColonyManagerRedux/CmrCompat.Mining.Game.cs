using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Mining job and tab, as far as <see cref="Shell.CmrMiningDetails"/>
        /// reads and writes them. Kept separate from <see cref="CmrCompat.Ready"/> and
        /// <see cref="Hunting"/> so a rename touching only Mining cannot disturb the manager
        /// window's tab and job lists or any other tab's detail rows.
        ///
        /// <c>ManagerJob_Mining</c> and <c>ManagerTab_Mining</c> are internal to the mod and never
        /// named beyond the strings passed to <see cref="ReflectionSurface"/>; callers pass them as
        /// <c>object</c>. The nested <c>Task</c> enum is internal by containment, so its values
        /// travel boxed.
        ///
        /// <c>ShouldCheckReachable</c> and <c>UsePathBasedDistance</c> resolve on the common
        /// <c>ManagerJob</c> base and are reached through <see cref="JobBase"/>; the shared
        /// <c>Trigger_Threshold</c> members go through <see cref="Threshold"/>'s job-taking
        /// overloads. <c>managerGetter</c> stays bound here anyway, because
        /// <see cref="AncientDangerRectCount"/> needs the manager itself, not just its map.
        ///
        /// MUTATION VEHICLES — every write here is one of the mod's own widget bodies:
        /// <list type="bullet">
        /// <item>Property setters the mod's toggle delegates call and methods it calls verbatim are
        /// doctrine A/B: the <c>DeconstructBuildings</c> setter carries the mod's filter-sync
        /// bookkeeping, the padlock setters drop their cached list, and the allow-setters carry the
        /// sync-to-filter bookkeeping.</item>
        /// <item>The remaining toggles are drawn by handing <c>Utilities.DrawToggle</c> a
        /// <c>ref bool</c>, so the toggle's whole mutation IS a write to that plain public field.
        /// <see cref="SetField"/> is their single write site and carries the MUTATION-C marker;
        /// each wrapper cites the draw call it reproduces. <c>MiningArea</c> is the same shape,
        /// written by the area strip itself.</item>
        /// <item><see cref="OpenThresholdDetails"/> writes the mining-specific sync field, then
        /// hands off to <see cref="Threshold.OpenDetails"/>.</item>
        /// <item><see cref="SwapTasks"/> reproduces <c>DrawTaskPriorityOrder</c>'s own swap through
        /// the field's <c>IList</c> indexer — the one sanctioned MUTATION-C for a plain field
        /// write, since the priority list has no public reorder method.</item>
        /// </list>
        /// </summary>
        internal static class Mining
        {
            private static readonly Type tabType;

            private static readonly MethodInfo triggerGetter;
            private static readonly MethodInfo managerGetter;
            private static readonly MethodInfo ancientDangerRectsGetter;

            private static readonly FieldInfo mineThickRoofsField;
            private static readonly FieldInfo allowMiningField;
            private static readonly FieldInfo takeOwnershipOfMiningJobsField;
            private static readonly FieldInfo controlDeepDrillsField;
            private static readonly FieldInfo checkRoofSupportField;
            private static readonly FieldInfo checkRoofSupportAdvancedField;
            private static readonly FieldInfo checkRoomDivisionField;
            private static readonly FieldInfo countChunkProductsField;
            private static readonly FieldInfo haulMapChunksField;
            private static readonly FieldInfo haulMinedChunksField;
            private static readonly FieldInfo deconstructAncientDangerWhenFoggedField;
            private static readonly FieldInfo invertMiningAreaField;
            private static readonly FieldInfo syncFilterAndAllowedField;
            private static readonly FieldInfo miningAreaField;
            private static readonly FieldInfo syncField;
            private static readonly FieldInfo allowedBuildingsField;
            private static readonly FieldInfo allowedMineralsField;
            private static readonly FieldInfo taskPriorityOrderField;

            private static readonly MethodInfo deconstructBuildingsGetter;
            private static readonly MethodInfo deconstructBuildingsSetter;
            private static readonly MethodInfo mineralsLockedGetter;
            private static readonly MethodInfo mineralsLockedSetter;
            private static readonly MethodInfo buildingsLockedGetter;
            private static readonly MethodInfo buildingsLockedSetter;
            private static readonly MethodInfo allMineralsGetter;
            private static readonly MethodInfo allDeconstructibleBuildingsGetter;
            private static readonly MethodInfo setAllowMineralMethod;
            private static readonly MethodInfo setBuildingAllowedMethod;
            private static readonly MethodInfo refreshAllBuildingsAndMineralsMethod;
            private static readonly MethodInfo getChunkProductKindMethod;
            private static readonly MethodInfo getMineralTooltipMethod;
            private static readonly MethodInfo getBuildingTooltipMethod;

            private static readonly MethodInfo targetLabelGetter;
            private static readonly MethodInfo currentCountMethod;

            private static readonly MethodInfo chunksCachedValueGetter;
            private static readonly MethodInfo designatedCachedValueGetter;
            private static readonly MethodInfo cacheUpdateMethod;
            private static readonly MethodInfo cacheForceUpdateMethod;
            private static readonly MethodInfo cacheValueGetter;

            private static readonly object filterToAllowedSync;

            private static readonly bool ready;

            static Mining()
            {
                var surface = new ReflectionSurface("CmrCompat.Mining");

                Type jobType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Mining");
                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Mining");
                Type managerType = surface.Type("ColonyManagerRedux.Manager");
                Type baseJobType = surface.Type("ColonyManagerRedux.ManagerJob");
                Type triggerType = surface.Type("ColonyManagerRedux.Trigger_Threshold");

                triggerGetter = Getter(surface.Property(jobType, "TriggerThreshold"));
                managerGetter = Getter(surface.Property(baseJobType, "Manager"));
                ancientDangerRectsGetter = Getter(surface.Property(managerType, "AncientDangerRects"));

                mineThickRoofsField = surface.Field(jobType, "MineThickRoofs");
                allowMiningField = surface.Field(jobType, "AllowMining");
                takeOwnershipOfMiningJobsField = surface.Field(jobType, "TakeOwnershipOfMiningJobs");
                controlDeepDrillsField = surface.Field(jobType, "ControlDeepDrills");
                checkRoofSupportField = surface.Field(jobType, "CheckRoofSupport");
                checkRoofSupportAdvancedField = surface.Field(jobType, "CheckRoofSupportAdvanced");
                checkRoomDivisionField = surface.Field(jobType, "CheckRoomDivision");
                countChunkProductsField = surface.Field(jobType, "CountChunkProducts");
                haulMapChunksField = surface.Field(jobType, "HaulMapChunks");
                haulMinedChunksField = surface.Field(jobType, "HaulMinedChunks");
                deconstructAncientDangerWhenFoggedField =
                    surface.Field(jobType, "DeconstructAncientDangerWhenFogged");
                invertMiningAreaField = surface.Field(jobType, "InvertMiningArea");
                syncFilterAndAllowedField = surface.Field(jobType, "SyncFilterAndAllowed");
                miningAreaField = surface.Field(jobType, "MiningArea");
                syncField = surface.Field(jobType, "Sync");
                filterToAllowedSync = surface.EnumValue(syncField != null ? syncField.FieldType : null, "FilterToAllowed");
                allowedBuildingsField = surface.Field(jobType, "AllowedBuildings");
                allowedMineralsField = surface.Field(jobType, "AllowedMinerals");
                taskPriorityOrderField = surface.Field(jobType, "TaskPriorityOrder");

                PropertyInfo deconstructBuildingsProperty = surface.Property(jobType, "DeconstructBuildings");
                deconstructBuildingsGetter = Getter(deconstructBuildingsProperty);
                deconstructBuildingsSetter = surface.Required("ManagerJob_Mining.DeconstructBuildings setter",
                    Setter(deconstructBuildingsProperty));
                PropertyInfo mineralsLockedProperty = surface.Property(jobType, "MineralsLockedToMap");
                mineralsLockedGetter = Getter(mineralsLockedProperty);
                mineralsLockedSetter = surface.Required("ManagerJob_Mining.MineralsLockedToMap setter",
                    Setter(mineralsLockedProperty));
                PropertyInfo buildingsLockedProperty = surface.Property(jobType, "BuildingsLockedToMap");
                buildingsLockedGetter = Getter(buildingsLockedProperty);
                buildingsLockedSetter = surface.Required("ManagerJob_Mining.BuildingsLockedToMap setter",
                    Setter(buildingsLockedProperty));
                allMineralsGetter = Getter(surface.Property(jobType, "AllMinerals"));
                allDeconstructibleBuildingsGetter =
                    Getter(surface.Property(jobType, "AllDeconstructibleBuildings"));
                setAllowMineralMethod = surface.Method(jobType, "SetAllowMineral",
                    new[] { typeof(ThingDef), typeof(bool), typeof(bool) });
                setBuildingAllowedMethod = surface.Method(jobType, "SetBuildingAllowed",
                    new[] { typeof(ThingDef), typeof(bool), typeof(bool) });
                refreshAllBuildingsAndMineralsMethod =
                    surface.Method(jobType, "RefreshAllBuildingsAndMinerals", new Type[0]);
                getChunkProductKindMethod = surface.Method(jobType, "GetChunkProductKind", new Type[0]);
                getMineralTooltipMethod = surface.Method(tabType, "GetMineralTooltip", new[] { typeof(ThingDef) });
                getBuildingTooltipMethod = surface.Method(tabType, "GetBuildingTooltip", new[] { typeof(ThingDef) });

                targetLabelGetter = Getter(surface.Property(triggerType, "TargetLabel"));
                currentCountMethod = surface.Method(triggerType, "GetCurrentCount", new[] { typeof(bool) });

                chunksCachedValueGetter = Getter(surface.Property(jobType, "ChunksCachedValue"));
                designatedCachedValueGetter = Getter(surface.Property(jobType, "DesignatedCachedValue"));
                // Both properties share one closed generic cache type the mod never names
                // publicly, so its accessor's return type is the only handle that survives a
                // version change.
                Type cacheType = chunksCachedValueGetter != null ? chunksCachedValueGetter.ReturnType : null;
                cacheUpdateMethod = surface.Method(cacheType, "DoUpdateIfNeeded", new[] { typeof(bool) });
                cacheForceUpdateMethod = surface.Method(cacheType, "ForceUpdate", new Type[0]);
                cacheValueGetter = Getter(surface.Property(cacheType, "Value"));

                ready = surface.Ready;
            }

            /// <summary>True when every member the Mining detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Mining tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            /// <summary>The manager's ancient-danger rect count for this map, the same count the warning icon gates on (ManagerTab_Mining.cs:373-402).</summary>
            public static int AncientDangerRectCount(object job)
            {
                var manager = Get(managerGetter, job, "AncientDangerRectCount");
                var rects = Get(ancientDangerRectsGetter, manager, "AncientDangerRectCount") as ICollection;
                return rects == null ? 0 : rects.Count;
            }

            // Plain-field toggles; the class remarks carry the mutation vehicle.

            public static bool MineThickRoofs(object job)
            {
                return FieldFlag(mineThickRoofsField, job, "MineThickRoofs");
            }

            /// <summary>Writes the storage the mine-thick-roofs toggle passes by ref (ManagerTab_Mining.cs:425-430).</summary>
            public static void SetMineThickRoofs(object job, bool value)
            {
                SetField(mineThickRoofsField, job, value, "SetMineThickRoofs");
            }

            public static bool AllowMining(object job)
            {
                return FieldFlag(allowMiningField, job, "AllowMining");
            }

            /// <summary>Writes the storage the allow-mining toggle passes by ref (ManagerTab_Mining.cs:299-304).</summary>
            public static void SetAllowMining(object job, bool value)
            {
                SetField(allowMiningField, job, value, "SetAllowMining");
            }

            public static bool TakeOwnershipOfMiningJobs(object job)
            {
                return FieldFlag(takeOwnershipOfMiningJobsField, job, "TakeOwnershipOfMiningJobs");
            }

            /// <summary>Writes the storage the take-ownership toggle passes by ref, drawn only while AllowMining is true (ManagerTab_Mining.cs:309-314).</summary>
            public static void SetTakeOwnershipOfMiningJobs(object job, bool value)
            {
                SetField(takeOwnershipOfMiningJobsField, job, value, "SetTakeOwnershipOfMiningJobs");
            }

            public static bool ControlDeepDrills(object job)
            {
                return FieldFlag(controlDeepDrillsField, job, "ControlDeepDrills");
            }

            /// <summary>Writes the storage the control-deep-drills toggle passes by ref (ManagerTab_Mining.cs:329-334).</summary>
            public static void SetControlDeepDrills(object job, bool value)
            {
                SetField(controlDeepDrillsField, job, value, "SetControlDeepDrills");
            }

            public static bool CheckRoofSupport(object job)
            {
                return FieldFlag(checkRoofSupportField, job, "CheckRoofSupport");
            }

            /// <summary>Writes the storage the check-roof-support toggle passes by ref (ManagerTab_Mining.cs:433-438).</summary>
            public static void SetCheckRoofSupport(object job, bool value)
            {
                SetField(checkRoofSupportField, job, value, "SetCheckRoofSupport");
            }

            public static bool CheckRoofSupportAdvanced(object job)
            {
                return FieldFlag(checkRoofSupportAdvancedField, job, "CheckRoofSupportAdvanced");
            }

            /// <summary>Writes the storage the advanced-roof-support toggle passes by ref, drawn only while CheckRoofSupport is true (ManagerTab_Mining.cs:441-450).</summary>
            public static void SetCheckRoofSupportAdvanced(object job, bool value)
            {
                SetField(checkRoofSupportAdvancedField, job, value, "SetCheckRoofSupportAdvanced");
            }

            public static bool CheckRoomDivision(object job)
            {
                return FieldFlag(checkRoomDivisionField, job, "CheckRoomDivision");
            }

            /// <summary>Writes the storage the check-room-division toggle passes by ref (ManagerTab_Mining.cs:464-470).</summary>
            public static void SetCheckRoomDivision(object job, bool value)
            {
                SetField(checkRoomDivisionField, job, value, "SetCheckRoomDivision");
            }

            public static bool CountChunkProducts(object job)
            {
                return FieldFlag(countChunkProductsField, job, "CountChunkProducts");
            }

            public static void SetCountChunkProducts(object job, bool value)
            {
                SetField(countChunkProductsField, job, value, "SetCountChunkProducts");
                Call(cacheForceUpdateMethod, Get(chunksCachedValueGetter, job, "ChunksCachedValue"), null,
                    "SetCountChunkProducts");
                Call(cacheForceUpdateMethod, Get(designatedCachedValueGetter, job, "DesignatedCachedValue"), null,
                    "SetCountChunkProducts");
            }

            public static bool HaulMapChunks(object job)
            {
                return FieldFlag(haulMapChunksField, job, "HaulMapChunks");
            }

            /// <summary>Writes the storage the haul-map-chunks toggle passes by ref (ManagerTab_Mining.cs:342-347).</summary>
            public static void SetHaulMapChunks(object job, bool value)
            {
                SetField(haulMapChunksField, job, value, "SetHaulMapChunks");
            }

            public static bool HaulMinedChunks(object job)
            {
                return FieldFlag(haulMinedChunksField, job, "HaulMinedChunks");
            }

            /// <summary>Writes the storage the haul-mined-chunks toggle passes by ref (ManagerTab_Mining.cs:350-355).</summary>
            public static void SetHaulMinedChunks(object job, bool value)
            {
                SetField(haulMinedChunksField, job, value, "SetHaulMinedChunks");
            }

            public static bool DeconstructAncientDangerWhenFogged(object job)
            {
                return FieldFlag(deconstructAncientDangerWhenFoggedField, job, "DeconstructAncientDangerWhenFogged");
            }

            /// <summary>Writes the storage the ancient-danger toggle passes by ref, drawn only while DeconstructBuildings is true (ManagerTab_Mining.cs:378-384).</summary>
            public static void SetDeconstructAncientDangerWhenFogged(object job, bool value)
            {
                SetField(deconstructAncientDangerWhenFoggedField, job, value, "SetDeconstructAncientDangerWhenFogged");
            }

            public static bool InvertMiningArea(object job)
            {
                return FieldFlag(invertMiningAreaField, job, "InvertMiningArea");
            }

            /// <summary>Writes the storage the invert-area toggle passes by ref (ManagerTab_Mining.cs:412-418).</summary>
            public static void SetInvertMiningArea(object job, bool value)
            {
                SetField(invertMiningAreaField, job, value, "SetInvertMiningArea");
            }

            public static bool SyncFilterAndAllowed(object job)
            {
                return FieldFlag(syncFilterAndAllowedField, job, "SyncFilterAndAllowed");
            }

            /// <summary>Writes the public field the synchronize-threshold toggle passes by ref (ManagerTab_Mining.cs:512-518).</summary>
            public static void SetSyncFilterAndAllowed(object job, bool value)
            {
                SetField(syncFilterAndAllowedField, job, value, "SetSyncFilterAndAllowed");
            }

            // Mining area.

            public static Area MiningArea(object job)
            {
                if (!ready || job == null || miningAreaField == null)
                {
                    return null;
                }
                try
                {
                    return miningAreaField.GetValue(job) as Area;
                }
                catch (Exception ex)
                {
                    Fail("MiningArea", ex);
                    return null;
                }
            }

            /// <summary>Writes the field the area strip itself writes by ref (AreaAllowedGUI.DoAllowedAreaSelectors, via ManagerTab_Mining.cs:411).</summary>
            public static void SetMiningArea(object job, Area area)
            {
                SetField(miningAreaField, job, area, "SetMiningArea");
            }

            // Deconstruct buildings.

            public static bool DeconstructBuildings(object job)
            {
                return Flag(deconstructBuildingsGetter, job, "DeconstructBuildings");
            }

            /// <summary>The property the deconstruct-buildings toggle writes (ManagerTab_Mining.cs:363-369); the setter carries the mod's own filter-sync bookkeeping.</summary>
            public static void SetDeconstructBuildings(object job, bool value)
            {
                SetProperty(deconstructBuildingsSetter, job, value, "SetDeconstructBuildings");
            }

            // Task priority order. The enum is internal by containment, so its values travel boxed.

            /// <summary>The job's own task order, in its own list order -- the order the priority rows draw in.</summary>
            public static List<object> TaskPriorityOrder(object job)
            {
                var result = new List<object>();
                if (!ready || job == null || taskPriorityOrderField == null)
                {
                    return result;
                }
                try
                {
                    var list = taskPriorityOrderField.GetValue(job) as IList;
                    if (list != null)
                    {
                        foreach (object item in list)
                        {
                            result.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("TaskPriorityOrder", ex);
                }
                return result;
            }

            /// <summary>The enum member's own name, for composing the mod's own per-task label key.</summary>
            public static string TaskName(object task)
            {
                return task == null ? "" : task.ToString();
            }

            /// <summary>
            /// Swaps the two list entries in place through the field's own <c>IList</c> indexer.
            /// MUTATION-C: mirrors <c>ManagerTab_Mining.DrawTaskPriorityOrder</c>'s own
            /// <c>tasks.Swap(i, j)</c> call (ManagerTab_Mining.cs:286); the up/down buttons are inline
            /// IMGUI lambdas with no invokable reorder method, and the list has no public swap of its
            /// own to call instead.
            /// </summary>
            public static void SwapTasks(object job, int indexA, int indexB)
            {
                if (!ready || job == null || taskPriorityOrderField == null)
                {
                    return;
                }
                try
                {
                    var list = taskPriorityOrderField.GetValue(job) as IList;
                    if (list == null || indexA < 0 || indexB < 0
                        || indexA >= list.Count || indexB >= list.Count)
                    {
                        return;
                    }
                    object temp = list[indexA];
                    list[indexA] = list[indexB];
                    list[indexB] = temp;
                }
                catch (Exception ex)
                {
                    Fail("SwapTasks", ex);
                }
            }

            // Minerals.

            /// <summary>Every mineral the job offers, in the mod's own order.</summary>
            public static List<ThingDef> AllMinerals(object job)
            {
                return DefList(allMineralsGetter, job, "AllMinerals");
            }

            public static bool IsMineralAllowed(object job, ThingDef mineral)
            {
                return DefSetContains(allowedMineralsField, job, mineral, "IsMineralAllowed");
            }

            /// <summary>The exact call every mineral toggle and shortcut hands to the mod's own widgets (ManagerTab_Mining.cs:128, 153, 166, 181, 198).</summary>
            public static void SetAllowMineral(object job, ThingDef mineral, bool allow)
            {
                if (mineral == null)
                {
                    return;
                }
                Call(setAllowMineralMethod, job, new object[] { mineral, allow, true }, "SetAllowMineral");
            }

            public static bool MineralsLockedToMap(object job)
            {
                return Flag(mineralsLockedGetter, job, "MineralsLockedToMap");
            }

            /// <summary>The padlock icon's own write (ManagerTab_Mining.cs:691); the setter drops the cached mineral list itself.</summary>
            public static void SetMineralsLockedToMap(object job, bool value)
            {
                SetProperty(mineralsLockedSetter, job, value, "SetMineralsLockedToMap");
            }

            /// <summary>The mod's own hover text for a mineral row: description, then chunk or bar yield and drop chance (ManagerTab_Mining.cs:27-50).</summary>
            public static string MineralTooltip(ThingDef mineral)
            {
                if (!ready || mineral == null || getMineralTooltipMethod == null)
                {
                    return "";
                }
                return Call(getMineralTooltipMethod, null, new object[] { mineral }, "MineralTooltip") as string ?? "";
            }

            public static string BuildingTooltip(ThingDef building)
            {
                if (!ready || building == null || getBuildingTooltipMethod == null)
                {
                    return "";
                }
                return Call(getBuildingTooltipMethod, null, new object[] { building }, "BuildingTooltip") as string ?? "";
            }

            // Deconstructible buildings.

            /// <summary>Every deconstructible building the job offers, in the mod's own order.</summary>
            public static List<ThingDef> AllDeconstructibleBuildings(object job)
            {
                return DefList(allDeconstructibleBuildingsGetter, job, "AllDeconstructibleBuildings");
            }

            public static bool IsBuildingAllowed(object job, ThingDef building)
            {
                return DefSetContains(allowedBuildingsField, job, building, "IsBuildingAllowed");
            }

            /// <summary>The exact call the building toggle and its shortcut hand to the mod's own widget (ManagerTab_Mining.cs:83, 110).</summary>
            public static void SetBuildingAllowed(object job, ThingDef building, bool allow)
            {
                if (building == null)
                {
                    return;
                }
                Call(setBuildingAllowedMethod, job, new object[] { building, allow, true }, "SetBuildingAllowed");
            }

            public static bool BuildingsLockedToMap(object job)
            {
                return Flag(buildingsLockedGetter, job, "BuildingsLockedToMap");
            }

            /// <summary>The padlock icon's own write (ManagerTab_Mining.cs:729); the setter drops the cached building list itself.</summary>
            public static void SetBuildingsLockedToMap(object job, bool value)
            {
                SetProperty(buildingsLockedSetter, job, value, "SetBuildingsLockedToMap");
            }

            /// <summary>The refresh icon's own call for both def-list columns at once (ManagerTab_Mining.cs:670-673).</summary>
            public static void RefreshAllBuildingsAndMinerals(object job)
            {
                Call(refreshAllBuildingsAndMineralsMethod, job, null, "RefreshAllBuildingsAndMinerals");
            }

            // Threshold.

            public static int CurrentCount(object job)
            {
                object trigger = Trigger(job);
                if (trigger == null || currentCountMethod == null)
                {
                    return 0;
                }
                object value = Call(currentCountMethod, trigger, new object[] { true }, "CurrentCount");
                return value is int count ? count : 0;
            }

            /// <summary>The map's uncollected chunk yield the threshold summary reads (ManagerTab_Mining.cs:480-481).</summary>
            public static int ChunkCount(object job)
            {
                return CachedCount(chunksCachedValueGetter, job, "ChunkCount");
            }

            /// <summary>The job's outstanding designations' expected yield (ManagerTab_Mining.cs:482-483).</summary>
            public static int DesignatedCount(object job)
            {
                return CachedCount(designatedCachedValueGetter, job, "DesignatedCount");
            }

            private static int CachedCount(MethodInfo accessor, object job, string member)
            {
                if (!ready || job == null || accessor == null || cacheUpdateMethod == null)
                {
                    return 0;
                }
                object cache = Call(accessor, job, null, member);
                if (cache == null)
                {
                    return 0;
                }
                Call(cacheUpdateMethod, cache, new object[] { false }, member);
                object value = Get(cacheValueGetter, cache, member);
                return value is int count ? count : 0;
            }

            /// <summary>The chunk product kind the threshold tooltip's fifth argument names (ManagerTab_Mining.cs:485, 502).</summary>
            public static string ChunkProductKindName(object job)
            {
                if (!ready || job == null || getChunkProductKindMethod == null)
                {
                    return "";
                }
                object kind = Call(getChunkProductKindMethod, job, null, "ChunkProductKindName");
                return kind == null ? "" : kind.ToString();
            }

            /// <summary>The trigger's own operator-and-count label ("&lt; 500"), verbatim.</summary>
            public static string TargetLabel(object job)
            {
                return Get(targetLabelGetter, Trigger(job), "TargetLabel") as string ?? "";
            }

            /// <summary>
            /// Opens the mod's threshold details window as clicking the threshold label does: write
            /// the mining-specific sync field the click's delegate carries, then hand off to the
            /// shared threshold body.
            /// </summary>
            public static void OpenThresholdDetails(object job)
            {
                // MUTATION-C: the field write is the delegate ManagerTab_Mining.cs:505-508 hands the
                // shared threshold click body (Trigger_Threshold.DrawTriggerConfig,
                // Trigger_Threshold.cs:501-505): for a mining job, onOpenFilterDetails is
                // `job.Sync = Utilities.SyncDirection.FilterToAllowed`.
                Threshold.OpenDetails(job,
                    () => SetField(syncField, job, filterToAllowedSync, "OpenThresholdDetails"));
            }

            // Plumbing, gated on this block's own Ready so a rename here disturbs nothing else.

            private static List<ThingDef> DefList(MethodInfo getter, object job, string member)
            {
                var defs = new List<ThingDef>();
                var items = Get(getter, job, member) as IEnumerable;
                if (items == null)
                {
                    return defs;
                }
                try
                {
                    foreach (object item in items)
                    {
                        if (item is ThingDef def)
                        {
                            defs.Add(def);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
                return defs;
            }

            private static bool DefSetContains(FieldInfo field, object job, ThingDef def, string member)
            {
                if (!ready || field == null || job == null || def == null)
                {
                    return false;
                }
                try
                {
                    var set = field.GetValue(job) as ICollection<ThingDef>;
                    return set != null && set.Contains(def);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return false;
                }
            }

            private static object Trigger(object job)
            {
                return Get(triggerGetter, job, "TriggerThreshold");
            }

            private static object Get(MethodInfo getter, object instance, string member)
            {
                if (!ready || getter == null || (instance == null && !getter.IsStatic))
                {
                    return null;
                }
                try
                {
                    return getter.Invoke(instance, null);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static bool Flag(MethodInfo getter, object instance, string member)
            {
                return Get(getter, instance, member) is bool value && value;
            }

            private static bool FieldFlag(FieldInfo field, object instance, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return false;
                }
                try
                {
                    return field.GetValue(instance) is bool value && value;
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return false;
                }
            }

            private static object Call(MethodInfo method, object instance, object[] args, string member)
            {
                if (!ready || method == null || (instance == null && !method.IsStatic))
                {
                    return null;
                }
                try
                {
                    return method.Invoke(instance, args);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static void SetProperty(MethodInfo setter, object instance, object value, string member)
            {
                if (!ready || setter == null || instance == null)
                {
                    return;
                }
                try
                {
                    setter.Invoke(instance, new[] { value });
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
            }

            /// <summary>
            /// Shared write primitive for the plain public fields the mod's <c>ref</c>-parameter
            /// widgets write. Each wrapper above cites the draw call it reproduces.
            /// </summary>
            private static void SetField(FieldInfo field, object instance, object value, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: the write the mod's own widget performs through its ref parameter --
                    // see this class's remarks and each wrapper's cited draw call. None of these fields
                    // has a gated setter to invoke instead.
                    field.SetValue(instance, value);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
            }
        }
    }
}
