using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Case-insensitive string comparer that orders embedded digit runs numerically
    /// instead of lexicographically. "Lifter 2" sorts before "Lifter 10", whereas
    /// plain alphanumeric comparison would put "10" before "2".
    /// </summary>
    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                bool aDigit = char.IsDigit(a[i]);
                bool bDigit = char.IsDigit(b[j]);

                if (aDigit && bDigit)
                {
                    // Skip leading zeros so "01" and "1" compare equal on value,
                    // and fall back to length for tiebreak (shorter = smaller).
                    int aStart = i;
                    while (aStart < a.Length && a[aStart] == '0') aStart++;
                    int bStart = j;
                    while (bStart < b.Length && b[bStart] == '0') bStart++;

                    int aEnd = aStart;
                    while (aEnd < a.Length && char.IsDigit(a[aEnd])) aEnd++;
                    int bEnd = bStart;
                    while (bEnd < b.Length && char.IsDigit(b[bEnd])) bEnd++;

                    int aLen = aEnd - aStart;
                    int bLen = bEnd - bStart;
                    if (aLen != bLen)
                        return aLen.CompareTo(bLen);

                    for (int k = 0; k < aLen; k++)
                    {
                        int cmp = a[aStart + k].CompareTo(b[bStart + k]);
                        if (cmp != 0) return cmp;
                    }

                    // Numeric values tied; advance past the whole digit run
                    // (including leading zeros) and keep comparing.
                    i = aEnd;
                    j = bEnd;
                }
                else if (aDigit != bDigit)
                {
                    // Digits sort before letters so "Item 1" comes before "ItemA".
                    return aDigit ? -1 : 1;
                }
                else
                {
                    int cmp = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (cmp != 0) return cmp;
                    i++;
                    j++;
                }
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
