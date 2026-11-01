using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The game's one focus stack: a static front over a single
    /// <see cref="FocusStackCore"/>, mirroring the ActionRegistry/ActionCatalog
    /// split so the logic stays instance-testable.
    /// </summary>
    public static class FocusStack
    {
        private static readonly FocusStackCore core = new FocusStackCore();

        /// <summary>The shared core, exposed for dev-bridge introspection.</summary>
        public static FocusStackCore Core
        {
            get { return core; }
        }

        public static FocusScope Top
        {
            get { return core.Top; }
        }

        public static FocusScope Base
        {
            get { return core.Base; }
        }

        public static int Count
        {
            get { return core.Count; }
        }

        /// <summary>Transition term for the legacy suppression guards; see <see cref="FocusStackCore.AnyLiveModal"/>.</summary>
        public static bool AnyLiveModal
        {
            get { return core.AnyLiveModal; }
        }

        /// <summary>See <see cref="FocusStackCore.AnyLiveInputOwner"/>.</summary>
        public static bool AnyLiveInputOwner
        {
            get { return core.AnyLiveInputOwner; }
        }

        public static void Push(FocusScope scope)
        {
            core.Push(scope);
        }

        /// <summary>See <see cref="FocusStackCore.BeginReconcile"/>.</summary>
        public static void BeginReconcile()
        {
            core.BeginReconcile();
        }

        /// <summary>See <see cref="FocusStackCore.EndReconcile"/>.</summary>
        public static void EndReconcile()
        {
            core.EndReconcile();
        }

        /// <summary>See <see cref="FocusStackCore.InsertBelow"/>.</summary>
        public static void InsertBelow(FocusScope scope, FocusScope reference)
        {
            core.InsertBelow(scope, reference);
        }

        /// <summary>Bottom-up scope view for ordering decisions; do not mutate.</summary>
        public static System.Collections.Generic.IReadOnlyList<FocusScope> ScopesBottomUp
        {
            get { return core.ScopesBottomUp; }
        }

        public static bool Pop(FocusScope scope)
        {
            return core.Pop(scope);
        }

        /// <summary>See <see cref="FocusStackCore.Contains"/>.</summary>
        public static bool Contains(FocusScope scope)
        {
            return core.Contains(scope);
        }

        public static void SetBase(FocusScope scope)
        {
            core.SetBase(scope);
        }

        public static void ClearToBase(GameBoundary why)
        {
            core.ClearToBase(why);
        }

        /// <summary>See <see cref="FocusStackCore.RefocusTop"/>.</summary>
        public static void RefocusTop()
        {
            core.RefocusTop();
        }

        /// <summary>See <see cref="FocusStackCore.SuppressRefocus"/>.</summary>
        public static IDisposable SuppressRefocus()
        {
            return core.SuppressRefocus();
        }

        public static bool Dispatch(KeyEventSnapshot e, out string consumedActionId)
        {
            return core.Dispatch(e, ActionRegistry.Catalog, out consumedActionId);
        }

        public static bool ClaimedBelowModals(KeyChord chord)
        {
            return core.ClaimedBelowModals(chord, ActionRegistry.Catalog);
        }

        public static bool OfferChar(char c)
        {
            return core.OfferChar(c);
        }

        /// <summary>See <see cref="FocusStackCore.TopCharSinkScope"/>.</summary>
        public static FocusScope TopCharSinkScope
        {
            get { return core.TopCharSinkScope; }
        }

        public static string DebugDump()
        {
            return core.DebugDump();
        }
    }
}
