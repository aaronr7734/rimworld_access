using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Turns a raw <see cref="Color"/> into a human-readable name for announcement. RimWorld stores
    /// many colors (hair, apparel, skin) as raw RGB rather than as <see cref="ColorDef"/>s, so to
    /// announce a current color we reverse-map it back to the closest named <see cref="ColorDef"/>.
    /// Falls back to the nearest named color, then to a hex code if nothing is close.
    /// </summary>
    public static class ColorNameHelper
    {
        // Cached snapshot of every named color def, built once. ColorDefs are static game data.
        private static List<ColorDef> namedColors;

        private static List<ColorDef> NamedColors
        {
            get
            {
                if (namedColors == null)
                {
                    namedColors = DefDatabase<ColorDef>.AllDefs
                        .Where(c => !c.label.NullOrEmpty())
                        .ToList();
                }
                return namedColors;
            }
        }

        /// <summary>
        /// Best-effort spoken name for a color. Exact (indistinguishable) ColorDef match wins;
        /// otherwise the nearest named color by RGB distance; otherwise a hex code.
        /// </summary>
        public static string NameForColor(Color color)
        {
            // Exact match first — this is how the game itself compares swatches.
            foreach (ColorDef def in NamedColors)
            {
                if (color.IndistinguishableFrom(def.color))
                    return def.LabelCap.ToString();
            }

            // Nearest named color by squared RGB distance.
            ColorDef nearest = null;
            float bestDist = float.MaxValue;
            foreach (ColorDef def in NamedColors)
            {
                float dr = color.r - def.color.r;
                float dg = color.g - def.color.g;
                float db = color.b - def.color.b;
                float dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = def;
                }
            }

            if (nearest != null)
                return nearest.LabelCap.ToString();

            // Last resort: hex.
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        /// <summary>
        /// Names a whole swatch list, disambiguating colors that resolve to the same name. The game
        /// ships several distinct shades sharing one label (three "Purple"s, etc.); a screen reader
        /// can't tell them apart by name alone, so collisions get a trailing number ordered darkest
        /// to lightest ("Purple 1" = darkest). Unique names are left untouched.
        /// </summary>
        public static List<string> NamesForColors(List<Color> colors)
        {
            var names = colors.Select(NameForColor).ToList();

            var collisions = names
                .Select((name, index) => new { name, index })
                .GroupBy(x => x.name)
                .Where(g => g.Count() > 1);

            foreach (var group in collisions)
            {
                var orderedByLuminance = group
                    .OrderBy(x => Luminance(colors[x.index]))
                    .ToList();
                for (int i = 0; i < orderedByLuminance.Count; i++)
                    names[orderedByLuminance[i].index] = $"{orderedByLuminance[i].name} {i + 1}";
            }

            return names;
        }

        // Perceived brightness (Rec. 601). Used only to order same-named shades deterministically.
        private static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
