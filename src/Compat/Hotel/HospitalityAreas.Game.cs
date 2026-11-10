using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The cells of Hospitality's guest area strips (GuestUtility.DoAreaSelector, AreaGUI) and the
    /// labels each strip draws on them, resolved against the consumer's own reflection surface.
    /// </summary>
    internal sealed class HospitalityAreas
    {
        private readonly MethodInfo getAreas;       // GuestUtility.GetAreas(Map)
        private readonly MethodInfo shoppingLabel;  // GenericUtility.GetShoppingLabel(Area)

        public HospitalityAreas(ReflectionSurface surface)
        {
            Type guestUtility = surface.Type("Hospitality.Utilities.GuestUtility");
            Type genericUtility = surface.Type("Hospitality.Utilities.GenericUtility");
            getAreas = surface.Method(guestUtility, "GetAreas", new[] { typeof(Map) });
            shoppingLabel = surface.Method(genericUtility, "GetShoppingLabel", new[] { typeof(Area) });
        }

        /// <summary>The strip's cells in its own order: none first, then GuestUtility.GetAreas.</summary>
        public List<Area> Choices(Map map)
        {
            var areas = new List<Area> { null };
            if (map != null && getAreas.Invoke(null, new object[] { map }) is IEnumerable mapAreas)
            {
                areas.AddRange(mapAreas.OfType<Area>());
            }
            return areas;
        }

        /// <summary>The accommodation strip's label, AreaGUI's AreaUtility.AreaAllowedLabel_Area.</summary>
        public static string StayLabel(Area area)
        {
            return AreaUtility.AreaAllowedLabel_Area(area).StripTags();
        }

        public string ShoppingLabel(Area area)
        {
            return ((string)shoppingLabel.Invoke(null, new object[] { area })).StripTags();
        }
    }
}
