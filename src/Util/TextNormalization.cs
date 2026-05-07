using System.Globalization;
using System.Text;

namespace RimWorldAccess
{
    /// <summary>
    /// Unicode-aware text normalization helpers. Used by typeahead matching so that
    /// "cafe" matches "café" — a French/German/Spanish player can type without
    /// typing accented characters and still find items.
    /// </summary>
    public static class TextNormalization
    {
        /// <summary>
        /// Strip combining diacritics (NFD decomposition + drop NonSpacingMark code points)
        /// and expand the few Latin ligatures that don't decompose cleanly. Pattern from
        /// OniAccess's StringUtil.RemoveDiacritics.
        /// </summary>
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            string decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char c = decomposed[i];
                switch (c)
                {
                    case 'œ':
                    case 'Œ':
                        sb.Append("oe");
                        break;
                    case 'æ':
                    case 'Æ':
                        sb.Append("ae");
                        break;
                    case 'ß':
                        sb.Append("ss");
                        break;
                    default:
                        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
