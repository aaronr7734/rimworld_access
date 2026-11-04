using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only core model of Compositable Loadouts (packageId <c>Wiri.compositableloadouts</c>,
    /// namespace <c>Inventory</c>): a pawn's <c>LoadoutComponent</c>, its <c>Loadout</c> of
    /// <c>LoadoutElement</c>s, the <c>Tag</c>/<c>Item</c>/<c>ItemMember</c> definitions, the named
    /// <c>LoadoutState</c>s, the <c>LoadoutManager</c> game component, and the mod's windows. Model
    /// objects travel as <c>object</c>; read them only through these accessors.
    ///
    /// This file is shared by every slice and is not edited by them. A slice adds its own members as
    /// a nested static class in its own <c>ClCompat.&lt;Slice&gt;.Game.cs</c>, with its own
    /// <see cref="ReflectionSurface"/> and <c>Ready</c>, so one slice's drift never disables another.
    ///
    /// The invokers here call the mod's own methods; a caller that uses one to stand in for an inline
    /// mouse-only button body carries the MUTATION-C marker naming that body.
    /// </summary>
    internal static partial class ClCompat
    {
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly Type componentType;
        private static readonly Type loadoutType;
        private static readonly Type elementType;
        private static readonly Type stateType;
        private static readonly Type activeConditionType;
        private static readonly Type tagType;
        private static readonly Type itemType;
        private static readonly Type itemMemberType;
        private static readonly Type managerType;
        private static readonly Type utilityType;

        private static readonly Type loadoutEditorType;
        private static readonly Type tagEditorType;
        private static readonly Type tagSelectorType;
        private static readonly Type setTagLoadoutStateType;
        private static readonly Type loadoutStateEditorType;
        private static readonly Type itemSpecifierType;
        private static readonly Type confirmSettingsType;
        private static readonly Type overviewType;

        private static readonly MethodInfo componentLoadoutGetter;
        private static readonly MethodInfo componentAddTag;
        private static readonly MethodInfo componentRemoveTag;

        private static readonly FieldInfo loadoutElementsField;
        private static readonly MethodInfo loadoutCurrentStateGetter;
        private static readonly MethodInfo loadoutInPanicModeGetter;

        private static readonly MethodInfo elementTagGetter;
        private static readonly MethodInfo elementStateGetter;
        private static readonly MethodInfo elementConditionGetter;
        private static readonly MethodInfo elementStateNameGetter;
        private static readonly MethodInfo elementActiveMethod;
        private static readonly MethodInfo elementSetLoadoutState;

        private static readonly FieldInfo stateNameField;
        private static readonly ConstructorInfo stateCtor;

        private static readonly FieldInfo tagNameField;
        private static readonly FieldInfo tagItemsField;
        private static readonly ConstructorInfo tagCtor;

        private static readonly MethodInfo itemLabelGetter;
        private static readonly MethodInfo itemShortLabelGetter;
        private static readonly MethodInfo itemQuantityGetter;
        private static readonly MethodInfo itemRepresentativeDefGetter;
        private static readonly MethodInfo itemIsProstheticGetter;
        private static readonly MethodInfo itemIsApparelGetter;
        private static readonly MethodInfo itemHasAlternativesGetter;
        private static readonly MethodInfo itemMembersGetter;
        private static readonly MethodInfo itemRankedMembersDescription;

        private static readonly MethodInfo memberDefGetter;
        private static readonly MethodInfo memberLabelGetter;

        private static readonly FieldInfo managerInstanceField;
        private static readonly MethodInfo managerTagsGetter;
        private static readonly MethodInfo managerStatesGetter;
        private static readonly MethodInfo managerPanicStateGetter;
        private static readonly MethodInfo managerAddTag;
        private static readonly MethodInfo managerRemoveTag;
        private static readonly MethodInfo managerRemoveState;
        private static readonly MethodInfo managerSetPanicState;
        private static readonly MethodInfo managerActivePanicState;

        private static readonly MethodInfo isValidLoadoutHolderMethod;
        private static readonly MethodInfo getActiveStateMethod;
        private static readonly MethodInfo setActiveStateMethod;

        private static readonly object conditionStateActive;
        private static readonly object conditionStateInactive;

        private static readonly ConstructorInfo loadoutEditorCtor;
        private static readonly ConstructorInfo tagEditorCtor;
        private static readonly ConstructorInfo tagEditorForTagCtor;
        private static readonly ConstructorInfo tagSelectorCtor;
        private static readonly ConstructorInfo setTagLoadoutStateCtor;
        private static readonly ConstructorInfo loadoutStateEditorCtor;
        private static readonly ConstructorInfo itemSpecifierCtor;
        private static readonly MethodInfo wrapCallbackMethod;

        private static readonly bool ready;

        static ClCompat()
        {
            var surface = new ReflectionSurface("ClCompat");

            componentType = surface.Type("Inventory.LoadoutComponent");
            loadoutType = surface.Type("Inventory.Loadout");
            elementType = surface.Type("Inventory.LoadoutElement");
            stateType = surface.Type("Inventory.LoadoutState");
            activeConditionType = surface.Type("Inventory.ActiveCondition");
            tagType = surface.Type("Inventory.Tag");
            itemType = surface.Type("Inventory.Item");
            itemMemberType = surface.Type("Inventory.ItemMember");
            managerType = surface.Type("Inventory.LoadoutManager");
            utilityType = surface.Type("Inventory.Utility");

            loadoutEditorType = surface.Type("Inventory.Dialog_LoadoutEditor");
            tagEditorType = surface.Type("Inventory.Dialog_TagEditor");
            tagSelectorType = surface.Type("Inventory.Dialog_TagSelector");
            setTagLoadoutStateType = surface.Type("Inventory.Dialog_SetTagLoadoutState");
            loadoutStateEditorType = surface.Type("Inventory.Dialog_LoadoutStateEditor");
            itemSpecifierType = surface.Type("Inventory.Dialog_ItemSpecifier");
            confirmSettingsType = surface.Type("Inventory.Dialog_ConfirmSettings");
            overviewType = surface.Type("Inventory.MainTabWindow_LoadoutOverview");

            componentLoadoutGetter = Getter(surface.Property(componentType, "Loadout"));
            loadoutElementsField = surface.Field(loadoutType, "elements");
            loadoutCurrentStateGetter = Getter(surface.Property(loadoutType, "CurrentState"));
            loadoutInPanicModeGetter = Getter(surface.Property(loadoutType, "InPanicMode"));

            elementTagGetter = Getter(surface.Property(elementType, "Tag"));
            elementStateGetter = Getter(surface.Property(elementType, "State"));
            elementConditionGetter = Getter(surface.Property(elementType, "ActiveCondition"));
            elementStateNameGetter = Getter(surface.Property(elementType, "StateName"));

            stateNameField = surface.Field(stateType, "name");
            tagNameField = surface.Field(tagType, "name");
            tagItemsField = surface.Field(tagType, "requiredItems");
            stateCtor = surface.Constructor(stateType, new[] { typeof(string) });
            tagCtor = surface.Constructor(tagType, new[] { typeof(string) });

            itemLabelGetter = Getter(surface.Property(itemType, "Label"));
            itemShortLabelGetter = Getter(surface.Property(itemType, "ShortLabel"));
            itemQuantityGetter = Getter(surface.Property(itemType, "Quantity"));
            itemRepresentativeDefGetter = Getter(surface.Property(itemType, "RepresentativeDef"));
            itemIsProstheticGetter = Getter(surface.Property(itemType, "IsProsthetic"));
            itemIsApparelGetter = Getter(surface.Property(itemType, "IsApparel"));
            itemHasAlternativesGetter = Getter(surface.Property(itemType, "HasAlternatives"));
            itemMembersGetter = Getter(surface.Property(itemType, "Members"));
            itemRankedMembersDescription = surface.Method(itemType, "RankedMembersDescription", Type.EmptyTypes);

            memberDefGetter = Getter(surface.Property(itemMemberType, "Def"));
            memberLabelGetter = Getter(surface.Property(itemMemberType, "Label"));

            managerInstanceField = surface.Field(managerType, "instance");
            managerTagsGetter = Getter(surface.Property(managerType, "Tags"));
            managerStatesGetter = Getter(surface.Property(managerType, "States"));
            managerPanicStateGetter = Getter(surface.Property(managerType, "PanicState"));
            managerActivePanicState = surface.Method(managerType, "ActivePanicState", Type.EmptyTypes);

            isValidLoadoutHolderMethod = surface.Method(utilityType, "IsValidLoadoutHolder", new[] { typeof(Pawn) });
            getActiveStateMethod = surface.Method(utilityType, "GetActiveState", new[] { typeof(Pawn) });

            tagEditorCtor = surface.Constructor(tagEditorType, Type.EmptyTypes);
            loadoutEditorCtor = surface.Constructor(loadoutEditorType, new[] { typeof(Pawn) });

            // Signature lookups need every mod type resolved: a null in the array throws, and a
            // missing type has already made the surface not-ready.
            if (stateType != null && tagType != null && elementType != null && activeConditionType != null
                && itemType != null && itemMemberType != null)
            {
                Type stateList = typeof(List<>).MakeGenericType(stateType);
                Type tagList = typeof(List<>).MakeGenericType(tagType);

                componentAddTag = surface.Method(componentType, "AddTag",
                    new[] { tagType, stateType, activeConditionType, typeof(bool) });
                componentRemoveTag = surface.Method(componentType, "RemoveTag", new[] { elementType });
                elementActiveMethod = surface.Method(elementType, "Active", new[] { stateType });
                elementSetLoadoutState = surface.Method(elementType, "SetLoadoutState",
                    new[] { stateType, activeConditionType });

                managerAddTag = surface.Method(managerType, "AddTag", new[] { tagType });
                managerRemoveTag = surface.Method(managerType, "RemoveTag", new[] { tagType });
                managerRemoveState = surface.Method(managerType, "RemoveState", new[] { stateType });
                managerSetPanicState = surface.Method(managerType, "SetPanicState", new[] { stateType });

                setActiveStateMethod = surface.Method(utilityType, "SetActiveState",
                    new[] { typeof(Pawn), stateType, typeof(bool) });

                conditionStateActive = surface.Required("ActiveCondition.StateActive",
                    ParseEnum(activeConditionType, "StateActive"));
                conditionStateInactive = surface.Required("ActiveCondition.StateInactive",
                    ParseEnum(activeConditionType, "StateInactive"));

                tagEditorForTagCtor = surface.Constructor(tagEditorType, new[] { tagType });
                setTagLoadoutStateCtor = surface.Constructor(setTagLoadoutStateType, new[] { elementType });
                loadoutStateEditorCtor = surface.Constructor(loadoutStateEditorType, new[] { stateList });
                itemSpecifierCtor = surface.Constructor(itemSpecifierType, new[] { itemType, itemMemberType });
                tagSelectorCtor = surface.Constructor(tagSelectorType,
                    new[] { tagList, typeof(Action<>).MakeGenericType(tagType), typeof(bool), typeof(string) });
                wrapCallbackMethod = surface.Required("ClCompat.WrapCallback",
                    typeof(ClCompat).GetMethod("WrapCallback", AnyStatic)?.MakeGenericMethod(tagType));
            }

            ready = surface.Ready;
        }

        public static bool Ready
        {
            get { return ready; }
        }

        // ---- Types, for registrations and reflection in slice files ----

        public static Type LoadoutComponentType { get { return componentType; } }
        public static Type LoadoutType { get { return loadoutType; } }
        public static Type LoadoutElementType { get { return elementType; } }
        public static Type LoadoutStateType { get { return stateType; } }
        public static Type ActiveConditionType { get { return activeConditionType; } }
        public static Type TagType { get { return tagType; } }
        public static Type ItemType { get { return itemType; } }
        public static Type ItemMemberType { get { return itemMemberType; } }
        public static Type LoadoutManagerType { get { return managerType; } }
        public static Type UtilityType { get { return utilityType; } }

        public static Type LoadoutEditorWindowType { get { return loadoutEditorType; } }
        public static Type TagEditorWindowType { get { return tagEditorType; } }
        public static Type TagSelectorWindowType { get { return tagSelectorType; } }
        public static Type SetTagLoadoutStateWindowType { get { return setTagLoadoutStateType; } }
        public static Type LoadoutStateEditorWindowType { get { return loadoutStateEditorType; } }
        public static Type ItemSpecifierWindowType { get { return itemSpecifierType; } }
        public static Type ConfirmSettingsWindowType { get { return confirmSettingsType; } }
        public static Type OverviewWindowType { get { return overviewType; } }

        // ---- Pawns ----

        /// <summary>The mod's own gate for who carries a loadout; every loadout UI is hidden for anyone else.</summary>
        public static bool IsValidLoadoutHolder(Pawn pawn)
        {
            return ready && pawn != null
                && Guarded.Call(isValidLoadoutHolderMethod, null, "ClCompat.IsValidLoadoutHolder", pawn) is bool valid
                && valid;
        }

        public static ThingComp GetComponent(Pawn pawn)
        {
            if (!ready || pawn?.AllComps == null)
            {
                return null;
            }
            foreach (ThingComp comp in pawn.AllComps)
            {
                if (componentType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }
            return null;
        }

        public static object LoadoutOf(Pawn pawn)
        {
            return Guarded.Get<object>(componentLoadoutGetter, GetComponent(pawn), "ClCompat.LoadoutOf");
        }

        /// <summary>The pawn's current state, null for the default ("any") state.</summary>
        public static object ActiveState(Pawn pawn)
        {
            return ready && pawn != null ? Guarded.Call(getActiveStateMethod, null, "ClCompat.ActiveState", pawn) : null;
        }

        // ---- Loadouts and their elements ----

        /// <summary>Every element in priority order, active in the current state or not.</summary>
        public static List<object> Elements(object loadout)
        {
            return ToList(Guarded.FieldOf<IEnumerable>(loadoutElementsField, loadout, "ClCompat.Elements"));
        }

        public static object LoadoutCurrentState(object loadout)
        {
            return Guarded.Get<object>(loadoutCurrentStateGetter, loadout, "ClCompat.LoadoutCurrentState");
        }

        public static bool LoadoutInPanicMode(object loadout)
        {
            return Guarded.Get(loadoutInPanicModeGetter, loadout, "ClCompat.LoadoutInPanicMode", false);
        }

        public static object ElementTag(object element)
        {
            return Guarded.Get<object>(elementTagGetter, element, "ClCompat.ElementTag");
        }

        /// <summary>The state the element is conditioned on, null when it is active in every state.</summary>
        public static object ElementState(object element)
        {
            return Guarded.Get<object>(elementStateGetter, element, "ClCompat.ElementState");
        }

        /// <summary>True for "active when the state is active", false for "active when it is not".</summary>
        public static bool ElementActiveWhenStateActive(object element)
        {
            object condition = Guarded.Get<object>(elementConditionGetter, element, "ClCompat.ElementCondition");
            return condition == null || Equals(condition, conditionStateActive);
        }

        /// <summary>The mod's own condition label: "any", the state's name, or "not" plus the name.</summary>
        public static string ElementStateName(object element)
        {
            return Guarded.Get(elementStateNameGetter, element, "ClCompat.ElementStateName", "");
        }

        public static bool ElementActive(object element, object state)
        {
            return ready && element != null
                && Guarded.Call(elementActiveMethod, element, "ClCompat.ElementActive", state) is bool active
                && active;
        }

        // ---- States ----

        public static string StateName(object state)
        {
            return Guarded.FieldOf<string>(stateNameField, state, "ClCompat.StateName");
        }

        /// <summary>Every state the player has created, in the manager's order.</summary>
        public static List<object> AllStates()
        {
            return ManagerLoaded() ? ToList(Guarded.Get<IEnumerable>(managerStatesGetter, null, "ClCompat.AllStates")) : new List<object>();
        }

        /// <summary>The manager's live state list itself, the argument the state editor is constructed with.</summary>
        public static object StatesList()
        {
            return ManagerLoaded() ? Guarded.Get<object>(managerStatesGetter, null, "ClCompat.StatesList") : null;
        }

        public static object PanicState()
        {
            return ManagerLoaded() ? Guarded.Get<object>(managerPanicStateGetter, null, "ClCompat.PanicState") : null;
        }

        public static bool PanicActive()
        {
            return ManagerLoaded() && Guarded.Call(managerActivePanicState, null, "ClCompat.PanicActive") is bool active && active;
        }

        // ---- Tags ----

        public static List<object> AllTags()
        {
            return ManagerLoaded() ? ToList(Guarded.Get<IEnumerable>(managerTagsGetter, null, "ClCompat.AllTags")) : new List<object>();
        }

        public static string TagName(object tag)
        {
            return Guarded.FieldOf<string>(tagNameField, tag, "ClCompat.TagName");
        }

        public static List<object> TagItems(object tag)
        {
            return ToList(Guarded.FieldOf<IEnumerable>(tagItemsField, tag, "ClCompat.TagItems"));
        }

        // ---- Items ----

        /// <summary>The mod's full item label: stuff, def, and quality range.</summary>
        public static string ItemLabel(object item)
        {
            return Guarded.Get(itemLabelGetter, item, "ClCompat.ItemLabel", "");
        }

        /// <summary>The def's name, or "One of: A, B +n" for an item with alternatives.</summary>
        public static string ItemShortLabel(object item)
        {
            return Guarded.Get(itemShortLabelGetter, item, "ClCompat.ItemShortLabel", "");
        }

        public static int ItemQuantity(object item)
        {
            return Guarded.Get(itemQuantityGetter, item, "ClCompat.ItemQuantity", 0);
        }

        public static ThingDef ItemRepresentativeDef(object item)
        {
            return Guarded.Get<ThingDef>(itemRepresentativeDefGetter, item, "ClCompat.ItemRepresentativeDef");
        }

        public static bool ItemIsProsthetic(object item)
        {
            return Guarded.Get(itemIsProstheticGetter, item, "ClCompat.ItemIsProsthetic", false);
        }

        public static bool ItemIsApparel(object item)
        {
            return Guarded.Get(itemIsApparelGetter, item, "ClCompat.ItemIsApparel", false);
        }

        public static bool ItemHasAlternatives(object item)
        {
            return Guarded.Get(itemHasAlternativesGetter, item, "ClCompat.ItemHasAlternatives", false);
        }

        /// <summary>The item's members (its def and alternatives), best ranked first.</summary>
        public static List<object> ItemMembers(object item)
        {
            return ToList(Guarded.Get<IEnumerable>(itemMembersGetter, item, "ClCompat.ItemMembers"));
        }

        /// <summary>One line per rank, "1. A, B": the alternatives tooltip's body.</summary>
        public static string ItemRankedMembersDescription(object item)
        {
            return Guarded.Call(itemRankedMembersDescription, item, "ClCompat.ItemRankedMembersDescription") as string ?? "";
        }

        public static ThingDef MemberDef(object member)
        {
            return Guarded.Get<ThingDef>(memberDefGetter, member, "ClCompat.MemberDef");
        }

        public static string MemberLabel(object member)
        {
            return Guarded.Get(memberLabelGetter, member, "ClCompat.MemberLabel", "");
        }

        // ---- The mod's own mutators ----

        public static bool AddTag(Pawn pawn, object tag, object state, bool activeWhenStateActive, bool insertAtStart)
        {
            return Invoke(componentAddTag, GetComponent(pawn), "ClCompat.AddTag",
                tag, state, Condition(activeWhenStateActive), insertAtStart);
        }

        public static bool RemoveTag(Pawn pawn, object element)
        {
            return Invoke(componentRemoveTag, GetComponent(pawn), "ClCompat.RemoveTag", element);
        }

        /// <summary>The mod's state switch, which refuses (with its own message) while panic mode holds the pawn.</summary>
        public static bool SetActiveState(Pawn pawn, object state, bool immediatelyResolve)
        {
            return pawn != null && Invoke(setActiveStateMethod, null, "ClCompat.SetActiveState", pawn, state, immediatelyResolve);
        }

        public static bool SetElementState(object element, object state, bool activeWhenStateActive)
        {
            return Invoke(elementSetLoadoutState, element, "ClCompat.SetElementState", state, Condition(activeWhenStateActive));
        }

        public static object NewTag(string name)
        {
            return Construct(tagCtor, "ClCompat.NewTag", name);
        }

        public static object NewState(string name)
        {
            return Construct(stateCtor, "ClCompat.NewState", name);
        }

        public static bool ManagerAddTag(object tag)
        {
            return ManagerLoaded() && Invoke(managerAddTag, null, "ClCompat.ManagerAddTag", tag);
        }

        /// <summary>Deletes the tag everywhere: off every pawn, and suspending every bill that targeted it.</summary>
        public static bool ManagerRemoveTag(object tag)
        {
            return ManagerLoaded() && Invoke(managerRemoveTag, null, "ClCompat.ManagerRemoveTag", tag);
        }

        public static bool ManagerRemoveState(object state)
        {
            return ManagerLoaded() && Invoke(managerRemoveState, null, "ClCompat.ManagerRemoveState", state);
        }

        public static bool SetPanicState(object state)
        {
            return ManagerLoaded() && Invoke(managerSetPanicState, null, "ClCompat.SetPanicState", state);
        }

        // ---- The mod's windows ----

        public static Window OpenLoadoutEditor(Pawn pawn)
        {
            return Open(loadoutEditorCtor, "ClCompat.OpenLoadoutEditor", pawn);
        }

        /// <summary>The tag editor on the given tag, or on its own default selection when tag is null.</summary>
        public static Window OpenTagEditor(object tag)
        {
            return tag == null
                ? Open(tagEditorCtor, "ClCompat.OpenTagEditor")
                : Open(tagEditorForTagCtor, "ClCompat.OpenTagEditor", tag);
        }

        /// <summary>The state editor over the manager's live state list, as every mod call site opens it.</summary>
        public static Window OpenLoadoutStateEditor()
        {
            object states = StatesList();
            return states == null ? null : Open(loadoutStateEditorCtor, "ClCompat.OpenLoadoutStateEditor", states);
        }

        public static Window OpenSetTagLoadoutState(object element)
        {
            return Open(setTagLoadoutStateCtor, "ClCompat.OpenSetTagLoadoutState", element);
        }

        public static Window OpenItemSpecifier(object item, object member)
        {
            return Open(itemSpecifierCtor, "ClCompat.OpenItemSpecifier", item, member);
        }

        /// <summary>The tag picker over the given tags; onSelect receives each chosen tag.</summary>
        public static Window OpenTagSelector(IEnumerable<object> tags, Action<object> onSelect, bool multiSelect,
            string customTitleSuffix)
        {
            if (!ready || onSelect == null)
            {
                return null;
            }
            object callback = Guarded.Call(wrapCallbackMethod, null, "ClCompat.OpenTagSelector", onSelect);
            return callback == null
                ? null
                : Open(tagSelectorCtor, "ClCompat.OpenTagSelector", TypedList(tagType, tags), callback, multiSelect,
                    customTitleSuffix);
        }

        // ---- Plumbing ----

        /// <summary>A List&lt;T&gt; of a mod type, for mod signatures that take one.</summary>
        public static IList TypedList(Type elementType, IEnumerable<object> items)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
            if (items != null)
            {
                foreach (object item in items)
                {
                    list.Add(item);
                }
            }
            return list;
        }

        public static object Condition(bool activeWhenStateActive)
        {
            return activeWhenStateActive ? conditionStateActive : conditionStateInactive;
        }

        private static Action<T> WrapCallback<T>(Action<object> body)
        {
            return value => body(value);
        }

        /// <summary>The manager's statics deref its instance, which is null outside a loaded game.</summary>
        private static bool ManagerLoaded()
        {
            return ready && Guarded.FieldOf<object>(managerInstanceField, null, "ClCompat.ManagerInstance") != null;
        }

        private static bool Invoke(MethodInfo method, object instance, string context, params object[] args)
        {
            if (!ready || method == null || (instance == null && !method.IsStatic))
            {
                return false;
            }
            try
            {
                method.Invoke(instance, args);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError(context, ex);
                return false;
            }
        }

        private static object Construct(ConstructorInfo ctor, string context, params object[] args)
        {
            if (!ready || ctor == null)
            {
                return null;
            }
            try
            {
                return ctor.Invoke(args);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError(context, ex);
                return null;
            }
        }

        private static Window Open(ConstructorInfo ctor, string context, params object[] args)
        {
            if (!(Construct(ctor, context, args) is Window window))
            {
                return null;
            }
            Find.WindowStack.Add(window);
            return window;
        }

        private static List<object> ToList(IEnumerable source)
        {
            var list = new List<object>();
            if (source == null)
            {
                return list;
            }
            foreach (object item in source)
            {
                if (item != null)
                {
                    list.Add(item);
                }
            }
            return list;
        }

        private static object ParseEnum(Type enumType, string name)
        {
            try
            {
                return Enum.Parse(enumType, name);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static MethodInfo Getter(PropertyInfo property)
        {
            return property?.GetGetMethod(true);
        }
    }
}
