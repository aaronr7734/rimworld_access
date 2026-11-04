using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class ClCompat
    {
        /// <summary>
        /// The private state of the three small dialogs: the tag picker (<c>Dialog_TagSelector</c>),
        /// the per-tag state popup (<c>Dialog_SetTagLoadoutState</c>) and the state editor
        /// (<c>Dialog_LoadoutStateEditor</c>). Their text boxes are raw <c>GUI.TextField</c>s the shell
        /// cannot see, so the scopes edit the backing strings directly, as the field itself would.
        /// </summary>
        internal static class Dialogs
        {
            private static readonly FieldInfo selectorTagsField;
            private static readonly FieldInfo selectorSelectedField;
            private static readonly FieldInfo selectorSearchField;
            private static readonly FieldInfo selectorMultiSelectField;
            private static readonly FieldInfo selectorTitleSuffixField;
            private static readonly MethodInfo selectorCloseAndSelect;
            private static readonly MethodInfo selectorDrawSearchBar;

            private static readonly FieldInfo popupElementField;
            private static readonly MethodInfo popupGetElems;
            private static readonly MethodInfo popupUpdateBaseColor;

            private static readonly FieldInfo editorNameField;
            private static readonly FieldInfo editorStatesField;
            private static readonly MethodInfo editorPanicGenerator;
            private static readonly MethodInfo editorDoWindowContents;
            private static readonly MethodInfo pairFirstGetter;
            private static readonly MethodInfo pairSetSecond;

            private static readonly bool ready;

            static Dialogs()
            {
                var surface = new ReflectionSurface("ClCompat.Dialogs");
                surface.Supplied("Inventory.Dialog_TagSelector", tagSelectorType);
                surface.Supplied("Inventory.Dialog_SetTagLoadoutState", setTagLoadoutStateType);
                surface.Supplied("Inventory.Dialog_LoadoutStateEditor", loadoutStateEditorType);

                selectorTagsField = surface.Field(tagSelectorType, "tags");
                selectorSelectedField = surface.Field(tagSelectorType, "selectedTags");
                selectorSearchField = surface.Field(tagSelectorType, "searchString");
                selectorMultiSelectField = surface.Field(tagSelectorType, "multiSelect");
                selectorTitleSuffixField = surface.Field(tagSelectorType, "customTitleSuffix");
                selectorDrawSearchBar = surface.Method(tagSelectorType, "DrawSearchBar", new[] { typeof(UnityEngine.Rect) });

                popupElementField = surface.Field(setTagLoadoutStateType, "element");
                popupUpdateBaseColor = surface.Method(setTagLoadoutStateType, "UpdateBaseColor", Type.EmptyTypes);

                editorNameField = surface.Field(loadoutStateEditorType, "loadoutStateString");
                editorStatesField = surface.Field(loadoutStateEditorType, "states");
                editorDoWindowContents = surface.Method(loadoutStateEditorType, "DoWindowContents", new[] { typeof(UnityEngine.Rect) });

                if (tagType != null && elementType != null && stateType != null)
                {
                    selectorCloseAndSelect = surface.Method(tagSelectorType, "CloseAndSelect", new[] { tagType });
                    popupGetElems = surface.Method(setTagLoadoutStateType, "GetElems", new[] { elementType });
                    editorPanicGenerator = surface.Method(loadoutStateEditorType, "GenerateMenuFor", new[] { stateType });
                }

                Type pairType = editorStatesField?.FieldType.GetGenericArguments()[0];
                if (pairType != null)
                {
                    pairFirstGetter = surface.Property(pairType, "First")?.GetGetMethod(true);
                    pairSetSecond = surface.Method(pairType, "SetSecond", new[] { typeof(bool) });
                }

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The private, version-pinned method the state popup's patch targets.</summary>
            public static MethodInfo PopupUpdateBaseColor
            {
                get { return popupUpdateBaseColor; }
            }

            /// <summary>Where the picker's search box draws, the point to post a live edit buffer before.</summary>
            public static MethodInfo SelectorDrawSearchBar
            {
                get { return selectorDrawSearchBar; }
            }

            /// <summary>The state editor's draw, which both its name boxes run inside.</summary>
            public static MethodInfo EditorDoWindowContents
            {
                get { return editorDoWindowContents; }
            }

            // ---- Dialog_TagSelector ----

            public static bool SelectorMultiSelect(Window dialog)
            {
                return Guarded.FieldOf(selectorMultiSelectField, dialog, "ClCompat.Dialogs.SelectorMultiSelect", false);
            }

            /// <summary>The suffix the caller appended to the title ("to copy"), or null.</summary>
            public static string SelectorTitleSuffix(Window dialog)
            {
                return Guarded.FieldOf<string>(selectorTitleSuffixField, dialog, "ClCompat.Dialogs.SelectorTitleSuffix");
            }

            public static string SelectorSearch(Window dialog)
            {
                return Guarded.FieldOf(selectorSearchField, dialog, "ClCompat.Dialogs.SelectorSearch", "");
            }

            public static void SetSelectorSearch(Window dialog, string value)
            {
                if (ready && dialog != null)
                {
                    selectorSearchField.SetValue(dialog, value ?? ""); // MUTATION-C: mirrors Dialog_TagSelector.cs:66 (GUI.TextField writes the field each draw)
                }
            }

            /// <summary>The tags the list draws: the dialog's own name-sorted list, filtered by DrawTagList's search test.</summary>
            public static List<object> SelectorVisibleTags(Window dialog)
            {
                var result = new List<object>();
                string search = SelectorSearch(dialog).ToLowerInvariant();
                foreach (object tag in ToList(Guarded.FieldOf<IEnumerable>(selectorTagsField, dialog, "ClCompat.Dialogs.SelectorTags")))
                {
                    string name = TagName(tag) ?? "";
                    if (name.ToLowerInvariant().Contains(search))
                    {
                        result.Add(tag);
                    }
                }
                return result;
            }

            public static bool SelectorIsSelected(Window dialog, object tag)
            {
                IList selected = Guarded.FieldOf<IList>(selectorSelectedField, dialog, "ClCompat.Dialogs.SelectorSelected");
                return selected != null && selected.Contains(tag);
            }

            /// <summary>Shift+click on a tag row: adds it to, or drops it from, the multi-selection.</summary>
            public static bool ToggleSelectorSelected(Window dialog, object tag)
            {
                IList selected = Guarded.FieldOf<IList>(selectorSelectedField, dialog, "ClCompat.Dialogs.SelectorSelected");
                if (selected == null || tag == null)
                {
                    return false;
                }
                // MUTATION-C: mirrors Dialog_TagSelector.cs:112-116; the Shift+click branch is inline in DrawTagList.
                if (selected.Contains(tag))
                {
                    selected.Remove(tag);
                    return false;
                }
                selected.Add(tag);
                return true;
            }

            /// <summary>A plain click on a tag row: hands every selected tag, then this one, to the caller and closes.</summary>
            public static void SelectorCloseAndSelect(Window dialog, object tag)
            {
                Guarded.Call(selectorCloseAndSelect, dialog, "ClCompat.Dialogs.SelectorCloseAndSelect", tag);
            }

            // ---- Dialog_SetTagLoadoutState ----

            public static object PopupElement(Window dialog)
            {
                return Guarded.FieldOf<object>(popupElementField, dialog, "ClCompat.Dialogs.PopupElement");
            }

            /// <summary>The popup's own state dropdown generator; static, taking the element.</summary>
            public static MethodInfo PopupStateGenerator
            {
                get { return ready ? popupGetElems : null; }
            }

            // ---- Dialog_LoadoutStateEditor ----

            public static string EditorNewName(Window dialog)
            {
                return Guarded.FieldOf(editorNameField, dialog, "ClCompat.Dialogs.EditorNewName", "");
            }

            public static void SetEditorNewName(Window dialog, string value)
            {
                if (ready && dialog != null)
                {
                    editorNameField.SetValue(dialog, value ?? ""); // MUTATION-C: mirrors Dialog_LoadoutStateEditor.cs:44 (GUI.TextField writes the field each draw)
                }
            }

            /// <summary>The states in the editor's own order (name, descending), less any the manager no longer holds.</summary>
            public static List<object> EditorStates(Window dialog)
            {
                var result = new List<object>();
                List<object> live = AllStates();
                foreach (object pair in ToList(Guarded.FieldOf<IEnumerable>(editorStatesField, dialog, "ClCompat.Dialogs.EditorStates")))
                {
                    object state = Guarded.Get<object>(pairFirstGetter, pair, "ClCompat.Dialogs.PairFirst");
                    if (state != null && live.Contains(state))
                    {
                        result.Add(state);
                    }
                }
                return result;
            }

            /// <summary>Shows or hides the row's rename box, the flag the rename icon sets.</summary>
            public static void SetEditorRenaming(Window dialog, object state, bool renaming)
            {
                foreach (object pair in ToList(Guarded.FieldOf<IEnumerable>(editorStatesField, dialog, "ClCompat.Dialogs.EditorStates")))
                {
                    if (Guarded.Get<object>(pairFirstGetter, pair, "ClCompat.Dialogs.PairFirst") == state)
                    {
                        Guarded.Call(pairSetSecond, pair, "ClCompat.Dialogs.SetEditorRenaming", renaming);
                    }
                }
            }

            public static void SetStateName(object state, string value)
            {
                if (ready && state != null)
                {
                    stateNameField.SetValue(state, value ?? ""); // MUTATION-C: mirrors Dialog_LoadoutStateEditor.cs:73 (the rename box writes state.name each draw)
                }
            }

            /// <summary>The editor's panic-state dropdown generator; an instance method taking the current panic state.</summary>
            public static MethodInfo EditorPanicGenerator
            {
                get { return ready ? editorPanicGenerator : null; }
            }
        }
    }
}
