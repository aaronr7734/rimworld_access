using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Hospitality: Storefront's Shop tab on a cash register
    /// (Storefront.Store.ITab_Register_Store). Rows follow the tab's left column (store name and its
    /// rename icon, the Opened checkbox, the stats box, the stock list), and its right column becomes
    /// one row opening the store filter in the shared thing-filter screen.
    /// </summary>
    internal sealed class StorefrontStoreTabAdapter : CashRegisterTabAdapter
    {
        private readonly Type renameDialogType;
        private readonly MethodInfo getStore;        // StoreUtility.GetStore(Building_CashRegister)
        private readonly MethodInfo getStoreFilter;  // StoreController.GetStoreFilter()
        private readonly PropertyInfo nameProp;
        private readonly PropertyInfo activeStaff;
        private readonly PropertyInfo customers;
        private readonly PropertyInfo stockProp;
        private readonly FieldInfo openForBusiness;
        private readonly FieldInfo incomeYesterday;
        private readonly FieldInfo incomeToday;

        private StorefrontStoreTabAdapter()
            : base("TabRegisterStore", "RimWorldAccess.Compat.Storefront.TabName")
        {
            var surface = new ReflectionSurface("StorefrontStoreTabAdapter");
            registerType = surface.Type("CashRegister.Building_CashRegister");
            renameDialogType = surface.Type("Storefront.Store.Dialog_RenameStore");
            Type utility = surface.Type("Storefront.Store.StoreUtility");
            Type controller = surface.Type("Storefront.Store.StoreController");
            getStore = surface.Method(utility, "GetStore", new[] { registerType });
            getStoreFilter = surface.Method(controller, "GetStoreFilter", Type.EmptyTypes);
            nameProp = surface.Property(controller, "Name");
            activeStaff = surface.Property(controller, "ActiveStaff");
            customers = surface.Property(controller, "Customers");
            stockProp = surface.Property(controller, "Stock");
            openForBusiness = surface.Field(controller, "openForBusiness");
            incomeYesterday = surface.Field(controller, "incomeYesterday");
            incomeToday = surface.Field(controller, "incomeToday");
            ready = surface.Ready;
        }

        public static void TryRegister()
        {
            CompatRegistration.TabAdapter("Storefront.Store.ITab_Register_Store",
                t => new StorefrontStoreTabAdapter(), "Storefront store tab compat");
        }

        // Dispatch token, never displayed raw.
        public override string CategoryKey => "Storefront Store";

        protected override void Build(InspectionTreeItem categoryItem, Thing register, InspectionMode mode)
        {
            object store = getStore.Invoke(null, new object[] { register });
            if (store == null)
            {
                return;
            }
            InspectRows.Info(categoryItem, () => ((string)nameProp.GetValue(store, null)).CapitalizeFirst());
            HotelTreeRows.RenameButton(categoryItem, "StoreTooltipRename", renameDialogType, store, mode);

            // MUTATION-C: mirrors ITab_Register_Store.DrawSettings, whose Listing_Standard.CheckboxLabeled
            // writes StoreController.openForBusiness by ref; the controller has no setter.
            InspectRows.Checkbox(categoryItem, CompatText.ModText("TabRegisterOpened"), CompatText.ModTip("TabRegisterOpenedTooltip"),
                () => (bool)openForBusiness.GetValue(store), v => openForBusiness.SetValue(store, v), mode);

            BuildStats(categoryItem, store);
            InspectRows.Info(categoryItem, () => StockText(store));
            HotelTreeRows.FilterRow(categoryItem, "RimWorldAccess.Compat.Storefront.StoreFilter".Translate(), store,
                () => ThingFilterMenuState.Open((ThingFilter)getStoreFilter.Invoke(store, null), null, TabName()), mode);
        }

        private void BuildStats(InspectionTreeItem parent, object store)
        {
            // DrawStats names only the first active staff member.
            InspectRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterActiveStaff",
                HotelTreeRows.Pawns(activeStaff, store).FirstOrDefault()?.LabelShort ?? ""));
            InspectRows.Info(parent, () => HotelTreeRows.StatLine("TabRegisterCustomers", HotelTreeRows.Pawns(customers, store).Count.ToString()),
                () => HotelTreeRows.Names(HotelTreeRows.Pawns(customers, store)));
            HotelTreeRows.IncomeRows(parent, store, incomeYesterday, incomeToday);
        }

        // DrawStock: one "Nx Label" per def, minified buildings counted as what they contain.
        private string StockText(object store)
        {
            var stock = stockProp.GetValue(store, null) is IEnumerable things ? things.OfType<Thing>().ToList() : new List<Thing>();
            var entries = new List<string>();
            foreach (var group in stock.GroupBy(t => t.GetInnerIfMinified().def ?? t.def))
            {
                if (group.Key != null)
                {
                    entries.Add("RimWorldAccess.Compat.Storefront.StockEntry".Translate(group.Sum(t => t.stackCount), group.Key.LabelCap.ToString()));
                }
            }
            return entries.Count == 0
                ? (string)"RimWorldAccess.Compat.Storefront.NoStock".Translate()
                : "RimWorldAccess.Compat.Storefront.StockLine".Translate(entries.ToCommaList()).ToString();
        }
    }
}
