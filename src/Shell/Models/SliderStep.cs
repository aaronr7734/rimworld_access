using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard slider stepping shared by the capture engines
    /// (WidgetCapture.MaybeStepSlider, ListingRowCapture's slider tap).
    ///
    /// <see cref="Stepped"/> moves by exactly one step from the CURRENT value,
    /// clamped, and never snaps it onto a grid first (Colony
    /// Manager Redux's 0..3000 threshold slider starts at 500, which does not sit
    /// on this engine's own invented 150-unit step grid; the OLD algorithm derived
    /// every step from <paramref name="min"/> via <c>round((value-min)/step)</c>,
    /// which silently snapped 500 onto the nearest grid multiple (450) before
    /// stepping, so Right-then-Left landed on 450 rather than restoring 500 and the
    /// player could never get their original value back — recon-cmr.md, live
    /// trace: "500 -&gt; 300 -&gt; 450 -&gt; 600"). A slider genuinely IS free-form
    /// when it declares no <paramref name="roundTo"/> of its own; the step size
    /// below is this reader's OWN invention for keyboard reachability, not a grid
    /// the mod's value is expected to already sit on, so snapping onto it is never
    /// correct. <see cref="Snapped"/> is a distinct operation (quantizing an
    /// externally supplied value, e.g. a typed-in number, onto that same grid) and
    /// keeps the round-to-nearest-multiple behavior on purpose.
    ///
    /// Each stepped result is rounded onto a DECIMAL grid two orders finer than
    /// the step itself. That is what reconciles the two live bugs this class has
    /// eaten: raw <c>value + step</c> accumulates binary float dust a mod's own
    /// caption renders ("65.00001%" after thirteen 5% presses — live QA), while
    /// snapping to the STEP grid destroys off-grid starting values (the CMR 500).
    /// A grid two decimal orders below the step is far too fine to disturb any
    /// reachable value, yet every landing is decimal-clean, so one press from a
    /// dusty value still lands clean and every later press round-trips exactly.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class SliderStep
    {
        /// <summary>
        /// The value one keyboard step from <paramref name="value"/> in
        /// <paramref name="direction"/> (+1/-1), clamped to
        /// [<paramref name="min"/>, <paramref name="max"/>]. The step unit is
        /// <paramref name="roundTo"/> when the slider declares one (vanilla's
        /// own drag granularity). When it declares none (<paramref name="roundTo"/>
        /// &lt;= 0) the slider is continuous, but many mods draw an integer-valued
        /// slider that way and cast the float result to int themselves; there a
        /// twentieth-of-range step lands between the whole numbers we announce and
        /// can never reach them (a 1..64 range steps by 3.15, so 16 is
        /// unreachable). So when both bounds are whole numbers we step by a whole
        /// number — one at a time for ranges small enough to walk by key, a
        /// coarser whole step only for very large ranges. Genuinely fractional
        /// sliders (non-integer bounds) keep the twentieth-of-range default both
        /// engines have always used, as does one the caller proved <paramref name="fractional"/>.
        /// A <paramref name="grid"/> (the precision the owner stores at) outranks all but roundTo.
        /// </summary>
        public static float Stepped(float value, int direction, float min, float max, float roundTo, bool fractional = false, double grid = 0.0)
        {
            double step = ResolveStep(min, max, roundTo, fractional, grid);
            if (step <= 0.0)
            {
                return value;
            }
            double stepped = RoundToStepDecimals((double)value + direction * step, step);
            if (stepped < min)
            {
                stepped = min;
            }
            if (stepped > max)
            {
                stepped = max;
            }
            return (float)stepped;
        }

        /// <summary>
        /// Snap <paramref name="value"/> onto the same step grid <see cref="Stepped"/>'s
        /// size uses (the nearest multiple of that grid from <paramref name="min"/>),
        /// clamped to [<paramref name="min"/>, <paramref name="max"/>]. Used to set a
        /// slider to an exact keyboard-entered value without a second rounding rule of
        /// its own — a genuinely different operation from <see cref="Stepped"/> (which
        /// must NEVER quantize the slider's own current value; see the class remarks),
        /// since here the caller-supplied value is a fresh external input with no
        /// "current position" to preserve.
        /// </summary>
        public static float Snapped(float value, float min, float max, float roundTo, double grid = 0.0)
        {
            double step = ResolveStep(min, max, roundTo, false, grid);
            if (step <= 0.0)
            {
                return Math.Max(min, Math.Min(max, value));
            }
            double k = Math.Round(((double)value - (double)min) / step);
            double snapped = (double)min + k * step;
            if (snapped < min)
            {
                snapped = min;
            }
            if (snapped > max)
            {
                snapped = max;
            }
            return (float)snapped;
        }

        /// <summary>
        /// Rounds <paramref name="value"/> onto the decimal grid two orders of
        /// magnitude finer than <paramref name="step"/> (see the class remarks).
        /// A step of 0.05 rounds results to 4 decimals; a step of 150 to whole
        /// numbers. Clamped so enormous steps still round to whole numbers and
        /// microscopic steps never exceed double's meaningful decimal range.
        /// </summary>
        private static double RoundToStepDecimals(double value, double step)
        {
            int decimals = (int)Math.Ceiling(-Math.Log10(step)) + 2;
            if (decimals < 0)
            {
                decimals = 0;
            }
            if (decimals > 9)
            {
                decimals = 9;
            }
            return Math.Round(value, decimals);
        }

        private static double ResolveStep(float min, float max, float roundTo, bool fractional, double grid)
        {
            if (roundTo > 0f)
            {
                return roundTo;
            }
            double range = (double)max - (double)min;
            if (grid > 0.0)
            {
                return GridStep(range, grid);
            }
            bool integerBounds = !fractional
                && Math.Abs(min - Math.Round((double)min)) < 1e-6
                && Math.Abs(max - Math.Round((double)max)) < 1e-6;
            // Range >= 2 is what tells an integer COUNT (e.g. 1..64 settlements)
            // apart from a fractional slider that merely has whole-number
            // bounds -- most notably a 0..1 factor/percent, which must keep its
            // fine twentieth-of-range step, not snap to whole numbers.
            if (integerBounds && range >= 2.0)
            {
                return GridStep(range, 1.0);
            }
            return range / 20.0;
        }

        /// <summary>One grid unit up to 500 stops, else a whole multiple of it near a twentieth of the range.</summary>
        private static double GridStep(double range, double grid)
        {
            double stops = range / grid;
            if (stops <= 500.0)
            {
                return grid;
            }
            return grid * Math.Max(1.0, Math.Round(stops / 20.0));
        }
    }
}
