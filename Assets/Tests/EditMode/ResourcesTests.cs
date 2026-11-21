using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// Assets/Resources invariants. Resources.Load takes a path without the extension, so two files
    /// that differ only by extension (Training.txt + Training.json) are ambiguous: the game silently
    /// gets the wrong one (the TUTORIAL button did nothing because of exactly this).
    /// </summary>
    public class ResourcesTests
    {
        [Test]
        public void Resources_NoTwoFilesShareAPathWithoutExtension()
        {
            var root = Path.Combine(ProjectRoot(), "Assets", "Resources");
            var clashes = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta", StringComparison.Ordinal))
                .GroupBy(f => Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f)))
                .Where(g => g.Count() > 1)
                .Select(g => string.Join(" + ", g.Select(Path.GetFileName)))
                .ToList();
            Assert.IsEmpty(clashes, "ambiguous Resources.Load names: " + string.Join("; ", clashes));
        }
    }
}
