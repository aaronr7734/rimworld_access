using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class PolicyEditorPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!PolicyEditorState.IsActive)
                return;

            DrawMenuOverlay();
        }

        private static void DrawMenuOverlay()
        {
            float screenWidth = UI.screenWidth;
            float overlayWidth = 700f;
            float overlayHeight = 100f;
            float overlayX = (screenWidth - overlayWidth) / 2f;
            float overlayY = 20f;

            Rect overlayRect = new Rect(overlayX, overlayY, overlayWidth, overlayHeight);

            Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            Widgets.DrawBoxSolid(overlayRect, backgroundColor);
            Widgets.DrawBox(overlayRect, 2);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            string title = PolicyEditorState.GetTitle();
            if (PolicyEditorState.CurrentPolicy != null)
                title += $": {PolicyEditorState.CurrentPolicy.label}";

            Rect titleRect = new Rect(overlayX, overlayY + 10f, overlayWidth, 30f);
            Widgets.Label(titleRect, title);

            Text.Font = GameFont.Tiny;
            string instructions = PolicyEditorState.CurrentType == PolicyEditorState.PolicyType.Reading
                ? "Tab: Switch panel | Escape: Close"
                : "Escape: Close";
            Rect instructionsRect = new Rect(overlayX, overlayY + 50f, overlayWidth, 25f);
            Widgets.Label(instructionsRect, instructions);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
