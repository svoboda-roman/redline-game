using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>
    /// The once-per-save seen-set: ids of hints already shown. Serialisable as is with Unity's
    /// JsonUtility (public field <see cref="seen"/>) or as a single string (<see cref="Serialize"/>).
    /// </summary>
    [Serializable]
    public sealed class TutorialProgress
    {
        /// <summary>Hint ids shown at least once, in the order they were first shown.</summary>
        public List<string> seen = new List<string>();

        /// <summary>True when the hint was already shown in this save.</summary>
        public bool Contains(string id) => seen != null && id != null && seen.Contains(id);

        /// <summary>Records a hint as shown (no duplicates, invalid ids ignored).</summary>
        public void Add(string id)
        {
            if (seen == null) seen = new List<string>();
            if (HintZone.IsValidId(id) && !seen.Contains(id)) seen.Add(id);
        }

        /// <summary>Forgets every hint ("reset tutorial").</summary>
        public void Clear() => seen?.Clear();

        /// <summary>The seen ids joined by ',' (ids never contain a comma).</summary>
        public string Serialize() => seen == null ? string.Empty : string.Join(",", seen);

        /// <summary>Reads <see cref="Serialize"/> output; null, empty or junk entries are skipped.</summary>
        public static TutorialProgress Deserialize(string text)
        {
            var p = new TutorialProgress();
            if (string.IsNullOrEmpty(text)) return p;
            foreach (var part in text.Split(','))
                p.Add(part.Trim());
            return p;
        }
    }
}
