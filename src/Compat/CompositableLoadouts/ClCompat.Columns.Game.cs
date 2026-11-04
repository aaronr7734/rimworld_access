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
        /// The Assign-tab columns (<c>PawnColumnWorker_LoadoutState</c>, <c>PawnColumnWorker_TagSummary</c>)
        /// and the copy and paste buttons <c>LoadoutCopyPaste</c> draws in the state cell, whose
        /// clipboard the loadout overview shares.
        /// </summary>
        internal static class Columns
        {
            private static readonly Type loadoutStateWorkerType;
            private static readonly Type tagSummaryWorkerType;
            private static readonly FieldInfo copiedTagsField;
            private static readonly MethodInfo primedGetter;
            private static readonly MethodInfo primedSetter;
            private static readonly MethodInfo canPasteToMethod;
            private static readonly MethodInfo pasteToMethod;
            private static readonly ConstructorInfo copiedTagsCtor;
            private static readonly FieldInfo elemsToCopyField;
            private static readonly bool ready;

            static Columns()
            {
                var surface = new ReflectionSurface("ClCompat.Columns");
                loadoutStateWorkerType = surface.Type("Inventory.PawnColumnWorker_LoadoutState");
                tagSummaryWorkerType = surface.Type("Inventory.PawnColumnWorker_TagSummary");
                Type copyPasteType = surface.Type("Inventory.LoadoutCopyPaste");
                Type copiedTagsType = surface.Type("Inventory.CopiedTags");

                surface.Method(loadoutStateWorkerType, "GenerateMenuFor", new[] { typeof(Pawn) });
                copiedTagsField = surface.Field(copyPasteType, "copiedTags");
                PropertyInfo primed = surface.Property(copyPasteType, "Primed");
                primedGetter = primed?.GetGetMethod(true);
                primedSetter = surface.Required("LoadoutCopyPaste.Primed setter", primed?.GetSetMethod(true));
                canPasteToMethod = surface.Method(copyPasteType, "CanPasteTo", new[] { typeof(Pawn) });
                pasteToMethod = surface.Method(copyPasteType, "PasteTo", new[] { typeof(Pawn) });
                elemsToCopyField = surface.Field(copiedTagsType, "elemsToCopy");
                if (elementType != null)
                {
                    copiedTagsCtor = surface.Constructor(copiedTagsType,
                        new[] { typeof(Pawn), typeof(List<>).MakeGenericType(elementType), typeof(bool) });
                }

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            public static Type LoadoutStateWorkerType
            {
                get { return loadoutStateWorkerType; }
            }

            public static Type TagSummaryWorkerType
            {
                get { return tagSummaryWorkerType; }
            }

            /// <summary>True once anything was copied; the cell draws its paste button from then on.</summary>
            public static bool Primed
            {
                get { return ready && Guarded.Get(primedGetter, null, "ClCompat.Columns.Primed", false); }
            }

            /// <summary>The mod's own gate for the paste button: false when pasting would change nothing.</summary>
            public static bool CanPasteTo(Pawn pawn)
            {
                return ready && Guarded.Call(canPasteToMethod, null, "ClCompat.Columns.CanPasteTo", pawn) is bool can && can;
            }

            public static void PasteTo(Pawn pawn)
            {
                if (ready)
                {
                    Guarded.Call(pasteToMethod, null, "ClCompat.Columns.PasteTo", pawn);
                }
            }

            /// <summary>Copies every element of the pawn's loadout, to replace or add to the target's on paste.</summary>
            public static void CopyAll(Pawn pawn, bool replace)
            {
                if (!ready)
                {
                    return;
                }
                IList elements = TypedList(elementType, Elements(LoadoutOf(pawn)));
                object copied = Construct(copiedTagsCtor, "ClCompat.Columns.CopyAll", pawn, elements, replace);
                if (copied != null)
                {
                    SetClipboard(copied);
                }
            }

            /// <summary>Empties the clipboard, as the copy button's right click does before offering its options.</summary>
            public static void ClearClipboard()
            {
                if (ready)
                {
                    copiedTagsField.SetValue(null, null); // MUTATION-C: mirrors LoadoutCopyPaste.cs:88
                }
            }

            /// <summary>Adds one element to the clipboard, starting one for this pawn when it is empty.</summary>
            public static void AddToClipboard(Pawn pawn, object element, bool replace)
            {
                if (!ready)
                {
                    return;
                }
                object copied = copiedTagsField.GetValue(null);
                if (copied == null)
                {
                    copied = Construct(copiedTagsCtor, "ClCompat.Columns.AddToClipboard", pawn,
                        TypedList(elementType, null), replace);
                    if (copied == null)
                    {
                        return;
                    }
                }
                // MUTATION-C: mirrors LoadoutCopyPaste.cs:103-114; the selector callback is an inline lambda.
                (elemsToCopyField.GetValue(copied) as IList)?.Add(element);
                SetClipboard(copied);
            }

            // MUTATION-C: mirrors LoadoutCopyPaste.cs:85-97; the copy button's click body is inline.
            private static void SetClipboard(object copied)
            {
                copiedTagsField.SetValue(null, copied); // MUTATION-C: mirrors LoadoutCopyPaste.cs:85
                Guarded.Call(primedSetter, null, "ClCompat.Columns.Primed", true);
            }
        }
    }
}
