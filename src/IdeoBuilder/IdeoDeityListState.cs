using System;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using DeityType = RimWorld.IdeoFoundation_Deity.Deity;

namespace RimWorldAccess
{
    /// <summary>
    /// Windowless overlay for managing a deity-foundation ideoligion's deities. Opened from the
    /// builder hub's Deities section. Lists current deities (name, title, gender), with an
    /// "Add deity" node and a "Randomize deities" node. Enter on a deity opens a float menu of
    /// per-deity actions (edit name, edit title, set gender, regenerate, remove). Name/title use
    /// the modal TextInputController; gender uses the windowless float menu.
    /// </summary>
    public static class IdeoDeityListState
    {
        public static bool IsActive { get; private set; }

        private static Ideo ideo;
        private static IdeoFoundation_Deity foundation;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("IdeoDeityList");
        private static bool configured;
        private static readonly TextInputController controller = new TextInputController();

        private static readonly System.Reflection.MethodInfo GenerateNewDeityMethod =
            AccessTools.Method(typeof(IdeoFoundation_Deity), "GenerateNewDeity");
        private static readonly System.Reflection.MethodInfo FillDeityMethod =
            AccessTools.Method(typeof(IdeoFoundation_Deity), "FillDeity");

        public static void Open(Ideo targetIdeo)
        {
            if (targetIdeo?.foundation is IdeoFoundation_Deity f)
            {
                ideo = targetIdeo;
                foundation = f;
                IsActive = true;
                EnsureConfigured();
                RebuildTree();
                AnnounceOpening();
            }
        }

        public static void Close()
        {
            IsActive = false;
            ideo = null;
            foundation = null;
            treeNav.Reset();
        }

        private static void EnsureConfigured()
        {
            if (configured) return;
            configured = true;
            treeNav.AnnounceChildCounts = false;
            treeNav.FormatItemAnnouncement = FormatItem;
            treeNav.FormatStateChangeAnnouncement = i => (i.IsExpanded ? "Expanded" : "Collapsed") + ". " + i.Label;
            treeNav.FormatSearchAnnouncement = (i, t) => $"{i.Label}, {t.CurrentMatchPosition} of {t.MatchCount} matches for '{t.SearchBuffer}'";
            treeNav.OnActivate = HandleActivate;
            treeNav.OnDelete = HandleDelete;
        }

        public static void RebuildTree()
        {
            if (foundation == null) return;

            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category,
            };

            // Randomize-all and Add nodes.
            root.Children.Add(new InspectionTreeItem
            {
                Label = "RandomizeDeities".Translate().ToString(),
                IndentLevel = 0,
                Type = InspectionTreeItem.ItemType.Item,
                Data = "RANDOMIZE",
                Parent = root,
            });

            if (foundation.DeitiesListForReading.Count < ideo.DeityCountRange.max)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Label = "AddDeity".Translate().ToString(),
                    IndentLevel = 0,
                    Type = InspectionTreeItem.ItemType.Item,
                    Data = "ADD",
                    Parent = root,
                });
            }

            foreach (var deity in foundation.DeitiesListForReading)
            {
                var node = new InspectionTreeItem
                {
                    Label = BuildDeityLabel(deity),
                    IndentLevel = 0,
                    IsExpandable = deity.relatedMeme != null,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Data = deity,
                    Parent = root,
                };
                if (deity.relatedMeme != null)
                {
                    node.Children.Add(new InspectionTreeItem
                    {
                        Label = "RelatedToMeme".Translate() + ": " + deity.relatedMeme.LabelCap.Resolve(),
                        IndentLevel = 1,
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Data = deity.relatedMeme,
                        LinkedDef = deity.relatedMeme,
                        Parent = node,
                    });
                }
                root.Children.Add(node);
            }

            treeNav.Initialize(root);
        }

        private static string BuildDeityLabel(DeityType deity)
        {
            var sb = new StringBuilder();
            sb.Append(deity.name);
            if (!string.IsNullOrEmpty(deity.type))
                sb.Append(", ").Append(deity.type);
            sb.Append(", ").Append(deity.gender.GetLabel().CapitalizeFirst());
            return sb.ToString();
        }

        #region Activation / delete

        private static bool HandleActivate(InspectionTreeItem item)
        {
            if (item?.Data is string s)
            {
                if (s == "ADD") { AddDeity(); return true; }
                if (s == "RANDOMIZE") { RandomizeAll(); return true; }
            }
            if (item?.Data is DeityType deity)
            {
                OpenDeityActions(deity);
                return true;
            }
            return false;
        }

        private static bool HandleDelete(InspectionTreeItem item)
        {
            if (item?.Data is DeityType deity)
            {
                RemoveDeity(deity);
                return true;
            }
            return false;
        }

        #endregion

        #region Operations

        private static void AddDeity()
        {
            if (foundation.DeitiesListForReading.Count >= ideo.DeityCountRange.max)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            var newDeity = (DeityType)GenerateNewDeityMethod.Invoke(foundation, null);
            foundation.DeitiesListForReading.Add(newDeity);
            ideo.RegenerateAllPreceptNames();
            ideo.RegenerateDescription();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            RebuildAndAnnounce();
        }

        private static void RemoveDeity(DeityType deity)
        {
            int min = ideo.DeityCountRange.min;
            if (foundation.DeitiesListForReading.Count <= min)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                string noun = (min <= 1) ? "Deity".Translate().ToString() : Find.ActiveLanguageWorker.Pluralize("Deity".Translate(), min);
                TolkHelper.Speak("DeitiesRequired".Loc(min, noun.Named("DEITYNOUN")), SpeechPriority.High);
                return;
            }
            foundation.DeitiesListForReading.Remove(deity);
            ideo.RegenerateDescription();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            TolkHelper.SpeakData($"{deity.name}, removed");
            RebuildTree();
        }

        private static void RandomizeAll()
        {
            foundation.GenerateDeities();
            ideo.RegenerateAllPreceptNames();
            ideo.RegenerateDescription();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            RebuildAndAnnounce();
        }

        private static void OpenDeityActions(DeityType deity)
        {
            var options = new System.Collections.Generic.List<FloatMenuOption>
            {
                new FloatMenuOption("DeityName".Translate() + "...", () => EditDeityName(deity)),
                new FloatMenuOption("DeityTitle".Translate() + "...", () => EditDeityTitle(deity)),
                new FloatMenuOption("DeityGender".Translate() + "...", () => OpenGenderPicker(deity)),
                new FloatMenuOption("Regenerate".Translate().CapitalizeFirst(), () =>
                {
                    FillDeityMethod.Invoke(foundation, new object[] { deity });
                    ideo.RegenerateDescription();
                    NotifyReturnedFromPicker();
                }),
            };

            if (foundation.DeitiesListForReading.Count > ideo.DeityCountRange.min)
                options.Add(new FloatMenuOption("Remove".Translate().CapitalizeFirst(), () => RemoveDeity(deity)));

            TolkHelper.SpeakData(deity.name);
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void EditDeityName(DeityType deity)
        {
            controller.Begin(deity.name, TextFieldSpec.Unrestricted("DeityName"),
                text =>
                {
                    deity.name = text.Trim();
                    ideo.RegenerateAllPreceptNames();
                    ideo.RegenerateDescription();
                    RebuildAndAnnounce();
                });
        }

        private static void EditDeityTitle(DeityType deity)
        {
            controller.Begin(deity.type, TextFieldSpec.Unrestricted("DeityTitle"),
                text =>
                {
                    deity.type = text.Trim();
                    ideo.RegenerateDescription();
                    RebuildAndAnnounce();
                });
        }

        private static void OpenGenderPicker(DeityType deity)
        {
            var options = new System.Collections.Generic.List<FloatMenuOption>();
            foreach (Gender g in (Gender[])Enum.GetValues(typeof(Gender)))
            {
                var captured = g;
                options.Add(new FloatMenuOption(g.GetLabel().CapitalizeFirst(), () =>
                {
                    deity.gender = captured;
                    ideo.RegenerateDescription();
                    NotifyReturnedFromPicker();
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        #endregion

        #region Refresh / input

        /// <summary>Refresh the tree after returning from a float-menu action that mutated deities.</summary>
        public static void NotifyReturnedFromPicker()
        {
            if (!IsActive) return;
            RebuildAndAnnounce();
        }

        private static void RebuildAndAnnounce()
        {
            RebuildTree();
            treeNav.ReannounceCurrentItem();
        }

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;

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

            return treeNav.HandleInput(ev);
        }

        #endregion

        private static string FormatItem(InspectionTreeItem item)
        {
            var sb = new StringBuilder();
            sb.Append(item.Label);
            if (item.IsExpandable)
                sb.Append(item.IsExpanded ? ", expanded" : ", collapsed");
            var (pos, total) = treeNav.GetSiblingPosition(item);
            string position = MenuHelper.FormatPosition(pos - 1, total);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);
            return sb.ToString();
        }

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append("Deities".Translate());
            sb.Append(". ").Append(foundation.DeitiesListForReading.Count);
            if (treeNav.Count > 0)
                sb.Append(". ").Append(treeNav.VisibleItems[0].Label);
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
        }
    }
}
