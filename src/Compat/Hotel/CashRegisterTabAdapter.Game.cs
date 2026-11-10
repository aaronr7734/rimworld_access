using System;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The plumbing the suite's register tabs (Gastronomy's Dining, Storefront's Shop) share: a tab
    /// on Building_CashRegister named by the mod's own key, built once per category expansion.
    /// </summary>
    internal abstract class CashRegisterTabAdapter : InspectNodeAdapter
    {
        private readonly string tabKey;
        private readonly string fallbackKey;

        // Set by the subclass constructor from its reflection surface.
        protected Type registerType;
        protected bool ready;

        protected CashRegisterTabAdapter(string tabKey, string fallbackKey)
        {
            this.tabKey = tabKey;
            this.fallbackKey = fallbackKey;
        }

        public override bool Ready => ready;

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => TabName();

        public override string CategoryDisplayName(object obj) => TabName();

        protected string TabName()
        {
            return CompatText.ResolveOrFallback(tabKey, fallbackKey);
        }

        public override bool CanExpand(object obj)
        {
            return ready && registerType.IsInstanceOfType(obj);
        }

        public sealed override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0 || !registerType.IsInstanceOfType(obj))
            {
                return;
            }
            InspectNodeFactory.GuardedBuild(GetType().Name, () => Build(categoryItem, (Thing)obj, mode));
        }

        protected abstract void Build(InspectionTreeItem categoryItem, Thing register, InspectionMode mode);
    }
}
