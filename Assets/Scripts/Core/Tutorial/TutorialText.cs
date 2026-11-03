using System;
using System.Text;

namespace Redline.Core
{
    /// <summary>Resolves control tokens such as {HOOK} or {JUMP} in hint text.</summary>
    public static class TutorialText
    {
        /// <summary>
        /// Replaces every {KEY} (KEY = A-Z, 0-9, '_') with resolver(KEY). A null resolver, or one that
        /// returns null/empty or throws, leaves the bare key ("HOOK") so the text still reads.
        /// Anything else in braces, and unmatched braces, stay as written. Allocates; call it when a
        /// hint changes, not per frame.
        /// </summary>
        public static string Resolve(string text, Func<string, string> resolver)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? string.Empty;
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int close = c == '{' ? text.IndexOf('}', i + 1) : -1;
                if (close > i + 1 && IsKey(text, i + 1, close))
                {
                    string key = text.Substring(i + 1, close - i - 1);
                    sb.Append(Label(key, resolver));
                    i = close + 1;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string Label(string key, Func<string, string> resolver)
        {
            if (resolver == null) return key;
            string label;
            try
            {
                label = resolver(key);
            }
            catch (Exception)
            {
                return key;
            }
            return string.IsNullOrEmpty(label) ? key : label;
        }

        private static bool IsKey(string s, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                char c = s[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }
    }
}
