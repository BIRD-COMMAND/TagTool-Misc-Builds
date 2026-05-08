using System.Collections.Generic;

namespace TagTool.Geometry.Utils
{
    /// <summary>
    /// Shared geometry helpers used by exporter implementations.
    /// Mirrors relevant portions of donor geometry.rs.
    /// </summary>
    public static class GeometryUtils
    {
        /// <summary>
        /// Converts a triangle-strip index buffer into a triangle-list.
        ///
        /// Ported from donor geometry.rs strip_to_list():
        ///   - 0xFFFF is the strip-restart sentinel; resets the current run and parity.
        ///   - Every even-parity face uses winding A-B-C; every odd-parity face flips to A-C-B
        ///     to maintain consistent front-facing after the shared-edge flip.
        ///   - Degenerate triangles (any two indices equal) are silently skipped.
        /// </summary>
        public static List<int> StripToTriangleList(IList<int> indices)
        {
            var result = new List<int>(indices.Count);
            int run = 0;
            bool parity = false;
            int a = -1, b = -1;

            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];

                if (idx == 0xFFFF) // strip restart sentinel
                {
                    a = -1; b = -1;
                    parity = false;
                    run = 0;
                    continue;
                }

                if (run == 0) { a = idx; run++; continue; }
                if (run == 1) { b = idx; run++; continue; }

                int c = idx;

                // Skip degenerate triangles.
                if (a != b && b != c && a != c)
                {
                    if (!parity)
                    {
                        result.Add(a); result.Add(b); result.Add(c);
                    }
                    else
                    {
                        result.Add(a); result.Add(c); result.Add(b);
                    }
                }

                a = b;
                b = c;
                parity = !parity;
            }

            return result;
        }

        /// <summary>
        /// Converts a triangle-fan vertex sequence (first vertex = hub) into a triangle-list.
        /// Used for cluster portals (donor ass.rs portal fan triangulation).
        /// </summary>
        public static List<int> TriangleFanToList(IList<int> fanIndices)
        {
            var result = new List<int>((fanIndices.Count - 2) * 3);
            if (fanIndices.Count < 3)
                return result;

            int hub = fanIndices[0];
            for (int i = 1; i + 1 < fanIndices.Count; i++)
            {
                result.Add(hub);
                result.Add(fanIndices[i]);
                result.Add(fanIndices[i + 1]);
            }

            return result;
        }

        /// <summary>
        /// Returns 0.0f for negative zero, passing all other values unchanged.
        /// Matches donor geometry.rs / BlamAssetWriter -0.0 normalization behaviour.
        /// </summary>
        public static float NormalizeNegativeZero(float value)
            => value == 0.0f ? 0.0f : value;
    }
}
