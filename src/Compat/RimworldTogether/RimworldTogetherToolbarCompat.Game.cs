using System;
using System.Reflection;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Declares Rimworld Together's HUD icon strip to <see cref="ModHudToolbarRegistry"/>. The
    /// mod draws tooltip-less <c>Widgets.ButtonImageWithBG</c> buttons plus a ping label
    /// straight onto the map UI (<c>Patch_GlobalControls_GlobalControlsOnGUI</c>) and world UI
    /// (<c>Patch_WorldInterface_WorldInterfaceOnGUI</c>, where Search replaces Market), gated on
    /// <c>SessionManager.IsReadyToPlay</c> — no window, no labels, nothing to capture. Each
    /// entry here mirrors the matching <c>InterfaceDrawer.Draw*Button</c> click body exactly,
    /// in the drawer's own call order; entries are offered per-view the same way the two
    /// patches split (Market on the map, settlement Search on the world).
    /// </summary>
    internal static class RimworldTogetherToolbarCompat
    {
        private const string ToolbarId = "rimworldtogether";
        private const string PackageId = "nova.rimworldtogether";

        /// <summary>
        /// One RT singleton dialog: the shared IsDialogOpen/Instance shape every DLG_* class
        /// carries, plus its parameterless constructor for the drawer-mirrored open branch.
        /// DLG_Leaderboard alone has no such constructor (the server's reply builds it via
        /// <c>PM_Leaderboard</c>), so its handle skips ctor resolution.
        /// </summary>
        private sealed class DialogHandle
        {
            private readonly PropertyInfo isOpen;
            private readonly PropertyInfo instance;
            private readonly ConstructorInfo ctor;

            public DialogHandle(ReflectionSurface surface, string typeName, bool constructible = true)
            {
                Type type = surface.Type(typeName);
                isOpen = surface.Property(type, "IsDialogOpen");
                instance = surface.Property(type, "Instance");
                ctor = constructible ? surface.Constructor(type, Type.EmptyTypes) : null;
            }

            public bool IsOpen()
            {
                return (bool)isOpen.GetValue(null);
            }

            public Window Create()
            {
                return ctor.Invoke(null) as Window;
            }

            public void CloseInstance()
            {
                var window = instance.GetValue(null) as Window;
                if (window != null)
                {
                    window.Close(true);
                }
            }
        }

        private static readonly PropertyInfo isReadyToPlayProperty;
        private static readonly PropertyInfo isAdminProperty;
        private static readonly PropertyInfo currentActionValuesProperty;
        private static readonly PropertyInfo guildActionProperty;
        private static readonly PropertyInfo leaderboardActionProperty;
        private static readonly PropertyInfo marketActionProperty;
        private static readonly PropertyInfo rollbackActionProperty;
        private static readonly PropertyInfo actionIsEnabledProperty;
        private static readonly PropertyInfo currentPingProperty;
        private static readonly MethodInfo pushNewDialogMethod;
        private static readonly ConstructorInfo messageCtor;
        private static readonly ConstructorInfo waitCtor;
        private static readonly ConstructorInfo yesNoCtor;
        private static readonly ConstructorInfo guildPacketCtor;
        private static readonly PropertyInfo guildStepModeProperty;
        private static readonly object guildInformationStep;
        private static readonly object guildPacketHeader;
        private static readonly PropertyInfo serverEndpointProperty;
        private static readonly MethodInfo enqueuePacketMethod;
        private static readonly MethodInfo getSaveNameMethod;
        private static readonly MethodInfo leaderboardAskMethod;
        private static readonly DialogHandle optionsDialog;
        private static readonly DialogHandle chatDialog;
        private static readonly DialogHandle guildDialog;
        private static readonly DialogHandle leaderboardDialog;
        private static readonly DialogHandle marketDialog;
        private static readonly DialogHandle searchDialog;
        private static readonly DialogHandle adminDialog;
        private static readonly bool ready;

        static RimworldTogetherToolbarCompat()
        {
            var surface = new ReflectionSurface("RimworldTogetherToolbarCompat");

            Type sessionManagerType = surface.Type("RTClient.Managers.SessionManager");
            isReadyToPlayProperty = surface.Property(sessionManagerType, "IsReadyToPlay");
            isAdminProperty = surface.Property(sessionManagerType, "IsAdmin");
            currentActionValuesProperty = surface.Property(sessionManagerType, "CurrentActionValues");

            Type actionsConfigType = surface.Type("RTShared.Files.Default.FL_ActionsConfig");
            guildActionProperty = surface.Property(actionsConfigType, "GuildAction");
            leaderboardActionProperty = surface.Property(actionsConfigType, "LeaderboardAction");
            marketActionProperty = surface.Property(actionsConfigType, "MarketAction");
            rollbackActionProperty = surface.Property(actionsConfigType, "RollbackAction");
            actionIsEnabledProperty = surface.Property(surface.Type("RTShared.Files.Actions.ACT_Base"), "IsEnabled");

            currentPingProperty = surface.Property(surface.Type("RTNetwork.PacketManagers.PM_KeepAlive"), "CurrentPing");

            Type dlgBaseType = surface.Type("RTClient.Dialogs.DLG_Base");
            pushNewDialogMethod = surface.Method(dlgBaseType, "PushNewDialog", new[] { typeof(Window) });
            messageCtor = surface.Constructor(surface.Type("RTClient.Dialogs.Default.DLG_Message"),
                new[] { typeof(string), typeof(string[]), typeof(Action) });
            waitCtor = surface.Constructor(surface.Type("RTClient.Dialogs.Default.DLG_Wait"), new[] { typeof(string) });
            yesNoCtor = surface.Constructor(surface.Type("RTClient.Dialogs.Default.DLG_YesNo"),
                new[] { typeof(string), typeof(Action), typeof(Action), typeof(string), typeof(string), typeof(Color?), typeof(Color?) });

            Type guildPacketType = surface.Type("RTNetwork.Packets.PKT_PlayerGuild");
            guildPacketCtor = surface.Constructor(guildPacketType, Type.EmptyTypes);
            guildStepModeProperty = surface.Property(guildPacketType, "StepMode");
            guildInformationStep = surface.EnumValue(surface.Type("RTNetwork.Packets.PKT_PlayerGuild+GuildStepMode"), "Information");
            Type packetHeaderType = surface.Type("RTShared.Misc.PacketHeader");
            guildPacketHeader = surface.EnumValue(packetHeaderType, "Guild");
            serverEndpointProperty = surface.Property(surface.Type("RTNetwork.Components.Network"), "ServerEndpoint");
            enqueuePacketMethod = surface.Method(surface.Type("RTNetwork.Components.Listener"), "EnqueuePacket",
                new[] { packetHeaderType, typeof(object) });
            getSaveNameMethod = surface.Method(surface.Type("RTClient.PacketManagers.PM_Saves"), "GetSaveName", Type.EmptyTypes);

            leaderboardAskMethod = surface.Method(surface.Type("RTClient.PacketManagers.PM_Leaderboard"), "Ask", Type.EmptyTypes);

            optionsDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Options");
            chatDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Chat");
            guildDialog = new DialogHandle(surface, "RTClient.Dialogs.Guild.DLG_Guild");
            leaderboardDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Leaderboard", constructible: false);
            marketDialog = new DialogHandle(surface, "RTClient.Dialogs.Marketplace.DLG_Marketplace");
            searchDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_SettlementFinder");
            adminDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Admin");

            ready = surface.Ready;
        }

        public static void Register()
        {
            if (!ready)
            {
                return;
            }

            ModHudToolbarRegistry.Register(ToolbarId, Title, IsReadyToPlay);

            // InterfaceDrawer's call order, each mirroring its Draw*Button. Only chat can be open while
            // this menu is (other dialogs absorb input), so only its label carries open/closed state.
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Options".Translate(),
                () => Toggle(optionsDialog));
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => (chatDialog.IsOpen()
                    ? "RimWorldAccess.Compat.RimworldTogether.Toolbar.CloseChat"
                    : "RimWorldAccess.Compat.RimworldTogether.Toolbar.OpenChat").Translate(),
                () => Toggle(chatDialog));
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Guild".Translate(),
                ActivateGuild);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Leaderboard".Translate(),
                ActivateLeaderboard);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Market".Translate(),
                ActivateMarket,
                () => !WorldRendererUtility.WorldSelected);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.SettlementSearch".Translate(),
                () => Toggle(searchDialog),
                () => WorldRendererUtility.WorldSelected);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Rollback".Translate(),
                ActivateRollback,
                () => !WorldRendererUtility.WorldSelected);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Admin".Translate(),
                () => Toggle(adminDialog),
                IsAdmin);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Ping".Translate(
                    (int)currentPingProperty.GetValue(null)),
                null);
        }

        /// <summary>The mod's own listed name, so the pause-menu item and the menu's region heading match whatever the player's mod list actually calls it.</summary>
        private static string Title()
        {
            return CompatText.ModTitle(PackageId, "RimWorld Together");
        }

        private static bool IsReadyToPlay()
        {
            return (bool)isReadyToPlayProperty.GetValue(null);
        }

        private static bool IsAdmin()
        {
            return (bool)isAdminProperty.GetValue(null);
        }

        /// <summary>Mirrors the shared Draw*Button toggle shape: open branch is vanilla's own <c>Find.WindowStack.Add(new DLG_*())</c> line, close branch its <c>Instance.Close(true)</c> line.</summary>
        private static void Toggle(DialogHandle dialog)
        {
            if (dialog.IsOpen())
            {
                dialog.CloseInstance();
                return;
            }
            Window window = dialog.Create();
            if (window != null)
            {
                Find.WindowStack.Add(window);
            }
        }

        /// <summary>Mirrors <c>InterfaceDrawer.DrawGuildButton</c>: a wait dialog, then a guild information request whose reply (<c>PM_Guilds</c>) opens the guild dialog.</summary>
        private static void ActivateGuild()
        {
            if (guildDialog.IsOpen())
            {
                guildDialog.CloseInstance();
                return;
            }
            if (!ActionEnabled(guildActionProperty))
            {
                PushDisabledMessage();
                return;
            }
            object waitDescription = waitCtor.GetParameters()[0].DefaultValue;
            pushNewDialogMethod.Invoke(null, new[] { waitCtor.Invoke(new[] { waitDescription }) });
            object packet = guildPacketCtor.Invoke(null);
            // MUTATION-C: mirrors InterfaceDrawer.DrawGuildButton; the request packet is built inline in the click body, with no method to call instead.
            guildStepModeProperty.SetValue(packet, guildInformationStep);
            enqueuePacketMethod.Invoke(serverEndpointProperty.GetValue(null), new[] { guildPacketHeader, packet });
        }

        private static void ActivateRollback()
        {
            if (!ActionEnabled(rollbackActionProperty))
            {
                PushDisabledMessage();
                return;
            }
            Action reload = delegate
            {
                GameDataSaveLoader.LoadGame((string)getSaveNameMethod.Invoke(null, null));
            };
            // l10n-exempt: the target mod's own hardcoded dialog strings (InterfaceDrawer.cs), mirrored byte-for-byte.
            object dialog = yesNoCtor.Invoke(new object[]
            {
                "Are you sure you want to rollback your save?", reload, null, "Yes", "No", (Color?)Color.red, null
            });
            pushNewDialogMethod.Invoke(null, new[] { dialog });
        }

        /// <summary>Mirrors <c>InterfaceDrawer.DrawLeaderboardButton</c>: the open branch asks the server (<c>PM_Leaderboard.Ask</c>) rather than constructing the dialog locally.</summary>
        private static void ActivateLeaderboard()
        {
            if (leaderboardDialog.IsOpen())
            {
                leaderboardDialog.CloseInstance();
                return;
            }
            if (!ActionEnabled(leaderboardActionProperty))
            {
                PushDisabledMessage();
                return;
            }
            leaderboardAskMethod.Invoke(null, null);
        }

        /// <summary>Mirrors <c>InterfaceDrawer.DrawMarketButton</c>, feature gate included.</summary>
        private static void ActivateMarket()
        {
            if (marketDialog.IsOpen())
            {
                marketDialog.CloseInstance();
                return;
            }
            if (!ActionEnabled(marketActionProperty))
            {
                PushDisabledMessage();
                return;
            }
            Find.WindowStack.Add(marketDialog.Create());
        }

        /// <summary>
        /// The drawer's <c>SessionManager.CurrentActionValues.XAction.IsEnabled</c> read. A null
        /// config folds to enabled — the mod's own defaults are all true, and the drawer only
        /// runs after a connect has populated the values anyway.
        /// </summary>
        private static bool ActionEnabled(PropertyInfo actionProperty)
        {
            object values = currentActionValuesProperty.GetValue(null);
            if (values == null)
            {
                return true;
            }
            object action = actionProperty.GetValue(values);
            return action == null || (bool)actionIsEnabledProperty.GetValue(action);
        }

        /// <summary>
        /// The disabled-feature branch shared by the guild/leaderboard/market buttons, verbatim:
        /// the mod's own <c>DLG_Message("ERROR", ...)</c> pushed through its own dialog stack.
        /// </summary>
        private static void PushDisabledMessage()
        {
            // l10n-exempt: both literals are the target mod's own hardcoded dialog strings
            // (InterfaceDrawer.cs), mirrored byte-for-byte so the keyboard path shows the exact
            // dialog a mouse click shows.
            object message = messageCtor.Invoke(new object[]
            {
                "ERROR",
                new[] { "This feature has been disabled in this server!" },
                null
            });
            pushNewDialogMethod.Invoke(null, new object[] { message });
        }
    }
}
