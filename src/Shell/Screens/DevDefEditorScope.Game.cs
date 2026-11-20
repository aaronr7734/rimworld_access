using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for RimWorld's dev-mode def editor
    /// (<see cref="LudeonTK.EditWindow_DefEditor"/>, opened only by the "Edit effecter..." and
    /// "Edit Animation..." debug actions), registered through <see cref="ScopeForWindow"/>.
    /// The window renders a genuine expand/collapse tree (<c>Listing_TreeDefs</c> over
    /// <see cref="TreeNode_Editor"/>), so this is a <see cref="TreeRegionScope"/>: every vanilla node
    /// is wrapped into an <see cref="InspectionTreeItem"/> row (label = <c>LabelText</c> plus
    /// <c>ExtraInfoText</c>; terminal rows append the rendered value and a role word). Children wrap
    /// lazily on first expand (<see cref="OnBeforeExpandNode"/>), so a large def tree is never walked
    /// whole up front.
    /// Every value write rides <see cref="TreeNode_Editor.Value"/>'s setter (vehicle A). "Delete" is
    /// <see cref="TreeNode_Editor.Delete"/> plus <see cref="TreeNode_Editor.CheckLatentDelete"/> for
    /// list items; "New"/"Add" reproduce <c>MakeCreateNewObjectMenu</c>'s create-and-attach and are
    /// marked MUTATION-C, since those are inline draw-pass delegates.
    /// Attachment is unconditional — no <c>Arming</c> flag: unlike its EditWindow siblings this window
    /// never auto-opens, so every open is invited.
    /// Escape is this scope's: <c>closeOnCancel</c> is false, so vanilla never closes the window and
    /// <see cref="OwnsCancel"/> is true; the base's typeahead-gated Cancel claim (registered first)
    /// clears an active search before this scope's claim closes. <c>closeOnAccept</c> is false too.
    /// Text sessions outrank typeahead, so typing digits into an int field can never start a search:
    /// while <see cref="TextFieldEditSession"/> holds a modal controller, the dispatcher routes every
    /// key to it ahead of the char-sink call that feeds this scope.
    /// </summary>
    internal sealed class DevDefEditorScope : TreeRegionScope
    {
        // Digits-only (optionally signed) for int, plus one decimal point for float. minLength 0
        // everywhere so the buffer may be cleared mid-edit and an empty string is a legal value.
        private static readonly TextFieldSpec IntSpec = new TextFieldSpec(
            labelKey: null, minLength: 0, allowedChars: new Regex("^-?[0-9]*$"));
        private static readonly TextFieldSpec FloatSpec = new TextFieldSpec(
            labelKey: null, minLength: 0, allowedChars: new Regex(@"^-?[0-9]*\.?[0-9]*$"));
        private static readonly TextFieldSpec StringSpec = new TextFieldSpec(
            labelKey: null, minLength: 0);

        private enum Terminal { Bool, Int, Float, String, Enum, FloatRange, IntRange, Uneditable }

        /// <summary>
        /// <c>EditWindow_DefEditor</c> is internal to Assembly-CSharp, so the window is held as its
        /// <see cref="Window"/> base and its <c>def</c> field read reflectively.
        /// </summary>
        internal static readonly Type WindowType = AccessTools.TypeByName("LudeonTK.EditWindow_DefEditor");
        private static readonly FieldInfo DefField = AccessTools.Field(WindowType, "def");

        private readonly Window window;
        private readonly Def def;
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        private bool announcedOpen;

        private static readonly Dictionary<FieldInfo, bool> sliderFields = new Dictionary<FieldInfo, bool>();

        public DevDefEditorScope(Window window)
        {
            this.window = window;
            this.def = DefField.GetValue(window) as Def;

            // The wrapper root carries the def's own name and is presented as the region, not a row.
            Tree.SkipRoot = true;

            // Space opens the structural context menu (New / Add / Delete) for the current row.
            Claim("devDefEditor.actions", e => OpenActionsMenu());

            Claim(SharedMenuGrammar.Cancel, e => PerformCancel(), when: () => !TypeaheadHasActiveSearch);

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name => "dev-def-editor";

        /// <summary>closeOnCancel is false on this window, so vanilla never closes it — this scope owns Escape.</summary>
        public override bool OwnsCancel => true;

        /// <summary>
        /// Listing_TreeDefs draws through ButtonIcon controls, not Widgets.ButtonText, so there is
        /// nothing for the Buttons region to scrape and no dialog footer to present.
        /// </summary>
        protected override bool IncludeActionsRegion => false;

        /// <summary>The def under edit names the region, matching the open announcement.</summary>
        protected override string TreeRegionLabel =>
            def != null
                ? def.ToString()
                : "RimWorldAccess.Dev.DefEditor.RegionName".Translate().ToString();

        public override void OnPush()
        {
            base.OnPush();
            BuildTree();
            DefEditorRowCapture.SliderScope = this;
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(DefEditorRowCapture.SliderScope, this))
            {
                DefEditorRowCapture.SliderScope = null;
            }
        }

        /// <summary>The focused row's node, kept while a text session's shadow scope sits on top.</summary>
        internal TreeNode_Editor FocusedValueNode
        {
            get
            {
                FocusScope top = FocusStack.Top;
                return ReferenceEquals(top, this) || top is TextSessionScope
                    ? CurrentTreeItem()?.Data as TreeNode_Editor
                    : null;
            }
        }

        /// <summary>A float row's <c>[EditSliderRange]</c> slider (Listing_TreeDefs.cs:184-188); vanilla's int rows draw a text field only.</summary>
        protected override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            value = 0f;
            high = false;
            var node = TreeItemAt(index)?.Data as TreeNode_Editor;
            if (node == null || !DefEditorRowCapture.DrewRecently(node)
                || node.nodeType != EditTreeNodeType.TerminalValue || node.ObjectType != typeof(float)
                || !HasSliderRange(node))
            {
                return false;
            }
            value = (float)node.Value;
            return true;
        }

        /// <summary>Every float slider row already edits its value as text on Enter.</summary>
        protected override bool HasOwnNumberEntry(int region, int index)
        {
            return true;
        }

        private static bool HasSliderRange(TreeNode_Editor node)
        {
            FieldInfo field = node.owningField;
            if (field == null)
            {
                return false;
            }
            bool has;
            if (!sliderFields.TryGetValue(field, out has))
            {
                has = TryGetSliderRange(node, out _, out _);
                sliderFields[field] = has;
            }
            return has;
        }

        public override void OnFocus()
        {
            base.OnFocus();

            // Only the first focus announces: a later one returns from a picker that already
            // re-announced the row, or was cancelled silently.
            if (!announcedOpen)
            {
                announcedOpen = true;
                TolkHelper.SpeakData("RimWorldAccess.Dev.DefEditor.Opened".Translate(
                    def != null ? def.ToString() : "").ToString());
                AnnounceCurrentItem();
            }
        }

        // Tree construction: wrap vanilla TreeNode_Editor nodes into rows.

        private void BuildTree()
        {
            // The exact root vanilla renders; RootOf caches, so this is the same node instance and
            // expansion is independent of vanilla's own.
            TreeNode_Editor rootNode = EditTreeNodeDatabase.RootOf(def);
            var root = new InspectionTreeItem
            {
                Label = def != null ? def.ToString() : "",
                Data = rootNode,
                IsExpandable = true,
                IndentLevel = 0,
            };
            BuildChildren(root);
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>Lazy child-wrap hook: build a node's rows the first time it is expanded.</summary>
        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            // A genuinely-expandable node always has at least one child once built (empty lists and
            // null objects report Openable false), so no children reliably means "not yet wrapped".
            if (item.Children.Count == 0)
            {
                BuildChildren(item);
            }
        }

        /// <summary>
        /// (Re)builds one wrapper node's children from its vanilla node: config-error rows first
        /// (mirroring <c>DoSpecialPreElements</c>), then the custom-editor-widgets note, then the
        /// real field/element rows.
        /// </summary>
        private void BuildChildren(InspectionTreeItem item)
        {
            item.Children.Clear();
            var node = item.Data as TreeNode_Editor;
            if (node == null)
            {
                return;
            }

            foreach (string error in GetConfigErrors(node))
            {
                AddReadOnlyChild(item, error);
            }
            if (HasCustomEditWidgets(node))
            {
                AddReadOnlyChild(item,
                    "RimWorldAccess.Dev.DefEditor.CustomWidgets".Translate().ToString());
            }

            if (node.children != null)
            {
                foreach (TreeNode child in node.children)
                {
                    var childNode = child as TreeNode_Editor;
                    if (childNode == null)
                    {
                        continue;
                    }
                    var row = new InspectionTreeItem
                    {
                        Label = ComputeLabel(childNode),
                        Data = childNode,
                        IsExpandable = IsExpandable(childNode),
                        IndentLevel = item.IndentLevel + 1,
                        Parent = item,
                    };
                    item.Children.Add(row);
                }
            }
        }

        private void AddReadOnlyChild(InspectionTreeItem parent, string label)
        {
            parent.Children.Add(new InspectionTreeItem
            {
                Label = label,
                Type = InspectionTreeItem.ItemType.DetailText,
                IsExpandable = false,
                IndentLevel = parent.IndentLevel + 1,
                Parent = parent,
            });
        }

        private bool IsExpandable(TreeNode_Editor node)
        {
            return node.Openable || HasExtras(node);
        }

        private bool HasExtras(TreeNode_Editor node)
        {
            return GetConfigErrors(node).Count > 0 || HasCustomEditWidgets(node);
        }

        private static List<string> GetConfigErrors(TreeNode_Editor node)
        {
            if (node.obj is Editable editable)
            {
                return editable.ConfigErrors().ToList();
            }
            return new List<string>();
        }

        private static bool HasCustomEditWidgets(TreeNode_Editor node)
        {
            return node.obj != null && node.obj.GetType().GetMethod("DoEditWidgets") != null;
        }

        // Row labels.

        private string ComputeLabel(TreeNode_Editor node)
        {
            if (node.nodeType == EditTreeNodeType.TerminalValue)
            {
                return node.LabelText + ", " + RenderValue(node) + ", " + RoleWord(TerminalOf(node.ObjectType));
            }

            string label = node.LabelText;
            string extra = node.ExtraInfoText;
            if (!string.IsNullOrEmpty(extra))
            {
                label += ", " + extra;
            }
            int errors = GetConfigErrors(node).Count;
            if (errors > 0)
            {
                label += ", " + "RimWorldAccess.Dev.DefEditor.ConfigErrors".Translate(errors);
            }
            return label;
        }

        private static string RenderValue(TreeNode_Editor node)
        {
            object value = node.Value;
            if (value == null)
            {
                return "RimWorldAccess.Dev.DefEditor.NullValue".Translate().ToString();
            }
            return value.ToString();
        }

        private static string RoleWord(Terminal terminal)
        {
            switch (terminal)
            {
                case Terminal.Bool:
                    return "RimWorldAccess.Dev.DefEditor.RoleCheckbox".Translate().ToString();
                case Terminal.Enum:
                    return "RimWorldAccess.Dev.DefEditor.RoleComboBox".Translate().ToString();
                case Terminal.Uneditable:
                    return "RimWorldAccess.Dev.DefEditor.RoleReadOnly".Translate().ToString();
                default:
                    return "RimWorldAccess.Dev.DefEditor.RoleTextField".Translate().ToString();
            }
        }

        private static Terminal TerminalOf(Type type)
        {
            if (type == typeof(bool)) return Terminal.Bool;
            if (type == typeof(int)) return Terminal.Int;
            if (type == typeof(float)) return Terminal.Float;
            if (type == typeof(string)) return Terminal.String;
            if (type.IsEnum) return Terminal.Enum;
            if (type == typeof(FloatRange)) return Terminal.FloatRange;
            if (type == typeof(IntRange)) return Terminal.IntRange;
            return Terminal.Uneditable;
        }

        // Announcements.

        /// <summary>
        /// The row's composed label plus the bare expansion state. Child counts are deliberately
        /// absent: they mislead while children wrap lazily (an unexpanded node reports zero), and a
        /// list's label already carries its own "(N elements)" tail.
        /// </summary>
        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Label = item.Label.TrimEnd('.', '!', '?');
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            return d;
        }

        // Left/Right: adjust an adjustable terminal, else expand/collapse.

        /// <summary>
        /// Right/Left adjust in place on an adjustable terminal (a bool, or a numeric field carrying
        /// <c>[EditSliderRange]</c>); everywhere else they fall through to tree expand/collapse.
        /// </summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            InspectionTreeItem item = TreeItemAt(index);
            var node = item?.Data as TreeNode_Editor;
            if (node != null && node.nodeType == EditTreeNodeType.TerminalValue)
            {
                Type type = node.ObjectType;
                if (type == typeof(bool))
                {
                    ToggleBool(node, item);
                    return;
                }
                if ((type == typeof(int) || type == typeof(float)) && TryGetSliderRange(node, out float min, out float max))
                {
                    StepSlider(node, item, direction, min, max);
                    return;
                }
            }
            base.AdjustContentItem(region, index, direction);
        }

        private InspectionTreeItem TreeItemAt(int index)
        {
            int treeIndex = index - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return null;
            }
            return Tree.Visible[treeIndex];
        }

        private static bool TryGetSliderRange(TreeNode_Editor node, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (node.owningField == null)
            {
                return false;
            }
            var attrs = (EditSliderRangeAttribute[])node.owningField.GetCustomAttributes(
                typeof(EditSliderRangeAttribute), inherit: true);
            if (attrs.Length == 0)
            {
                return false;
            }
            min = attrs[0].min;
            max = attrs[0].max;
            return true;
        }

        private void StepSlider(TreeNode_Editor node, InspectionTreeItem item, int direction, float min, float max)
        {
            bool isFloat = node.ObjectType == typeof(float);
            float current = isFloat ? (float)node.Value : (int)node.Value;
            float stepped = SliderStep.Stepped(current, direction, min, max, isFloat ? -1f : 1f);
            // Vehicle A: the property setter is vanilla's own write path (owningField.SetValue).
            node.Value = isFloat ? (object)stepped : (int)Math.Round(stepped);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshRow(item, node);
            ReannounceRow();
        }

        private void ToggleBool(TreeNode_Editor node, InspectionTreeItem item)
        {
            // Vehicle A: the property setter is vanilla's own write path.
            node.Value = !(bool)node.Value;
            RefreshRow(item, node);
            ReannounceRow();
        }

        // Enter: edit a terminal (expandables toggle their branch state).

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var node = item?.Data as TreeNode_Editor;
            if (node != null && node.nodeType == EditTreeNodeType.TerminalValue)
            {
                BeginTerminalEdit(node, item);
                return;
            }
            // Not a value row: Enter toggles an expandable (the one place Enter also collapses) and
            // leaves a plain read-only row silent.
            if (item != null && item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
            }
        }

        private void BeginTerminalEdit(TreeNode_Editor node, InspectionTreeItem item)
        {
            switch (TerminalOf(node.ObjectType))
            {
                case Terminal.Bool:
                    ToggleBool(node, item);
                    return;
                case Terminal.String:
                    BeginTextEdit(node, item, StringSpec, parseAndApply: s => node.Value = s);
                    return;
                case Terminal.Int:
                    BeginTextEdit(node, item, IntSpec, parseAndApply: s =>
                    {
                        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                        {
                            node.Value = v;
                        }
                    });
                    return;
                case Terminal.Float:
                    BeginTextEdit(node, item, FloatSpec, parseAndApply: s =>
                    {
                        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                        {
                            node.Value = v;
                        }
                    });
                    return;
                case Terminal.Enum:
                    OpenEnumPicker(node, item);
                    return;
                case Terminal.FloatRange:
                case Terminal.IntRange:
                    BeginRangeEdit(node, item);
                    return;
                default:
                    // Uneditable value type: read-only, re-read the row.
                    ReannounceRow();
                    return;
            }
        }

        private void BeginTextEdit(TreeNode_Editor node, InspectionTreeItem item, TextFieldSpec spec, Action<string> parseAndApply)
        {
            editSession.EnterEdit(
                RenderValue(node),
                spec,
                node.LabelText,
                apply: parseAndApply,
                onExit: delegate { RefreshRow(item, node); ReannounceRow(); },
                announcePrompt: true);
        }

        private void OpenEnumPicker(TreeNode_Editor node, InspectionTreeItem item)
        {
            var options = new List<FloatMenuOption>();
            foreach (object value in Enum.GetValues(node.ObjectType))
            {
                object localValue = value;
                options.Add(new FloatMenuOption(value.ToString(), delegate
                {
                    // Vehicle A: the property setter is vanilla's own write path.
                    node.Value = localValue;
                    RefreshRow(item, node);
                    ReannounceRow();
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false,
                titleText: node.LabelText);
        }

        // -------------------------------------------------------------------
        // FloatRange / IntRange: a two-step numeric edit writing a new struct. Each step commits on
        // Enter and cancels on Escape, so an Escape leaves that bound untouched; confirming the
        // minimum chains straight into the maximum step.
        // -------------------------------------------------------------------

        private void BeginRangeEdit(TreeNode_Editor node, InspectionTreeItem item)
        {
            bool isFloat = node.ObjectType == typeof(FloatRange);
            TextFieldSpec spec = isFloat ? FloatSpec : IntSpec;

            editSession.EnterEdit(
                RangeMin(node).ToString(CultureInfo.InvariantCulture),
                spec,
                node.LabelText + " " + "RimWorldAccess.Dev.DefEditor.RangeMin".Translate(),
                apply: s => ApplyRangeBound(node, item, s, isMin: true),
                onExit: delegate { RefreshRow(item, node); ReannounceRow(); },
                announcePrompt: true,
                onConfirm: delegate
                {
                    editSession.EnterEdit(
                        RangeMax(node).ToString(CultureInfo.InvariantCulture),
                        spec,
                        node.LabelText + " " + "RimWorldAccess.Dev.DefEditor.RangeMax".Translate(),
                        apply: s => ApplyRangeBound(node, item, s, isMin: false),
                        onExit: delegate { RefreshRow(item, node); ReannounceRow(); },
                        announcePrompt: true);
                });
        }

        private void ApplyRangeBound(TreeNode_Editor node, InspectionTreeItem item, string text, bool isMin)
        {
            if (node.ObjectType == typeof(FloatRange))
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    return;
                }
                var range = (FloatRange)node.Value;
                // Vehicle A: the property setter is vanilla's own write path.
                node.Value = isMin ? new FloatRange(v, range.max) : new FloatRange(range.min, v);
            }
            else
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                {
                    return;
                }
                var range = (IntRange)node.Value;
                // Vehicle A: the property setter is vanilla's own write path.
                node.Value = isMin ? new IntRange(v, range.max) : new IntRange(range.min, v);
            }
        }

        private static float RangeMin(TreeNode_Editor node)
        {
            return node.ObjectType == typeof(FloatRange) ? ((FloatRange)node.Value).min : ((IntRange)node.Value).min;
        }

        private static float RangeMax(TreeNode_Editor node)
        {
            return node.ObjectType == typeof(FloatRange) ? ((FloatRange)node.Value).max : ((IntRange)node.Value).max;
        }

        // Space: New / Add / Delete for the current row (vanilla's own gates).

        private void OpenActionsMenu()
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            var node = item?.Data as TreeNode_Editor;
            if (node == null)
            {
                TolkHelper.SpeakData("RimWorldAccess.Dev.DefEditor.NothingAvailable".Translate().ToString());
                return;
            }

            var options = new List<FloatMenuOption>();
            if (node.HasNewButton)
            {
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Dev.DefEditor.ActionNew".Translate().ToString(),
                    delegate { OpenCreateMenu(node, item, isList: false); }));
            }
            if (node.nodeType == EditTreeNodeType.ListRoot)
            {
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Dev.DefEditor.ActionAdd".Translate().ToString(),
                    delegate { OpenCreateMenu(node, item, isList: true); }));
            }
            if (node.HasDeleteButton)
            {
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Dev.DefEditor.ActionDelete".Translate().ToString(),
                    delegate { DeleteNode(node, item); }));
            }

            if (options.Count == 0)
            {
                TolkHelper.SpeakData("RimWorldAccess.Dev.DefEditor.NothingAvailable".Translate().ToString());
                return;
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false,
                titleText: node.LabelText);
        }

        private void OpenCreateMenu(TreeNode_Editor node, InspectionTreeItem item, bool isList)
        {
            Type baseType = isList
                ? node.obj.GetType().GetGenericArguments()[0]
                : node.owningField.FieldType;
            var options = new List<FloatMenuOption>();
            // SafeTypeSweep, not GenTypes' own InstantiableDescendantsAndSelf: baseType is an
            // arbitrary def field type, so its descendants can span any mod assembly, including one
            // with an unresolvable dependency on this platform.
            foreach (Type type in SafeTypeSweep.InstantiableDescendantsAndSelfSafe(baseType,
                (_, ex) => Log.Warning(
                    $"[RimWorld Access] dev def editor: skipping a type that failed to resolve "
                    + $"while listing creatable types for {baseType}: {ex.GetType().Name}")))
            {
                Type creatingType = type;
                options.Add(new FloatMenuOption(type.ToString(), delegate
                {
                    CreateAndAttach(node, item, creatingType, isList);
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        private void CreateAndAttach(TreeNode_Editor node, InspectionTreeItem item, Type creatingType, bool isList)
        {
            object created = creatingType == typeof(string) ? "" : Activator.CreateInstance(creatingType);
            if (isList)
            {
                // MUTATION-C: mirrors Listing_TreeDefs.MakeCreateNewObjectMenu's
                // list-add delegate (node.obj's own Add(o)); it is an inline
                // draw-pass delegate on the "Add" ButtonIcon, no invocable vanilla
                // method exists to ride.
                node.obj.GetType().GetMethod("Add").Invoke(node.obj, new object[] { created });
                node.RebuildChildNodes();
                RebuildAndReveal(item, node, item);
            }
            else
            {
                // MUTATION-C: mirrors Listing_TreeDefs.MakeCreateNewObjectMenu's
                // "New" delegate (owningField.SetValue(ParentObj, o) then the
                // parent's RebuildChildNodes); inline draw-pass delegate on the
                // "New" ButtonIcon, no invocable vanilla method.
                node.owningField.SetValue(node.ParentObj, created);
                var parent = (TreeNode_Editor)node.parentNode;
                parent.RebuildChildNodes();
                // The field node was replaced by RebuildChildNodes, so re-wrap the PARENT's children.
                RebuildAndRevealParent(item, parent);
            }
        }

        private void DeleteNode(TreeNode_Editor node, InspectionTreeItem item)
        {
            var parentNode = (TreeNode_Editor)node.parentNode;
            InspectionTreeItem parentItem = item.Parent;

            // Vehicle A: TreeNode_Editor.Delete is vanilla's own removal. For a list item it defers
            // to the parent's latent delete, which vanilla flushes on its next render pass — flush it
            // now so the wrapper rebuild sees the shortened list.
            node.Delete();
            if (node.IsListItem)
            {
                parentNode.CheckLatentDelete();
            }
            else
            {
                parentNode.RebuildChildNodes();
            }
            RebuildAndReveal(parentItem, parentNode, parentItem);
        }

        // Wrapper upkeep after edits and structural changes.

        private void RefreshRow(InspectionTreeItem item, TreeNode_Editor node)
        {
            item.Label = ComputeLabel(node);
        }

        /// <summary>Re-read the row under the cursor after an in-place value write.</summary>
        private void ReannounceRow()
        {
            RefreshModel();
            AnnounceCurrentItem();
        }

        /// <summary>Re-read the row after the TREE cursor moved, bringing the region cursor with it.</summary>
        private void SyncAndAnnounce()
        {
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Re-wraps <paramref name="parentItem"/>'s children from <paramref name="parentNode"/>
        /// (freshly rebuilt by the caller), keeps it expanded, re-flattens, lands the cursor on
        /// <paramref name="landing"/> or its nearest surviving ancestor, then re-announces.
        /// </summary>
        private void RebuildAndReveal(InspectionTreeItem parentItem, TreeNode_Editor parentNode, InspectionTreeItem landing)
        {
            parentItem.Data = parentNode;
            // The structural change alters the parent's own "(N elements)" tail, which submenu mode
            // speaks as the children's context prefix. The wrapper root keeps its def-name label.
            if (parentItem.Parent != null)
            {
                RefreshRow(parentItem, parentNode);
            }
            BuildChildren(parentItem);
            parentItem.IsExpandable = IsExpandable(parentNode);
            parentItem.IsExpanded = parentItem.IsExpandable;
            Tree.Reflatten();

            InspectionTreeItem target = landing;
            while (target != null && !TryRevealAndSelect(target))
            {
                target = target.Parent;
            }
            SyncAndAnnounce();
        }

        /// <summary>
        /// The "New" companion: the edited node was replaced by its parent's rebuild, so re-wrap the
        /// parent and land on the field row now carrying the created value (matched by owning field).
        /// </summary>
        private void RebuildAndRevealParent(InspectionTreeItem fieldItem, TreeNode_Editor parentNode)
        {
            InspectionTreeItem parentItem = fieldItem.Parent;
            if (parentItem == null)
            {
                return;
            }
            var originalField = (fieldItem.Data as TreeNode_Editor)?.owningField;
            parentItem.Data = parentNode;
            if (parentItem.Parent != null)
            {
                RefreshRow(parentItem, parentNode);
            }
            BuildChildren(parentItem);
            parentItem.IsExpanded = true;
            Tree.Reflatten();

            InspectionTreeItem landing = parentItem.Children.FirstOrDefault(
                c => (c.Data as TreeNode_Editor)?.owningField == originalField) ?? parentItem;
            InspectionTreeItem target = landing;
            while (target != null && !TryRevealAndSelect(target))
            {
                target = target.Parent;
            }
            SyncAndAnnounce();
        }

        // The focus ring.

        internal bool Owns(Window w)
        {
            return ReferenceEquals(window, w);
        }

        /// <summary>
        /// The focused row's band among the nodes <see cref="DefEditorRowCapture"/> saw vanilla draw
        /// this pass, by reference; a row vanilla did not draw gets no ring.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            var node = CurrentTreeItem()?.Data as TreeNode_Editor;
            return node == null ? default(Rect) : DefEditorRowCapture.Rows.FindFirst(node);
        }

        // Escape.

        /// <summary>
        /// Escape with no search active (the base's typeahead-gated claim wins while one is):
        /// closeOnCancel is false on this window, so close it explicitly.
        /// </summary>
        private void PerformCancel()
        {
            ShellFrameStamps.MarkCancelConsumed();
            TolkHelper.SpeakData("RimWorldAccess.Dev.DefEditor.Closed".Translate().ToString());
            window.Close();
        }
    }
}
