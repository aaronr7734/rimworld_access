using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Dialog for naming a new allowed area before creation.
    /// </summary>
    public class Dialog_NameAllowedArea : Window
    {
        private string areaName;
        private bool focusedTextField;
        private int startAcceptingInputAtFrame;
        private Action<string> onNameConfirmed;

        private bool AcceptsInput => startAcceptingInputAtFrame <= Time.frameCount;

        public override Vector2 InitialSize => new Vector2(280f, 175f);

        public Dialog_NameAllowedArea(Action<string> onNameConfirmed)
        {
            this.onNameConfirmed = onNameConfirmed;
            this.areaName = "Area"; // Default name
            this.startAcceptingInputAtFrame = 0;
            doCloseX = true;
            forcePause = true;
            closeOnAccept = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            // Announce the dialog to the user
            TolkHelper.Speak("Name this allowed area. Enter a name and press OK or Enter to continue.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            bool enterPressed = false;

            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                enterPressed = true;
                Event.current.Use();
            }

            // Title
            Rect titleRect = new Rect(inRect);
            Text.Font = GameFont.Medium;
            titleRect.height = Text.LineHeight + 10f;
            Widgets.Label(titleRect, "Name this allowed area:");
            Text.Font = GameFont.Small;

            // Text field
            GUI.SetNextControlName("AreaNameField");
            string newName = Widgets.TextField(new Rect(0f, titleRect.height, inRect.width, 35f), areaName);

            if (AcceptsInput && newName.Length < 28)
            {
                areaName = newName;
            }
            else if (!AcceptsInput)
            {
                ((TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl)).SelectAll();
            }

            if (!focusedTextField)
            {
                UI.FocusControl("AreaNameField", this);
                focusedTextField = true;
            }

            // OK button
            if (Widgets.ButtonText(new Rect(15f, inRect.height - 35f - 10f, inRect.width - 30f, 35f), "OK") || enterPressed)
            {
                if (string.IsNullOrWhiteSpace(areaName))
                {
                    Messages.Message("Name cannot be empty", MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }

                // Check if name is already in use
                Map map = Find.CurrentMap;
                if (map != null)
                {
                    foreach (Area area in map.areaManager.AllAreas)
                    {
                        if (area.Label == areaName)
                        {
                            Messages.Message("Name is already in use", MessageTypeDefOf.RejectInput, historical: false);
                            return;
                        }
                    }
                }

                // Name is valid, invoke callback and close
                onNameConfirmed?.Invoke(areaName);
                Find.WindowStack.TryRemove(this);
            }
        }
    }
}
