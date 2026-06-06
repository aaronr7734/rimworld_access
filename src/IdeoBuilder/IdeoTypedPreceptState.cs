using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Windowless overlay for editing one of the typed precept lists (roles, rituals, buildings,
    /// relics, weapons, venerated animals, preferred xenotypes, apparel). Opened from the builder
    /// hub.
    ///
    /// Presents the current precepts of the type as a tree (each expandable to read details), plus
    /// an "Add" node at the top. Enter on "Add" invokes vanilla's IdeoUIUtility.AddPrecept via
    /// reflection — its FloatMenu (including any nested grouping menus) is redirected to the
    /// accessible WindowlessFloatMenuState while this state is active. Delete removes the focused
    /// precept.
    ///
    /// Keys:
    ///   Up/Down/Home/End/Left/Right — tree navigation
    ///   Page Up/Down — jump between detail sections (e.g. a role's Abilities / Requirements)
    ///   Enter — Add (on the Add node) / re-announce (on a precept)
    ///   ] — edit the focused precept (rename, leader title, name lock, or vanilla inline edits
    ///       such as a weapon's swap-noble-and-despised), where the precept type supports it
    ///   Delete — remove the focused precept
    ///   Space — re-announce
    ///   A-Z / 0-9 — typeahead
    ///   Escape — close, return to hub
    /// </summary>
    public static class IdeoTypedPreceptState
    {
        public static bool IsActive { get; private set; }

        private static Ideo ideo;
        private static IdeoBuilderHelper.SectionKind kind;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("IdeoTypedPrecept");
        private static bool configured;

        private static readonly System.Reflection.MethodInfo AddPreceptMethod =
            AccessTools.Method(typeof(IdeoUIUtility), "AddPrecept");

        public static void Open(Ideo targetIdeo, IdeoBuilderHelper.SectionKind sectionKind)
        {
            if (targetIdeo == null) return;
            ideo = targetIdeo;
            kind = sectionKind;
            IsActive = true;
            EnsureConfigured();
            RebuildTree();
            AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            ideo = null;
            treeNav.Reset();
        }

        private static void EnsureConfigured()
        {
            if (configured) return;
            configured = true;
            treeNav.AnnounceChildCounts = false;
            treeNav.FormatItemAnnouncement = FormatItem;
            treeNav.FormatStateChangeAnnouncement = FormatStateChange;
            treeNav.FormatSearchAnnouncement = FormatSearch;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnDelete = HandleDelete;
            // Page Up/Down jump between a precept's detail-section headers (e.g. a role's
            // Abilities / Has-role-in-rituals / Requirements / Effects sections).
            treeNav.IsSectionBoundary = item => item.IsSectionHeader;
        }

        #region Predicate / filter per kind

        private static Func<Precept, bool> CurrentPreceptPredicate(IdeoBuilderHelper.SectionKind k)
        {
            switch (k)
            {
                case IdeoBuilderHelper.SectionKind.Roles: return p => p is Precept_Role;
                // Exact type + visible, matching vanilla's rituals filter — excludes hidden rituals
                // and Precept_Ritual subclasses like Precept_GravshipLaunch that shouldn't appear.
                case IdeoBuilderHelper.SectionKind.Rituals: return p => p.def.preceptClass == typeof(Precept_Ritual) && p.def.visible;
                case IdeoBuilderHelper.SectionKind.Buildings: return p => p is Precept_Building || p is Precept_RitualSeat;
                case IdeoBuilderHelper.SectionKind.Relics: return p => p is Precept_Relic;
                case IdeoBuilderHelper.SectionKind.Weapons: return p => p is Precept_Weapon;
                case IdeoBuilderHelper.SectionKind.VeneratedAnimals: return p => p is Precept_Animal;
                case IdeoBuilderHelper.SectionKind.PreferredXenotypes: return p => p is Precept_Xenotype;
                case IdeoBuilderHelper.SectionKind.Apparel: return p => p is Precept_Apparel;
                default: return p => false;
            }
        }

        private static Func<PreceptDef, bool> AddFilter(IdeoBuilderHelper.SectionKind k)
        {
            switch (k)
            {
                case IdeoBuilderHelper.SectionKind.Roles: return p => typeof(Precept_Role).IsAssignableFrom(p.preceptClass);
                case IdeoBuilderHelper.SectionKind.Rituals: return p => p.preceptClass == typeof(Precept_Ritual);
                case IdeoBuilderHelper.SectionKind.Buildings: return p => p.preceptClass == typeof(Precept_Building) || p.preceptClass == typeof(Precept_RitualSeat);
                case IdeoBuilderHelper.SectionKind.Relics: return p => p.preceptClass == typeof(Precept_Relic);
                case IdeoBuilderHelper.SectionKind.Weapons: return p => p.preceptClass == typeof(Precept_Weapon);
                case IdeoBuilderHelper.SectionKind.VeneratedAnimals: return p => p.preceptClass == typeof(Precept_Animal);
                case IdeoBuilderHelper.SectionKind.PreferredXenotypes: return p => p.preceptClass == typeof(Precept_Xenotype);
                case IdeoBuilderHelper.SectionKind.Apparel: return p => p.preceptClass == typeof(Precept_Apparel);
                default: return p => false;
            }
        }

        #endregion

        #region Tree

        public static void RebuildTree()
        {
            if (ideo == null) return;

            // Remember the focused precept so an edit / swap / refresh keeps the cursor on it
            // rather than snapping back to the "Add" row at the top.
            Precept focused = null;
            if (treeNav.RootItem != null)
            {
                var sel = treeNav.SelectedItem;
                focused = (sel?.Data as Precept) ?? (sel?.Parent?.Data as Precept);
            }

            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category,
            };

            // "Add" action node at the top.
            string typeLabel = IdeoBuilderHelper.GetLocalizedSectionLabel(kind);
            root.Children.Add(new InspectionTreeItem
            {
                Label = "Add".Translate().ToString() + " " + typeLabel,
                IndentLevel = 0,
                IsExpandable = false,
                Type = InspectionTreeItem.ItemType.Item,
                Data = "ADD",
                Parent = root,
            });

            // Current precepts of this type. Each is an expandable node: collapsed reads its full
            // details inline; expanded reads the short subject label and exposes the details as
            // child lines (same pattern as the meme picker).
            var pred = CurrentPreceptPredicate(kind);
            var current = ideo.PreceptsListForReading.Where(pred).ToList();
            foreach (var precept in current)
            {
                string shortLabel = IdeoBuilderHelper.PreceptLabel(precept);
                var detailLines = BuildPreceptDetailLines(precept);

                var node = new InspectionTreeItem
                {
                    ExpandedLabel = shortLabel,
                    Label = detailLines.Count > 0
                        ? shortLabel + ". " + string.Join(". ", detailLines.Select(d => d.Text))
                        : shortLabel,
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Data = precept,
                    LinkedDef = IdeologyHelper_GetPreceptDef(precept),
                    Parent = root,
                };
                foreach (var line in detailLines)
                {
                    node.Children.Add(new InspectionTreeItem
                    {
                        Label = line.Text,
                        IsSectionHeader = line.IsHeader,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Parent = node,
                    });
                }
                root.Children.Add(node);
            }

            treeNav.Initialize(root);

            // Restore the cursor onto the previously-focused precept after the rebuild.
            if (focused != null)
            {
                var items = treeNav.VisibleItems;
                for (int i = 0; i < items.Count; i++)
                {
                    if (ReferenceEquals(items[i].Data, focused))
                    {
                        treeNav.SetSelectedIndex(i);
                        break;
                    }
                }
            }
        }

        private static Def IdeologyHelper_GetPreceptDef(Precept precept)
        {
            // Surface a ThingDef/XenotypeDef etc. for Alt+I info card where available.
            if (precept is Precept_ThingDef ptd && ptd.ThingDef != null) return ptd.ThingDef;
            return null;
        }

        /// <summary>One detail line plus whether it is a section heading (for Page Up/Down).</summary>
        private struct DetailLine
        {
            public readonly string Text;
            public readonly bool IsHeader;
            public DetailLine(string text, bool isHeader) { Text = text; IsHeader = isHeader; }
        }

        // The opening tag vanilla wraps section titles in (ColorizeDescTitle uses
        // ColoredText.TipSectionTitleColor). Built via the same Colorize extension so it matches
        // exactly without hardcoding the hex; used to detect headings in the raw GetTip() text.
        private static readonly string SectionTitlePrefix = BuildSectionTitlePrefix();

        private static string BuildSectionTitlePrefix()
        {
            try
            {
                string sample = "x".Colorize(ColoredText.TipSectionTitleColor); // <color=#...>x</color>
                int gt = sample.IndexOf('>');
                return gt > 0 ? sample.Substring(0, gt + 1) : null;
            }
            catch { return null; }
        }

        private static bool IsSectionTitleLine(string rawLine)
        {
            if (string.IsNullOrEmpty(SectionTitlePrefix) || string.IsNullOrEmpty(rawLine)) return false;
            return rawLine.TrimStart().StartsWith(SectionTitlePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the detail lines for a precept from vanilla's own tooltip (GetTip), cleaned of
        /// markup and unresolved grammar tokens, flagging section-title lines so Page Up/Down can
        /// jump between them. For precepts that grant abilities (roles, ritual roles), each ability's
        /// description is injected inline with its tip bullet — vanilla's tip lists ability NAMES only
        /// ("- Leader speech"), so without this they read as bare names. Reuses the read-only Ideology
        /// viewer's own `EnhanceWithAbilityDescriptions` so the editor and viewer present abilities
        /// identically (one "- Leader speech. {what it does}" line per ability, not a separate
        /// disconnected list).
        /// </summary>
        private static List<DetailLine> BuildPreceptDetailLines(Precept precept)
        {
            var lines = new List<DetailLine>();

            string tip = precept.GetTip();
            if (precept.def != null && !precept.def.grantedAbilities.NullOrEmpty())
                tip = IdeologyHelper.EnhanceWithAbilityDescriptions(precept.def.grantedAbilities, precept.ideo, tip);

            if (!string.IsNullOrEmpty(tip))
            {
                foreach (var raw in tip.Split('\n'))
                {
                    bool isHeader = IsSectionTitleLine(raw);
                    string line = IdeoBuilderHelper.CleanGameText(raw);
                    if (!string.IsNullOrEmpty(line))
                        lines.Add(new DetailLine(line, isHeader));
                }
            }

            return lines;
        }

        #endregion

        #region Formatters / activation / delete

        private static string FormatItem(InspectionTreeItem item)
        {
            // Detail lines read as just their text — no position/level chatter.
            if (item.Type == InspectionTreeItem.ItemType.DetailText)
                return item.Label;

            var sb = new StringBuilder();
            // Smart label: expanded precept reads its short subject label (details are now child
            // nodes); collapsed reads the full inline details.
            sb.Append(item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                ? item.ExpandedLabel : item.Label);
            if (item.IsExpandable)
                sb.Append(item.IsExpanded ? ", expanded" : ", collapsed");

            var (pos, total) = treeNav.GetSiblingPosition(item);
            string position = MenuHelper.FormatPosition(pos - 1, total);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);

            string levelSuffix = MenuHelper.GetLevelSuffix("IdeoTypedPrecept", item.IndentLevel);
            if (!string.IsNullOrEmpty(levelSuffix))
                sb.Append(levelSuffix);

            return sb.ToString();
        }

        private static string FormatStateChange(InspectionTreeItem item)
        {
            string state = item.IsExpanded ? "Expanded" : "Collapsed";
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            return state + ". " + label;
        }

        private static string FormatSearch(InspectionTreeItem item, TypeaheadSearchHelper t)
        {
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            return $"{label}, {t.CurrentMatchPosition} of {t.MatchCount} matches for '{t.SearchBuffer}'";
        }

        private static bool HandleActivate(InspectionTreeItem item)
        {
            if (item?.Data is string s && s == "ADD")
            {
                InvokeAddPrecept();
                return true;
            }
            return false; // precept nodes fall through to expand/collapse
        }

        private static bool HandleDelete(InspectionTreeItem item)
        {
            // Allow deleting from a detail-line child too, by resolving up to its precept node.
            var precept = (item?.Data as Precept) ?? (item?.Parent?.Data as Precept);
            if (precept == null) return false;

            // Mirror vanilla's removal guard (Precept.DrawPreceptBox): some precepts can't be
            // removed in the UI, and a precept required by a meme can't be removed at all.
            if (!precept.def.canRemoveInUI || precept.def.issue.HasDefaultPrecept)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("CannotRemove".Translate() + ": " + IdeoBuilderHelper.PreceptLabel(precept), SpeechPriority.High);
                return true;
            }
            var requiringMeme = ideo.GetMemeThatRequiresPrecept(precept.def);
            if (requiringMeme != null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("CannotRemove".Translate() + ": " + "RequiredByMeme".Translate(requiringMeme.label), SpeechPriority.High);
                return true;
            }

            string removedName = IdeoBuilderHelper.PreceptLabel(precept);
            ideo.RemovePrecept(precept);
            ideo.anyPreceptEdited = true;
            ideo.RegenerateDescription();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            TolkHelper.Speak($"{removedName}, removed");
            RebuildTree();
            return true;
        }

        #endregion

        #region Add precept (reflection into vanilla)

        private static void InvokeAddPrecept()
        {
            if (AddPreceptMethod == null)
            {
                Log.Error("[RimWorld Access] Could not find IdeoUIUtility.AddPrecept");
                return;
            }
            try
            {
                bool group = kind == IdeoBuilderHelper.SectionKind.Precepts; // typed lists are ungrouped
                AddPreceptMethod.Invoke(null, new object[]
                {
                    ideo, IdeoEditMode.GameStart, AddFilter(kind), group
                });
                // Vanilla builds a FloatMenu and adds it to the WindowStack; our interception
                // (IdeoTypedPreceptFloatMenuRedirect) converts it to a WindowlessFloatMenuState.
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error invoking AddPrecept: {ex}");
            }
        }

        /// <summary>Called by the float-menu redirect after the player picks an option, so the
        /// tree reflects the newly added precept.</summary>
        public static void NotifyPreceptAdded()
        {
            if (!IsActive) return;
            ideo.RegenerateDescription();
            RebuildTree();
            treeNav.ReannounceCurrentItem();
        }

        #endregion

        #region Edit precept (] context menu)

        private static readonly TextInputController editController = new TextInputController();

        // Vanilla precept-name rules (Dialog_EditPrecept): letters/digits/space/'/-, max 32 chars.
        private static readonly Regex ValidPreceptNameRegex = new Regex("^[\\p{L}0-9 '\\-]*$");
        private const int MaxPreceptNameLength = 32;

        private static TextFieldSpec PreceptNameSpec(string labelKey) =>
            new TextFieldSpec(labelKey, maxLength: MaxPreceptNameLength, minLength: 1, allowedChars: ValidPreceptNameRegex);

        /// <summary>
        /// Opens an edit-actions context menu for the focused precept. Roles, relics, buildings and
        /// rituals — which vanilla edits through the inaccessible Dialog_EditPrecept — get an
        /// accessible rename, a name-lock toggle, and (for the leader role) male/female leader-title
        /// fields. Weapons, apparel and ritual seats surface vanilla's own inline EditFloatMenuOptions
        /// (swap noble/despised, set gender/type, replace building) directly, since those mutate the
        /// precept without a dialog. Animals and xenotypes have no vanilla edit options.
        /// </summary>
        private static void OpenEditMenu()
        {
            var item = treeNav.SelectedItem;
            var precept = (item?.Data as Precept) ?? (item?.Parent?.Data as Precept);
            if (precept == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var options = BuildEditOptions(precept);
            if (options.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No edit options");
                return;
            }

            TolkHelper.Speak("Edit".Translate() + " " + IdeoBuilderHelper.PreceptLabel(precept));
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static List<FloatMenuOption> BuildEditOptions(Precept precept)
        {
            var options = new List<FloatMenuOption>();

            // Each type mirrors the controls vanilla's Dialog_EditPrecept shows for it, surfaced as
            // accessible float-menu actions applied live.
            switch (precept)
            {
                case Precept_Role role:
                    if (role.def.leaderRole)
                    {
                        // Leader role: edit the ideo's leader title per gender, not a precept name.
                        options.Add(new FloatMenuOption(
                            "LeaderTitle".Translate() + " (" + Gender.Male.GetLabel() + ")",
                            () => BeginLeaderTitleEdit(role, female: false)));
                        options.Add(new FloatMenuOption(
                            "LeaderTitle".Translate() + " (" + Gender.Female.GetLabel() + ")",
                            () => BeginLeaderTitleEdit(role, female: true)));
                    }
                    else
                    {
                        AddNameAndLockOptions(role, options);
                    }
                    if (role.ApparelRequirements != null)
                        options.Add(new FloatMenuOption("EditApparelRequirement".Translate(),
                            () => OpenApparelRequirementsMenu(role)));
                    break;

                case Precept_Relic relic:
                    AddNameAndLockOptions(relic, options);
                    if (relic.ThingDef != null && relic.ThingDef.MadeFromStuff)
                        options.Add(new FloatMenuOption("ChooseStuffForRelic".Translate() + "...",
                            () => OpenRelicStuffMenu(relic)));
                    break;

                case Precept_Building building:
                    AddNameAndLockOptions(building, options);
                    var styles = StylesForBuilding(building);
                    if (styles.Count > 1)
                        options.Add(new FloatMenuOption("Appearance".Translate() + "...",
                            () => OpenBuildingStyleMenu(building, styles)));
                    break;

                case Precept_Ritual ritual:
                    AddNameAndLockOptions(ritual, options);
                    AddRitualEditOptions(ritual, options);
                    break;
            }

            // Inline vanilla edits that mutate the precept directly (weapon swap, apparel gender/
            // type, ritual-seat replacement).
            if (HasInlineEditOptions(precept))
            {
                var vanilla = precept.EditFloatMenuOptions();
                if (vanilla != null)
                    foreach (var opt in vanilla)
                        options.Add(opt);
            }

            return options;
        }

        private static void AddNameAndLockOptions(Precept precept, List<FloatMenuOption> options)
        {
            options.Add(new FloatMenuOption("EditName".Translate(), () => BeginRename(precept)));
            options.Add(new FloatMenuOption(NameLockText(precept), () => ToggleNameLock(precept)));
        }

        // Types whose EditFloatMenuOptions mutate the precept inline (no dialog).
        private static bool HasInlineEditOptions(Precept p) =>
            p is Precept_Weapon || p is Precept_Apparel || p is Precept_RitualSeat;

        private static void BeginRename(Precept precept)
        {
            editController.Begin(precept.Label, PreceptNameSpec("Name"),
                text => { precept.SetName(text.Trim()); AfterPreceptEdit(precept); });
        }

        private static void BeginLeaderTitleEdit(Precept_Role role, bool female)
        {
            string current = female
                ? (string.IsNullOrEmpty(ideo.leaderTitleFemale) ? role.Label : ideo.leaderTitleFemale)
                : (string.IsNullOrEmpty(ideo.leaderTitleMale) ? role.Label : ideo.leaderTitleMale);
            editController.Begin(current, PreceptNameSpec("LeaderTitle"),
                text =>
                {
                    string title = text.Trim();
                    if (female)
                    {
                        ideo.leaderTitleFemale = title;
                    }
                    else
                    {
                        role.SetName(title);            // vanilla sets the precept name to the male title
                        ideo.leaderTitleMale = title;
                    }
                    AfterPreceptEdit(role);
                });
        }

        private static void ToggleNameLock(Precept precept)
        {
            precept.nameLocked = !precept.nameLocked;
            (precept.nameLocked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            TolkHelper.Speak(NameLockText(precept), SpeechPriority.High);
        }

        private static string NameLockText(Precept precept) =>
            (precept.nameLocked ? "LockInOn" : "LockInOff")
                .Translate("PreceptName".Translate(), "PreceptNameLower".Translate());

        #region Relic stuff

        private static void OpenRelicStuffMenu(Precept_Relic relic)
        {
            var options = new List<FloatMenuOption>();
            foreach (var stuff in GenStuff.AllowedStuffsFor(relic.ThingDef))
            {
                var captured = stuff;
                string label = stuff.LabelCap;
                if (stuff == relic.stuff) label += ", current";
                options.Add(new FloatMenuOption(label, () => { relic.stuff = captured; AfterPreceptEdit(relic); }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            TolkHelper.Speak("RelicStuff".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        #endregion

        #region Building style

        // Mirrors Dialog_EditPrecept.StylesForBuilding: every (style, category) pair the building's
        // ThingDef can take under this ideoligion.
        private static List<StyleCategoryPair> StylesForBuilding(Precept_Building building)
        {
            var thingDef = building.ThingDef;
            if (thingDef != null && thingDef.canEditAnyStyle)
                return Precept_ThingDef.AllPossibleStylesForBuilding(thingDef);

            var result = new List<StyleCategoryPair>();
            if (thingDef == null) return result;
            foreach (var cat in ideo.thingStyleCategories)
                foreach (var tds in cat.category.thingDefStyles)
                    if (tds.ThingDef == thingDef)
                        result.Add(new StyleCategoryPair { category = cat.category, styleDef = tds.StyleDef });
            return result;
        }

        private static void OpenBuildingStyleMenu(Precept_Building building, List<StyleCategoryPair> styles)
        {
            var current = ideo.GetStyleAndCategoryFor(building.ThingDef);
            var options = new List<FloatMenuOption>();
            foreach (var pair in styles)
            {
                var captured = pair;
                string label = pair.category != null ? pair.category.LabelCap.ToString() : "Default".Translate().ToString();
                if (current != null && current.styleDef == pair.styleDef) label += ", current";
                options.Add(new FloatMenuOption(label, () =>
                {
                    ideo.style.SetStyleForThingDef(building.ThingDef, captured);
                    AfterPreceptEdit(building);
                }));
            }
            TolkHelper.Speak("Appearance".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        #endregion

        #region Ritual timing / reward

        private static void AddRitualEditOptions(Precept_Ritual ritual, List<FloatMenuOption> options)
        {
            // Starting condition: anytime vs a fixed date (only when the ritual supports both).
            if (ritual.canBeAnytime && ritual.sourcePattern != null && !ritual.sourcePattern.alwaysStartAnytime)
                options.Add(new FloatMenuOption(
                    "StartingCondition".Translate() + ": " +
                        (ritual.isAnytime ? "StartingCondition_Anytime" : "StartingCondition_Date").Translate(),
                    () => OpenStartingConditionMenu(ritual)));

            // Date — only meaningful when the ritual fires on a date rather than anytime.
            var dateTrigger = ritual.obligationTriggers.OfType<RitualObligationTrigger_Date>().FirstOrDefault();
            if (dateTrigger != null && !ritual.isAnytime)
                options.Add(new FloatMenuOption("Date".Translate() + ": " + dateTrigger.DateString,
                    () => OpenQuadrumMenu(ritual, dateTrigger)));

            // Attached reward.
            if (ritual.SupportsAttachableOutcomeEffect)
                options.Add(new FloatMenuOption(
                    "RitualAttachedReward".Translate() + ": " +
                        (ritual.attachableOutcomeEffect != null
                            ? ritual.attachableOutcomeEffect.LabelCap.ToString()
                            : "None".Translate().ToString()),
                    () => OpenRewardMenu(ritual)));
        }

        private static void OpenStartingConditionMenu(Precept_Ritual ritual)
        {
            var options = new List<FloatMenuOption>
            {
                StartingConditionOption(ritual, anytime: true),
                StartingConditionOption(ritual, anytime: false),
            };
            TolkHelper.Speak("StartingCondition".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static FloatMenuOption StartingConditionOption(Precept_Ritual ritual, bool anytime)
        {
            string label = (anytime ? "StartingCondition_Anytime" : "StartingCondition_Date").Translate();
            if (ritual.isAnytime == anytime) label += ", current";
            return new FloatMenuOption(label, () => { ritual.isAnytime = anytime; AfterPreceptEdit(ritual); });
        }

        private static void OpenQuadrumMenu(Precept_Ritual ritual, RitualObligationTrigger_Date dateTrigger)
        {
            int currentDay = GenDate.DayOfQuadrum((long)dateTrigger.triggerDaysSinceStartOfYear * 60000, 0f);
            var options = new List<FloatMenuOption>();
            foreach (var q in QuadrumUtility.QuadrumsInChronologicalOrder)
            {
                var quadrum = q;
                options.Add(new FloatMenuOption(q.Label(), () => OpenDayMenu(ritual, dateTrigger, quadrum, currentDay)));
            }
            TolkHelper.Speak("Date".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void OpenDayMenu(Precept_Ritual ritual, RitualObligationTrigger_Date dateTrigger, Quadrum quadrum, int currentDay)
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < 15; i++)
            {
                int day = i;
                string label = Find.ActiveLanguageWorker.OrdinalNumber(day + 1);
                if (day == currentDay) label += ", current";
                options.Add(new FloatMenuOption(label, () =>
                {
                    dateTrigger.triggerDaysSinceStartOfYear = (int)quadrum * 15 + day;
                    AfterPreceptEdit(ritual);
                }));
            }
            TolkHelper.Speak(quadrum.Label());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void OpenRewardMenu(Precept_Ritual ritual)
        {
            var options = new List<FloatMenuOption>();

            string noneLabel = "None".Translate();
            if (ritual.attachableOutcomeEffect == null) noneLabel += ", current";
            options.Add(new FloatMenuOption(noneLabel, () => { ritual.attachableOutcomeEffect = null; AfterPreceptEdit(ritual); }));

            foreach (var eff in DefDatabase<RitualAttachableOutcomeEffectDef>.AllDefs)
            {
                var captured = eff;
                var report = eff.CanAttachToRitual(ritual);
                string label = eff.LabelCap;
                if (eff == ritual.attachableOutcomeEffect) label += ", current";
                if (!report.Accepted)
                {
                    label += " (" + report.Reason + ")";
                    options.Add(new FloatMenuOption(label, null));
                }
                else
                {
                    options.Add(new FloatMenuOption(label, () => { ritual.attachableOutcomeEffect = captured; AfterPreceptEdit(ritual); }));
                }
            }
            TolkHelper.Speak("RitualAttachedReward".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        #endregion

        #region Role apparel requirements

        private static void OpenApparelRequirementsMenu(Precept_Role role)
        {
            var current = role.ApparelRequirements ?? new List<PreceptApparelRequirement>();
            var options = new List<FloatMenuOption>();

            // Add — blocked entirely if a meme forbids role apparel requirements (matches vanilla).
            var preventingMeme = ideo.memes.FirstOrDefault(m => m.preventApparelRequirements);
            if (preventingMeme != null)
                options.Add(new FloatMenuOption(
                    "CannotNotAddRoleApparelDueToMeme".Translate(preventingMeme.LabelCap.Named("MEME")), null));
            else
                options.Add(new FloatMenuOption("Add".Translate().CapitalizeFirst() + "...",
                    () => OpenAddApparelRequirementMenu(role)));

            // Existing requirements — selecting one removes it.
            foreach (var req in current)
            {
                var captured = req;
                string apparel = string.Join(", ", captured.requirement.AllRequiredApparel().Select(a => a.LabelCap.ToString()));
                options.Add(new FloatMenuOption("Remove".Translate() + ": " + apparel, () =>
                {
                    var list = role.ApparelRequirements;
                    list.Remove(captured);
                    role.ApparelRequirements = list; // re-assign to clear the role's cached tip
                    AfterPreceptEdit(role);
                }));
            }

            TolkHelper.Speak("EditApparelRequirement".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void OpenAddApparelRequirementMenu(Precept_Role role)
        {
            var current = role.ApparelRequirements ?? new List<PreceptApparelRequirement>();
            var options = new List<FloatMenuOption>();

            foreach (var possible in Precept_Role.AllPossibleRequirements(ideo, role.def, desperate: true))
            {
                var captured = possible;
                var apparelList = possible.requirement.AllRequiredApparel().ToList();
                if (apparelList.Count == 0) continue;

                string label = string.Join(", ", apparelList.Select(a => a.LabelCap.ToString()));
                bool canAdd = possible.CanAddRequirement(role, current, out string reason);
                if (!canAdd && !string.IsNullOrEmpty(reason))
                    label += " (" + reason + ")";

                options.Add(new FloatMenuOption(label, canAdd ? (System.Action)(() =>
                {
                    var list = role.ApparelRequirements ?? new List<PreceptApparelRequirement>();
                    list.Add(captured);
                    role.ApparelRequirements = list;
                    AfterPreceptEdit(role);
                }) : null));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        #endregion

        /// <summary>
        /// Shared post-edit refresh, mirroring Dialog_EditPrecept.ApplyChanges: clear every
        /// precept's cached tip, regenerate the description, rebuild the tree (keeping the cursor on
        /// this precept), and announce the new value.
        /// </summary>
        private static void AfterPreceptEdit(Precept precept)
        {
            foreach (var p in ideo.PreceptsListForReading)
                p.ClearTipCache();
            ideo.anyPreceptEdited = true;
            ideo.RegenerateDescription();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            RebuildTree();
            TolkHelper.Speak(IdeoBuilderHelper.PreceptLabel(precept), SpeechPriority.High);
        }

        #endregion

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool ctrl = ev.control;
            bool alt = KeyboardHelper.IsAltHeld;

            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (treeNav.HasActiveSearch)
                {
                    treeNav.Typeahead.ClearSearchAndAnnounce();
                    treeNav.ReannounceCurrentItem();
                    return true;
                }
                Close();
                SoundDefOf.TabClose.PlayOneShotOnCamera();
                TolkHelper.Speak("CustomizeIdeoligion".Loc());
                return true;
            }

            // ] — edit-actions context menu for the focused precept (mirrors the hub's ] idiom).
            if (key == KeyCode.RightBracket && !alt && !ctrl)
            {
                OpenEditMenu();
                return true;
            }

            return treeNav.HandleInput(ev);
        }

        #endregion

        #region Announcement

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append(IdeoBuilderHelper.GetLocalizedSectionLabel(kind));

            var pred = CurrentPreceptPredicate(kind);
            int count = ideo.PreceptsListForReading.Count(pred);
            sb.Append(". ").Append(count);

            if (treeNav.Count > 0)
            {
                var first = treeNav.VisibleItems[0];
                sb.Append(". ").Append(first.Label);
            }

            TolkHelper.Speak(sb.ToString(), SpeechPriority.High);
        }

        #endregion
    }
}
