using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Gastronomy's Dining tab on a cash register
    /// (Gastronomy.Restaurant.ITab_Register_Restaurant). Rows follow the tab's left column top to
    /// bottom (restaurant picker and its add, remove and rename icons, the five settings
    /// checkboxes, the guest price slider, then every stats line), and its right column becomes one
    /// row opening the menu filter in the shared thing-filter screen. The tab's restaurant is the one
    /// linked to the register (FillTab re-reads it every frame), so picking one links it through the
    /// mod's own RestaurantController.LinkRegister, exactly SetRestaurant's write.
    /// </summary>
    internal sealed class GastronomyRestaurantTabAdapter : InspectNodeAdapter
    {
        // DrawStock draws at most this many stock icons.
        private const int StockIconCap = 16;

        private readonly Type registerType;
        private readonly Type renameDialogType;
        private readonly MethodInfo getRestaurant;     // RestaurantUtility.GetRestaurant(Building_CashRegister)
        private readonly MethodInfo getAll;            // RestaurantUtility.GetAllRestaurants(Thing)
        private readonly MethodInfo getManager;        // RestaurantUtility.GetRestaurantsManager(RestaurantController)
        private readonly MethodInfo getPrice;          // RestaurantUtility.GetPrice(ThingDef, RestaurantController)
        private readonly MethodInfo linkRegister;      // RestaurantController.LinkRegister(Building_CashRegister)
        private readonly MethodInfo addRestaurant;     // RestaurantsManager.AddRestaurant()
        private readonly MethodInfo deleteRestaurant;  // RestaurantsManager.DeleteRestaurant(RestaurantController)
        private readonly MethodInfo getMenuFilters;    // RestaurantMenu.GetMenuFilters(out ThingFilter, out ThingFilter)
        private readonly PropertyInfo nameProp;
        private readonly PropertyInfo activeStaff;
        private readonly PropertyInfo patrons;
        private readonly PropertyInfo seats;
        private readonly PropertyInfo ordersProp;
        private readonly PropertyInfo debtsProp;
        private readonly PropertyInfo stockProp;
        private readonly PropertyInfo menuProp;
        private readonly PropertyInfo allOrders;
        private readonly PropertyInfo ordersForServing;
        private readonly PropertyInfo ordersForCooking;
        private readonly PropertyInfo allDebts;
        private readonly PropertyInfo allStock;
        private readonly FieldInfo restaurantsField;
        private readonly FieldInfo openForBusiness;
        private readonly FieldInfo allowGuests;
        private readonly FieldInfo allowColonists;
        private readonly FieldInfo allowPrisoners;
        private readonly FieldInfo allowSlaves;
        private readonly FieldInfo guestPrice;
        private readonly FieldInfo incomeYesterday;
        private readonly FieldInfo incomeToday;
        private readonly FieldInfo orderDef;
        private readonly FieldInfo orderPatron;
        private readonly FieldInfo debtPatron;
        private readonly FieldInfo debtAmount;
        private readonly FieldInfo stockDef;
        private readonly FieldInfo stockItems;

        private readonly bool ready;
        private InspectTabBase sharedTab;

        public override bool Ready => ready;

        private GastronomyRestaurantTabAdapter()
        {
            var surface = new ReflectionSurface("GastronomyRestaurantTabAdapter");
            registerType = surface.Type("CashRegister.Building_CashRegister");
            renameDialogType = surface.Type("Gastronomy.Restaurant.Dialog_RenameRestaurant");
            Type utility = surface.Type("Gastronomy.Restaurant.RestaurantUtility");
            Type controller = surface.Type("Gastronomy.Restaurant.RestaurantController");
            Type manager = surface.Type("Gastronomy.Restaurant.RestaurantsManager");
            Type menu = surface.Type("Gastronomy.Restaurant.RestaurantMenu");
            Type orders = surface.Type("Gastronomy.Restaurant.RestaurantOrders");
            Type order = surface.Type("Gastronomy.Restaurant.Order");
            Type debts = surface.Type("Gastronomy.Restaurant.RestaurantDebt");
            Type debt = surface.Type("Gastronomy.Restaurant.Debt");
            Type stock = surface.Type("Gastronomy.Restaurant.RestaurantStock");
            Type stockEntry = surface.Type("Gastronomy.Restaurant.RestaurantStock+Stock");

            getRestaurant = surface.Method(utility, "GetRestaurant", new[] { registerType });
            getAll = surface.Method(utility, "GetAllRestaurants", new[] { typeof(Thing) });
            getManager = surface.Method(utility, "GetRestaurantsManager", new[] { controller });
            getPrice = surface.Method(utility, "GetPrice", new[] { typeof(ThingDef), controller });
            linkRegister = surface.Method(controller, "LinkRegister", new[] { registerType });
            addRestaurant = surface.Method(manager, "AddRestaurant", Type.EmptyTypes);
            deleteRestaurant = surface.Method(manager, "DeleteRestaurant", new[] { controller });
            getMenuFilters = surface.Method(menu, "GetMenuFilters",
                new[] { typeof(ThingFilter).MakeByRefType(), typeof(ThingFilter).MakeByRefType() });
            nameProp = surface.Property(controller, "Name");
            activeStaff = surface.Property(controller, "ActiveStaff");
            patrons = surface.Property(controller, "Patrons");
            seats = surface.Property(controller, "Seats");
            ordersProp = surface.Property(controller, "Orders");
            debtsProp = surface.Property(controller, "Debts");
            stockProp = surface.Property(controller, "Stock");
            menuProp = surface.Property(controller, "Menu");
            allOrders = surface.Property(orders, "AllOrders");
            ordersForServing = surface.Property(orders, "AvailableOrdersForServing");
            ordersForCooking = surface.Property(orders, "AvailableOrdersForCooking");
            allDebts = surface.Property(debts, "AllDebts");
            allStock = surface.Property(stock, "AllStock");
            restaurantsField = surface.Field(manager, "restaurants");
            openForBusiness = surface.Field(controller, "openForBusiness");
            allowGuests = surface.Field(controller, "allowGuests");
            allowColonists = surface.Field(controller, "allowColonists");
            allowPrisoners = surface.Field(controller, "allowPrisoners");
            allowSlaves = surface.Field(controller, "allowSlaves");
            guestPrice = surface.Field(controller, "guestPricePercentage");
            incomeYesterday = surface.Field(debts, "incomeYesterday");
            incomeToday = surface.Field(debts, "incomeToday");
            orderDef = surface.Field(order, "consumableDef");
            orderPatron = surface.Field(order, "patron");
            debtPatron = surface.Field(debt, "patron");
            debtAmount = surface.Field(debt, "amount");
            stockDef = surface.Field(stockEntry, "def");
            stockItems = surface.Field(stockEntry, "items");
            ready = surface.Ready;
        }

        public static void TryRegister()
        {
            CompatRegistration.TabAdapter("Gastronomy.Restaurant.ITab_Register_Restaurant",
                t => new GastronomyRestaurantTabAdapter { sharedTab = InspectTabManager.GetSharedInstance(t) },
                "Gastronomy restaurant tab compat");
        }

        // Dispatch token, never displayed raw.
        public override string CategoryKey => "Gastronomy Restaurant";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => TabName();

        public override string CategoryDisplayName(object obj) => TabName();

        private static string TabName()
        {
            return CompatText.ResolveOrFallback("TabRegisterRestaurant", "RimWorldAccess.Compat.Gastronomy.TabName");
        }

        public override bool CanExpand(object obj)
        {
            return ready && registerType.IsInstanceOfType(obj);
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0 || !registerType.IsInstanceOfType(obj))
            {
                return;
            }
            InspectNodeFactory.GuardedBuild("GastronomyRestaurantTabAdapter", () =>
            {
                Thing register = (Thing)obj;
                object restaurant = Restaurant(register);
                if (restaurant == null)
                {
                    return;
                }
                BuildSelection(categoryItem, register, restaurant, mode);
                BuildSettings(categoryItem, restaurant, mode);
                BuildStats(categoryItem, register, restaurant, mode);
                BuildMenu(categoryItem, restaurant, mode);
            });
        }

        // FillTab's own fallback: the linked restaurant, else the map's first.
        private object Restaurant(Thing register)
        {
            object linked = getRestaurant.Invoke(null, new object[] { register });
            if (linked != null)
            {
                return linked;
            }
            var all = getAll.Invoke(null, new object[] { register }) as IList;
            return all != null && all.Count > 0 ? all[0] : null;
        }

        private string NameOf(object restaurant)
        {
            return ((string)nameProp.GetValue(restaurant, null)).CapitalizeFirst();
        }

        private void BuildSelection(InspectionTreeItem parent, Thing register, object restaurant, InspectionMode mode)
        {
            object manager = getManager.Invoke(null, new[] { restaurant });
            IList all = (IList)restaurantsField.GetValue(manager);

            HotelTreeRows.Combo(parent, "RimWorldAccess.Compat.Gastronomy.RestaurantPicker".Translate(),
                () => NameOf(Restaurant(register)), CompatText.ModTip("RestaurantTooltipSelect"),
                () => RestaurantOptions(parent, register, all, mode), mode);

            if (all.Count > 1)
            {
                HotelTreeRows.Button(parent, CompatText.Flatten(CompatText.ModText("RestaurantTooltipRemove")), null,
                    () => OnRemove(parent, register, manager, restaurant, mode), mode).OpensOverlayMenu = true;
            }
            HotelTreeRows.Button(parent, CompatText.Flatten(CompatText.ModText("RestaurantTooltipAdd")), null,
                () => OnAdd(parent, register, manager, mode), mode);
            HotelTreeRows.Button(parent, CompatText.Flatten(CompatText.ModText("RestaurantTooltipRename")), null,
                () => Find.WindowStack.Add((Window)Activator.CreateInstance(renameDialogType, restaurant)), mode).OpensOverlayMenu = true;
        }

        private List<FloatMenuOption> RestaurantOptions(InspectionTreeItem categoryItem, Thing register, IList all, InspectionMode mode)
        {
            var options = new List<FloatMenuOption>(all.Count);
            foreach (object restaurant in all)
            {
                object chosen = restaurant;
                options.Add(new FloatMenuOption(NameOf(chosen), () =>
                {
                    Guarded.Call(linkRegister, chosen, "GastronomyRestaurantTabAdapter link", register);
                    Rebuild(categoryItem, register, mode);
                }));
            }
            return options;
        }

        private void OnAdd(InspectionTreeItem categoryItem, Thing register, object manager, InspectionMode mode)
        {
            try
            {
                object added = addRestaurant.Invoke(manager, null);
                linkRegister.Invoke(added, new object[] { register });
                Rebuild(categoryItem, register, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Gastronomy.RestaurantAdded".Loc(NameOf(added)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"GastronomyRestaurantTabAdapter add failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Hospitality.ActionFailed".Loc());
            }
        }

        // MUTATION-C: mirrors ITab_Register_Restaurant.DrawRestaurantSelection's minus button, whose
        // Dialog_Confirm callback is an inline closure (DeleteRestaurant, then SetRestaurant on the last).
        private void OnRemove(InspectionTreeItem categoryItem, Thing register, object manager, object restaurant, InspectionMode mode)
        {
            string text = CompatText.ModArgs("RestaurantDialogConfirmRemove", (string)nameProp.GetValue(restaurant, null));
            Find.WindowStack.Add(new Dialog_Confirm(text, () =>
            {
                deleteRestaurant.Invoke(manager, new[] { restaurant });
                IList all = (IList)restaurantsField.GetValue(manager);
                linkRegister.Invoke(all[all.Count - 1], new object[] { register });
                Rebuild(categoryItem, register, mode);
            }));
        }

        private void BuildSettings(InspectionTreeItem parent, object restaurant, InspectionMode mode)
        {
            AddToggle(parent, restaurant, openForBusiness, "TabRegisterOpened", mode);
            AddToggle(parent, restaurant, allowGuests, "TabRegisterGuests", mode);
            AddToggle(parent, restaurant, allowColonists, "TabRegisterColonists", mode);
            AddToggle(parent, restaurant, allowPrisoners, "TabRegisterPrisoners", mode);
            if (ModsConfig.IdeologyActive)
            {
                AddToggle(parent, restaurant, allowSlaves, "TabRegisterSlaves", mode);
            }

            HotelTreeRows.Slider(parent, () => PriceLabel(restaurant), CompatText.ModTip("TabRegisterGuestPriceTooltip"),
                () => (float)guestPrice.GetValue(restaurant), v => SetPrice(restaurant, v), 0f, 5f, 0.1f, mode);
        }

        private static void AddToggle(InspectionTreeItem parent, object restaurant, FieldInfo field, string key, InspectionMode mode)
        {
            // MUTATION-C: mirrors DrawSettings, whose Listing_Standard.CheckboxLabeled rows write the
            // controller's public bools by ref; RestaurantController has no setter for any of them.
            HotelTreeRows.Checkbox(parent, CompatText.ModText(key), CompatText.ModTip(key + "Tooltip"),
                () => (bool)field.GetValue(restaurant), v => field.SetValue(restaurant, v), mode);
        }

        // MUTATION-C: mirrors DrawPrice, which assigns the slider's result straight to guestPricePercentage.
        private void SetPrice(object restaurant, float value)
        {
            guestPrice.SetValue(restaurant, value);
        }

        private string PriceLabel(object restaurant)
        {
            float price = (float)guestPrice.GetValue(restaurant);
            string text = price <= 0f ? CompatText.ModText("TabRegisterGuestPriceFree") : price.ToStringPercentEmptyZero();
            return CompatText.ModArgs("TabRegisterGuestPrice", text);
        }

        private void BuildStats(InspectionTreeItem parent, Thing register, object restaurant, InspectionMode mode)
        {
            HotelTreeRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterActiveStaff", Pawns(activeStaff, restaurant).Count.ToString()),
                () => Names(Pawns(activeStaff, restaurant)));
            HotelTreeRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterSeats", seats.GetValue(restaurant, null).ToString()));
            HotelTreeRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterPatrons", Pawns(patrons, restaurant).Count.ToString()),
                () => Names(Pawns(patrons, restaurant)));
            AddOrders(parent, restaurant, allOrders, "TabRegisterTotalOrders");
            AddOrders(parent, restaurant, ordersForServing, "TabRegisterNeedsServing");
            AddOrders(parent, restaurant, ordersForCooking, "TabRegisterNeedsCooking");
            HotelTreeRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterStocked", StockTotal(restaurant).ToString()),
                () => StockText(restaurant));
            object debts = debtsProp.GetValue(restaurant, null);
            HotelTreeRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterEarnedYesterday", ((float)incomeYesterday.GetValue(debts)).ToStringMoney()));
            HotelTreeRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterEarnedToday", ((float)incomeToday.GetValue(debts)).ToStringMoney()));
            BuildDebts(parent, register, restaurant, debts, mode);
        }

        private List<Pawn> Pawns(PropertyInfo property, object restaurant)
        {
            var list = new List<Pawn>();
            if (property.GetValue(restaurant, null) is IEnumerable pawns)
            {
                list.AddRange(pawns.OfType<Pawn>());
            }
            return list;
        }

        private static string Names(List<Pawn> pawns)
        {
            return pawns.Select(p => p.LabelShort).ToCommaList();
        }

        // DrawOrders: the count, then one icon per ordered meal whose tooltip names the patrons.
        private void AddOrders(InspectionTreeItem parent, object restaurant, PropertyInfo source, string key)
        {
            HotelTreeRows.Info(parent, () => HotelTreeRows.StatLine(key, Orders(restaurant, source).Count.ToString()), () =>
            {
                var groups = new List<string>();
                foreach (var group in Orders(restaurant, source).GroupBy(o => (ThingDef)orderDef.GetValue(o)))
                {
                    if (group.Key == null)
                    {
                        continue;
                    }
                    string patronsText = group.Select(o => ((Pawn)orderPatron.GetValue(o))?.Name?.ToStringShort).ToCommaList();
                    groups.Add(HotelTreeRows.LabeledValue(group.Key.LabelCap.ToString(), patronsText));
                }
                return CompatText.JoinSentences(groups);
            });
        }

        private List<object> Orders(object restaurant, PropertyInfo source)
        {
            object orders = ordersProp.GetValue(restaurant, null);
            return source.GetValue(orders, null) is IEnumerable items ? items.Cast<object>().ToList() : new List<object>();
        }

        private IEnumerable<object> StockEntries(object restaurant)
        {
            object stock = stockProp.GetValue(restaurant, null);
            if (!(allStock.GetValue(stock, null) is IEnumerable pairs))
            {
                yield break;
            }
            foreach (object pair in pairs)
            {
                yield return pair.GetType().GetProperty("Value").GetValue(pair, null);
            }
        }

        private int StockTotal(object restaurant)
        {
            return StockEntries(restaurant).Sum(e => ((IList)stockItems.GetValue(e)).Cast<Thing>().Sum(t => t.stackCount));
        }

        // DrawStock: the first entries of AllStock, one icon each, tooltip "Nx Label (price)".
        private string StockText(object restaurant)
        {
            var entries = new List<string>();
            foreach (object entry in StockEntries(restaurant).Take(StockIconCap))
            {
                var def = (ThingDef)stockDef.GetValue(entry);
                var items = ((IList)stockItems.GetValue(entry)).Cast<Thing>().ToList();
                if (def == null || items.Count == 0)
                {
                    continue;
                }
                string price = ((float)getPrice.Invoke(null, new[] { def, restaurant })).ToStringMoney();
                entries.Add("RimWorldAccess.Compat.Gastronomy.StockEntry".Translate(items.Sum(t => t.stackCount), def.LabelCap.ToString(), price));
            }
            return CompatText.JoinSentences(entries);
        }

        // DrawDebts: the unpaid total, then one silver icon per debt whose click clears it.
        private void BuildDebts(InspectionTreeItem parent, Thing register, object restaurant, object debts, InspectionMode mode)
        {
            var open = new List<object>();
            if (allDebts.GetValue(debts, null) is IEnumerable all)
            {
                foreach (object debt in all)
                {
                    open.Add(debt);
                }
            }
            string total = open.Sum(d => (float)debtAmount.GetValue(d)).ToStringMoney();
            InspectNodeFactory.Section(parent, HotelTreeRows.StatLine("TabRegisterDebts", total), debts, section =>
            {
                foreach (object debt in open)
                {
                    var patron = (Pawn)debtPatron.GetValue(debt);
                    float amount = (float)debtAmount.GetValue(debt);
                    if (patron == null || amount <= 0f)
                    {
                        continue;
                    }
                    object captured = debt;
                    HotelTreeRows.Button(section, CompatText.ModArgs("TabRegisterDebtTooltip", patron.Name.ToStringFull, amount.ToStringMoney()),
                        null, () => OnClearDebt(parent, register, captured, mode), mode);
                }
                if (section.Children.Count == 0)
                {
                    InspectNodeFactory.DetailLine(section, "RimWorldAccess.Compat.Gastronomy.NoDebts".Translate());
                }
            });
        }

        // MUTATION-C: mirrors DrawDebts' icon click, an inline `debt.amount = 0f` with no method behind it.
        private void OnClearDebt(InspectionTreeItem categoryItem, Thing register, object debt, InspectionMode mode)
        {
            debtAmount.SetValue(debt, 0f);
            SoundDefOf.Click.PlayOneShotOnCamera();
            Rebuild(categoryItem, register, mode);
            TolkHelper.Speak("RimWorldAccess.Compat.Gastronomy.DebtCleared".Loc());
        }

        private void BuildMenu(InspectionTreeItem parent, object restaurant, InspectionMode mode)
        {
            if (mode == InspectionMode.ReadOnly)
            {
                return;
            }
            InspectNodeFactory.ActionRow(parent, "RimWorldAccess.Compat.Gastronomy.MenuFilter".Translate(), restaurant, () =>
            {
                object menu = menuProp.GetValue(restaurant, null);
                object[] args = { null, null };
                getMenuFilters.Invoke(menu, args);
                // DrawRight's own call: hit points hidden, AllowFresh force-hidden.
                ThingFilterMenuState.Open((ThingFilter)args[0], (ThingFilter)args[1], TabName(),
                    forceHideHitPointsConfig: true, forceHiddenFilters: new[] { SpecialThingFilterDefOf.AllowFresh });
            }, opensOverlayMenu: true);
        }

        private void Rebuild(InspectionTreeItem categoryItem, Thing register, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, register, mode, this, sharedTab);
        }
    }
}
