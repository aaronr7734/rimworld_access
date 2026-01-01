using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches Dialog_Trade to intercept and replace it with the windowless trade interface.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Trade), "PostOpen")]
    public static class TradeNavigationPatch_PostOpen
    {
        private static bool hasIntercepted = false;

        /// <summary>
        /// Postfix patch that runs after Dialog_Trade.PostOpen().
        /// Closes the visual dialog and opens the windowless trade interface.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(Dialog_Trade __instance)
        {
            // Only intercept once per dialog opening
            if (hasIntercepted)
                return;

            hasIntercepted = true;

            try
            {
                // Verify TradeSession is active
                if (!TradeSession.Active)
                {
                    Log.Warning("RimWorld Access: TradeSession is not active when Dialog_Trade opened");
                    return;
                }

                // Close the visual dialog immediately
                __instance.Close(doCloseSound: false);

                // Open the windowless trade interface
                TradeNavigationState.Open();
            }
            catch (System.Exception ex)
            {
                Log.Error($"RimWorld Access: Error intercepting trade dialog: {ex.Message}\n{ex.StackTrace}");
                hasIntercepted = false;
            }
        }

        /// <summary>
        /// Gets whether we've intercepted the current dialog.
        /// </summary>
        public static bool HasIntercepted => hasIntercepted;

        /// <summary>
        /// Resets the interception flag (called from PostClose patch).
        /// </summary>
        public static void ResetInterception()
        {
            hasIntercepted = false;
        }
    }

    /// <summary>
    /// Patches Dialog_Trade.Close to reset interception flag.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Trade), "Close")]
    public static class TradeNavigationPatch_Close
    {
        /// <summary>
        /// Reset the interception flag when a dialog closes.
        /// This ensures we can intercept the next trade dialog.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            TradeNavigationPatch_PostOpen.ResetInterception();
        }
    }
}
