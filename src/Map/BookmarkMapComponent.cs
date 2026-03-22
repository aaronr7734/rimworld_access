using Verse;

namespace RimWorldAccess
{
    public class BookmarkMapComponent : MapComponent
    {
        private IntVec3[] bookmarks = new IntVec3[10];

        public BookmarkMapComponent(Map map) : base(map)
        {
            for (int i = 0; i < 10; i++)
                bookmarks[i] = IntVec3.Invalid;
        }

        public IntVec3 GetBookmark(int slot)
        {
            if (slot < 0 || slot >= 10)
                return IntVec3.Invalid;
            return bookmarks[slot];
        }

        public void SetBookmark(int slot, IntVec3 position)
        {
            if (slot >= 0 && slot < 10)
                bookmarks[slot] = position;
        }

        public bool IsBookmarkSet(int slot)
        {
            if (slot < 0 || slot >= 10)
                return false;
            return bookmarks[slot].IsValid;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            for (int i = 0; i < 10; i++)
            {
                Scribe_Values.Look(ref bookmarks[i], "bookmark" + i, IntVec3.Invalid);
            }
        }
    }
}
