using System;

namespace DieselBundleViewer.Models
{
    /// <summary>
    /// Orders .crate names by load priority, used to sort crates so the right one
    /// wins (loaded last) when the same asset id appears in more than one. Patch
    /// crates ("patch_*") override base crates, and a higher-numbered patch
    /// overrides a lower one
    /// </summary>
    public static class CratePriority
    {
        /// <summary>
        /// Returns &gt; 0 if crate <paramref name="a"/> should take precedence
        /// over <paramref name="b"/>, &lt; 0 if b should, 0 if they rank equal.
        /// </summary>
        public static int Compare(string a, string b)
        {
            bool aPatch = IsPatch(a);
            bool bPatch = IsPatch(b);
            if (aPatch != bPatch)
                return aPatch ? 1 : -1; // any patch crate outranks any base crate

            // Same class: highest name wins. Ordinal compare assumes patch
            // indices stay single-digit (patch_0_0 .. patch_0_9); a two-digit
            // segment would need numeric-aware comparison instead.
            return string.CompareOrdinal(a, b);
        }

        private static bool IsPatch(string name) =>
            name.StartsWith("patch", StringComparison.OrdinalIgnoreCase);
    }
}
