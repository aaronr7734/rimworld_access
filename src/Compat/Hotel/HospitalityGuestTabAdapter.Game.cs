using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Hospitality's Guest tab (Hospitality.ITab_Pawn_Guest), which
    /// replaces vanilla's Guest tab for visitors. Rows follow FillTabGuest's order: the two
    /// area pickers, the entertain and befriend checkboxes, Make Default, Send Away and the recruit
    /// button, then the penalty and goodwill lines, the friends bar, the recruit status line and the
    /// hospitality bar. A trader gets FillTabTrader's single line. Every row reads live state.
    /// The area pickers write through the tab's own SetAreaRestriction/SetAreaShopping, which apply
    /// to the guest's whole group, and the buttons run the tab's own button bodies.
    /// </summary>
    internal sealed class HospitalityGuestTabAdapter : InspectNodeAdapter
    {
        private readonly MethodInfo isTrader;            // GuestUtility.IsTrader(Pawn, bool)
        private readonly MethodInfo compGuestOf;         // CompUtility.CompGuest(Pawn)
        private readonly MethodInfo friendsInColony;     // GuestUtility.GetFriendsInColony(Pawn)
        private readonly MethodInfo friendsSeniority;    // GuestUtility.GetFriendsSeniorityInColony(Pawn)
        private readonly MethodInfo requiredFriends;     // ITab_Pawn_Guest.GetRequiredFriends(Pawn)
        private readonly MethodInfo requiredSeniority;   // ITab_Pawn_Guest.GetRequiredSeniority(Pawn)
        private readonly MethodInfo recruitPenalty;      // GuestUtility.RecruitPenalty(Pawn)
        private readonly MethodInfo forcedPenalty;       // GuestUtility.ForcedRecruitPenalty(Pawn)
        private readonly MethodInfo minRecruitOpinion;   // GuestUtility.GetMinRecruitOpinion(Pawn)
        private readonly MethodInfo visitScore;          // GuestUtility.GetVisitScore(Pawn, out float)
        private readonly MethodInfo mayRecruitNow;       // GuestUtility.MayRecruitRightNow(Pawn)
        private readonly MethodInfo recruitDialog;       // ITab_Pawn_Guest.RecruitDialog(Pawn, bool)
        private readonly MethodInfo setAllDefaults;      // ITab_Pawn_Guest.SetAllDefaults(Pawn)
        private readonly MethodInfo sendHomeDialog;      // ITab_Pawn_Guest.SendHomeDialog(Lord)
        private readonly MethodInfo setAreaRestriction;  // ITab_Pawn_Guest.SetAreaRestriction(Area)
        private readonly MethodInfo setAreaShopping;     // ITab_Pawn_Guest.SetAreaShopping(Area)
        private readonly FieldInfo lordField;            // CompGuest.lord
        private readonly FieldInfo entertainField;       // CompGuest.entertain
        private readonly FieldInfo makeFriendsField;     // CompGuest.makeFriends
        private readonly PropertyInfo onlyByForce;       // CompGuest.WillOnlyJoinByForce
        private readonly PropertyInfo guestArea;         // CompGuest.GuestArea
        private readonly PropertyInfo shoppingArea;      // CompGuest.ShoppingArea
        private readonly HospitalityAreas areas;

        private readonly bool ready;
        private InspectTabBase sharedTab;

        public override bool Ready => ready;

        private HospitalityGuestTabAdapter(Type tabType)
        {
            var surface = new ReflectionSurface("HospitalityGuestTabAdapter");
            surface.Supplied("Hospitality.ITab_Pawn_Guest", tabType);
            Type guestUtility = surface.Type("Hospitality.Utilities.GuestUtility");
            Type compUtility = surface.Type("Hospitality.Utilities.CompUtility");
            Type compGuest = surface.Type("Hospitality.CompGuest");
            Type[] pawnArg = { typeof(Pawn) };

            isTrader = surface.Method(guestUtility, "IsTrader", new[] { typeof(Pawn), typeof(bool) });
            compGuestOf = surface.Method(compUtility, "CompGuest", pawnArg);
            friendsInColony = surface.Method(guestUtility, "GetFriendsInColony", pawnArg);
            friendsSeniority = surface.Method(guestUtility, "GetFriendsSeniorityInColony", pawnArg);
            requiredFriends = surface.Method(tabType, "GetRequiredFriends", pawnArg);
            requiredSeniority = surface.Method(tabType, "GetRequiredSeniority", pawnArg);
            recruitPenalty = surface.Method(guestUtility, "RecruitPenalty", pawnArg);
            forcedPenalty = surface.Method(guestUtility, "ForcedRecruitPenalty", pawnArg);
            minRecruitOpinion = surface.Method(guestUtility, "GetMinRecruitOpinion", pawnArg);
            visitScore = surface.Method(guestUtility, "GetVisitScore", new[] { typeof(Pawn), typeof(float).MakeByRefType() });
            mayRecruitNow = surface.Method(guestUtility, "MayRecruitRightNow", pawnArg);
            recruitDialog = surface.Method(tabType, "RecruitDialog", new[] { typeof(Pawn), typeof(bool) });
            setAllDefaults = surface.Method(tabType, "SetAllDefaults", pawnArg);
            sendHomeDialog = surface.Method(tabType, "SendHomeDialog", new[] { typeof(Lord) });
            setAreaRestriction = surface.Method(tabType, "SetAreaRestriction", new[] { typeof(Area) });
            setAreaShopping = surface.Method(tabType, "SetAreaShopping", new[] { typeof(Area) });
            lordField = surface.Field(compGuest, "lord");
            entertainField = surface.Field(compGuest, "entertain");
            makeFriendsField = surface.Field(compGuest, "makeFriends");
            onlyByForce = surface.Property(compGuest, "WillOnlyJoinByForce");
            guestArea = surface.Property(compGuest, "GuestArea");
            shoppingArea = surface.Property(compGuest, "ShoppingArea");
            areas = new HospitalityAreas(surface);
            ready = surface.Ready;
        }

        public static void TryRegister()
        {
            CompatRegistration.TabAdapter("Hospitality.ITab_Pawn_Guest",
                t => new HospitalityGuestTabAdapter(t) { sharedTab = InspectTabManager.GetSharedInstance(t) },
                "Hospitality guest tab compat");
        }

        // Dispatch token, never displayed raw.
        public override string CategoryKey => "Hospitality Guest";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => TabName();

        public override string CategoryDisplayName(object obj) => TabName();

        private static string TabName()
        {
            return CompatText.ResolveOrFallback("TabGuest", "RimWorldAccess.Compat.Hospitality.GuestTabName");
        }

        public override bool CanExpand(object obj)
        {
            return ready && InspectionTreeBuilder.GetPawnFromThing(obj) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
            {
                return;
            }
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
            {
                return;
            }
            InspectNodeFactory.GuardedBuild("HospitalityGuestTabAdapter", () =>
            {
                if ((bool)isTrader.Invoke(null, new object[] { pawn, true }))
                {
                    InspectRows.Info(categoryItem, () => CompatText.ModAdjustedFor("IsATrader", pawn));
                    return;
                }
                object comp = compGuestOf.Invoke(null, new object[] { pawn });
                if (comp == null)
                {
                    return;
                }
                if (lordField.GetValue(comp) != null)
                {
                    BuildControls(categoryItem, pawn, comp, mode);
                }
                BuildStatus(categoryItem, pawn, comp);
            });
        }

        private void BuildControls(InspectionTreeItem parent, Pawn pawn, object comp, InspectionMode mode)
        {
            Func<Area> stay = () => (Area)guestArea.GetValue(comp, null);
            Func<Area> shop = () => (Area)shoppingArea.GetValue(comp, null);
            InspectRows.RadioGroup(parent, CompatText.ModText("AreaToStay"), () => HospitalityAreas.StayLabel(stay()),
                CompatText.ModTip("AreaToStayTooltip"), () => AreaChoices(pawn, HospitalityAreas.StayLabel, stay, setAreaRestriction), mode);
            InspectRows.RadioGroup(parent, CompatText.ModText("AreaToBuy"), () => areas.ShoppingLabel(shop()),
                CompatText.ModTip("AreaToBuyTooltip"), () => AreaChoices(pawn, areas.ShoppingLabel, shop, setAreaShopping), mode);

            // MUTATION-C: mirrors ITab_Pawn_Guest.FillTabGuest, which copies its two CheckboxLabeled refs
            // straight into CompGuest.entertain/makeFriends; the column workers' setters need a live PawnTable.
            InspectRows.Checkbox(parent, CompatText.ModText("ImproveRelationship"), CompatText.ModTip("ImproveTooltip"),
                () => (bool)entertainField.GetValue(comp), v => entertainField.SetValue(comp, v), mode);
            // MUTATION-C: same inline write as the checkbox above.
            InspectRows.Checkbox(parent, CompatText.ModText("MakeFriends"), CompatText.ModTip("TryRecruitTooltip"),
                () => (bool)makeFriendsField.GetValue(comp), v => makeFriendsField.SetValue(comp, v), mode);

            InspectionTreeItem makeDefault = HotelTreeRows.Button(parent, CompatText.ModText("MakeDefault"),
                CompatText.ModTip("MakeDefaultTooltip"), () => OnMakeDefault(pawn), mode);
            makeDefault.OpensOverlayMenu = true;
            InspectionTreeItem sendAway = HotelTreeRows.Button(parent, CompatText.ModText("SendAway"),
                CompatText.ModTip("SendAwayTooltip"), () => OnSendAway(pawn), mode);
            sendAway.OpensOverlayMenu = true;
            BuildRecruitRow(parent, pawn, comp, mode);
        }

        private void BuildStatus(InspectionTreeItem parent, Pawn pawn, object comp)
        {
            InspectRows.Info(parent, () => PenaltyLine(pawn, comp));
            InspectRows.Info(parent, () => GoodwillLine(pawn));
            InspectRows.Info(parent, () => FriendsBarLine(pawn, comp));
            InspectRows.Info(parent, () => StatusLine(pawn, comp));
            InspectRows.Info(parent, () => HospitalityBarLine(pawn));
            parent.Children.RemoveAll(c => string.IsNullOrEmpty(c.Label));
        }

        // Each status line is null where FillTabGuest draws nothing; the build drops it then, and a
        // line whose condition flips later is picked up by the next category rebuild.

        private string PenaltyLine(Pawn pawn, object comp)
        {
            Faction faction = pawn.Faction;
            if (faction == null || !faction.HasGoodwill || CanNeverRecruit(pawn, comp))
            {
                return null;
            }
            return CompatText.ModArgs("RecruitmentPenalty",
                Invoke<int>(recruitPenalty, pawn).ToString("##0"), Invoke<int>(forcedPenalty, pawn).ToString("##0"));
        }

        private static string GoodwillLine(Pawn pawn)
        {
            Faction faction = pawn.Faction;
            if (faction == null || !faction.HasGoodwill)
            {
                return null;
            }
            return InspectRows.LabeledValue(CompatText.ModText("FactionGoodwill"), faction.PlayerGoodwill.ToString("##0"));
        }

        private string FriendsBarLine(Pawn pawn, object comp)
        {
            if (OnlyByForce(comp) || CanNeverRecruit(pawn, comp))
            {
                return null;
            }
            bool royal = HospitalityCompat.HasRoyalTitle(pawn);
            int have = Friends(pawn, royal);
            int need = Required(pawn, royal);
            string label = royal
                ? CompatText.ModArgs("SeniorityRequirement", have / 100, need / 100)
                : CompatText.ModArgs("FriendsRequirement", have, need);
            float fill = need == 0 ? 0f : Mathf.Clamp01((float)have / need);
            return "RimWorldAccess.Compat.Hospitality.BarLine".Translate(label, fill.ToStringPercent());
        }

        private string StatusLine(Pawn pawn, object comp)
        {
            if (CanNeverRecruit(pawn, comp))
            {
                return CompatText.ModAdjustedFor("CanNeverRecruit", pawn).StripTags();
            }
            if (OnlyByForce(comp))
            {
                return CompatText.ModAdjustedFor("OnlyJoinsByForce", pawn).StripTags();
            }
            bool royal = HospitalityCompat.HasRoyalTitle(pawn);
            int need = Required(pawn, royal);
            float percent = 100f * Friends(pawn, royal) / need;
            if (percent <= 99f)
            {
                return CompatText.ModAdjustedFor("NotEnoughFriends", pawn, Invoke<int>(minRecruitOpinion, pawn)).StripTags();
            }
            return CompatText.ModAdjustedFor("CanNowBeRecruited", pawn);
        }

        private string HospitalityBarLine(Pawn pawn)
        {
            object[] args = { pawn, 0f };
            if (!(bool)visitScore.Invoke(null, args))
            {
                return null;
            }
            return "RimWorldAccess.Compat.Hospitality.BarLine".Translate(
                CompatText.ModText("Hospitality") + ":", Mathf.Clamp01((float)args[1]).ToStringPercent());
        }

        // The recruit button: DialogUtility.DrawRecruitButton's four branches, read live.

        private struct RecruitButton
        {
            public bool Forced;
            public string DisabledReason;
        }

        private RecruitButton RecruitState(Pawn pawn, object comp)
        {
            bool royal = HospitalityCompat.HasRoyalTitle(pawn);
            bool byForce = OnlyByForce(comp);
            bool blocked = !Invoke<bool>(mayRecruitNow, pawn) || CanNeverRecruit(pawn, comp);
            bool enoughFriends = Friends(pawn, royal) >= Required(pawn, royal);
            bool friendly = enoughFriends && !byForce;
            string reason = blocked ? CompatText.ModText("CantRecruit")
                : royal && !friendly ? CompatText.ModText("CantForceRecruit")
                : null;
            return new RecruitButton { Forced = !royal && !friendly, DisabledReason = reason };
        }

        private void BuildRecruitRow(InspectionTreeItem parent, Pawn pawn, object comp, InspectionMode mode)
        {
            bool readOnly = mode == InspectionMode.ReadOnly;
            Func<string> label = () =>
            {
                RecruitButton state = RecruitState(pawn, comp);
                return CompatText.ModText(state.Forced && state.DisabledReason == null ? "ForceRecruit" : "Recruit");
            };
            InspectionTreeItem item = InspectNodeFactory.ActionRow(parent, label(), pawn, null, opensOverlayMenu: true);
            item.LabelProvider = label;
            item.DescribeElement = () =>
            {
                RecruitButton state = RecruitState(pawn, comp);
                string tip = state.DisabledReason
                    ?? CompatText.ModText(state.Forced ? "ForceRecruitTooltip" : "RecruitTooltip");
                return new ElementDescription
                {
                    Label = label(),
                    Role = ElementRole.Button,
                    Disabled = state.DisabledReason != null,
                    ReadOnly = readOnly,
                    Extras = CompatText.Flatten(tip),
                };
            };
            if (!readOnly)
            {
                item.OnActivate = () => OnRecruit(pawn, comp);
            }
        }

        private void OnRecruit(Pawn pawn, object comp)
        {
            Guarded.Run("HospitalityGuestTabAdapter recruit", () =>
            {
                RecruitButton state = RecruitState(pawn, comp);
                if (state.DisabledReason != null)
                {
                    // DrawButtonDisabled's own refusal.
                    Messages.Message(state.DisabledReason, MessageTypeDefOf.RejectInput, true);
                    return;
                }
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
                recruitDialog.Invoke(null, new object[] { pawn, state.Forced });
            }, "RimWorldAccess.Compat.Hospitality.ActionFailed");
        }

        private void OnMakeDefault(Pawn pawn)
        {
            Guarded.Run("HospitalityGuestTabAdapter make default", () =>
            {
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
                setAllDefaults.Invoke(null, new object[] { pawn });
                TolkHelper.Speak("RimWorldAccess.Compat.Hospitality.DefaultsApplied".Loc());
            }, "RimWorldAccess.Compat.Hospitality.ActionFailed");
        }

        private void OnSendAway(Pawn pawn)
        {
            Guarded.Run("HospitalityGuestTabAdapter send away", () =>
            {
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
                sendHomeDialog.Invoke(null, new object[] { pawn.GetLord() });
            }, "RimWorldAccess.Compat.Hospitality.ActionFailed");
        }

        /// <summary>
        /// The strip's cells, each writing through the tab's own group-wide setter. That setter reads the selected pawn, so the guest is
        /// reselected first, as the tree builder itself does before reading tabs.
        /// </summary>
        private List<InspectRows.RadioChoice> AreaChoices(Pawn pawn, Func<Area, string> labelOf, Func<Area> current, MethodInfo tabSetter)
        {
            List<Area> cells = areas.Choices(pawn.MapHeld);
            var choices = new List<InspectRows.RadioChoice>(cells.Count);
            foreach (Area area in cells)
            {
                Area chosen = area;
                choices.Add(new InspectRows.RadioChoice
                {
                    Label = labelOf(chosen),
                    IsSelected = () => current() == chosen,
                    Select = () =>
                    {
                        if (Find.Selector.SingleSelectedThing != pawn)
                        {
                            Find.Selector.ClearSelection();
                            Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                        }
                        Guarded.Call(tabSetter, sharedTab, "HospitalityGuestTabAdapter area", chosen);
                        SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
                    },
                });
            }
            return choices;
        }

        private bool OnlyByForce(object comp)
        {
            return (bool)onlyByForce.GetValue(comp, null);
        }

        // FillTabGuest's own "flag2".
        private bool CanNeverRecruit(Pawn pawn, object comp)
        {
            Faction faction = pawn.Faction;
            return (faction != null && !faction.HasGoodwill) || (HospitalityCompat.HasRoyalTitle(pawn) && OnlyByForce(comp));
        }

        private int Friends(Pawn pawn, bool royal)
        {
            return Invoke<int>(royal ? friendsSeniority : friendsInColony, pawn);
        }

        private int Required(Pawn pawn, bool royal)
        {
            return Invoke<int>(royal ? requiredSeniority : requiredFriends, pawn);
        }

        private static T Invoke<T>(MethodInfo method, Pawn pawn)
        {
            return (T)method.Invoke(null, new object[] { pawn });
        }
    }
}
