namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A captured row's screen rect, described without any GUI types, with the placement tests
    /// the generic reader's caption rules share.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public struct LineBox
    {
        /// <summary>Tolerance for a box drawn a pixel past an edge it is meant to meet.</summary>
        public const float EdgeSlack = 2f;

        public float X;
        public float Y;
        public float Width;
        public float Height;

        public float XMax => X + Width;
        public float YMax => Y + Height;
        public float CenterX => X + Width / 2f;
        public float CenterY => Y + Height / 2f;

        /// <summary>This box's vertical centre lies within <paramref name="line"/>'s height.</summary>
        public bool OnLineOf(LineBox line)
        {
            return CenterY >= line.Y && CenterY <= line.YMax;
        }

        /// <summary>Horizontally within <paramref name="outer"/>, give or take <see cref="EdgeSlack"/>.</summary>
        public bool Inside(LineBox outer)
        {
            return X >= outer.X - EdgeSlack && XMax <= outer.XMax + EdgeSlack;
        }

        /// <summary>The two boxes share some horizontal span.</summary>
        public bool OverlapsColumn(LineBox other)
        {
            return X < other.XMax && other.X < XMax;
        }

        /// <summary>Distance from <paramref name="above"/>'s bottom edge down to this box's top; negative when they overlap.</summary>
        public float GapBelow(LineBox above)
        {
            return Y - above.YMax;
        }

        /// <summary>Distance from <paramref name="left"/>'s right edge across to this box's left; negative when they overlap.</summary>
        public float GapRight(LineBox left)
        {
            return X - left.XMax;
        }

        /// <summary>The point lies in the box, right and bottom edges excluded.</summary>
        public bool Contains(float x, float y)
        {
            return x >= X && x < XMax && y >= Y && y < YMax;
        }
    }
}
