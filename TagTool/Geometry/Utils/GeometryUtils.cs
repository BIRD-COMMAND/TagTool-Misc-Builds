using System;
using System.Collections.Generic;
using System.Numerics;
using TagTool.Common;

namespace TagTool.Geometry.Utils
{
    /// <summary>
    /// Cached edge row for BSP edge-ring walking. Pre-built once per BSP to avoid per-step
    /// struct access in the hot walk loop.
    /// Mirrors donor geometry.rs EdgeRow.
    /// </summary>
    public struct BspEdgeRow
    {
        public int StartVertex;
        public int EndVertex;
        public int ForwardEdge;
        public int ReverseEdge;
        public int LeftSurface;
        public int RightSurface;
    }

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

        /// <summary>
        /// Walk a single BSP surface's edge ring and return the ordered list of vertex indices
        /// that bound it.
        ///
        /// Ported from donor geometry.rs walk_surface_ring():
        ///   - Each Halo BSP edge belongs to exactly two surfaces (LeftSurface, RightSurface).
        ///   - Matching side determines which vertex to emit and which neighbour to follow:
        ///       left  → emit StartVertex, follow ForwardEdge
        ///       right → emit EndVertex,   follow ReverseEdge
        ///   - Terminates when the next edge equals firstEdge (ring closed).
        ///   - Returns an empty list on: out-of-range edge index, edge not owned by surface
        ///     (malformed BSP), or ring not closed within edges.Length * 2 + 8 steps.
        /// </summary>
        public static List<int> WalkSurfaceRing(int surfaceIndex, int firstEdge, BspEdgeRow[] edges)
        {
            var ring = new List<int>();
            int current  = firstEdge;
            int steps    = 0;
            int maxSteps = edges.Length * 2 + 8;

            while (true)
            {
                if (current < 0 || current >= edges.Length)
                    return new List<int>();

                var e = edges[current];
                int next;

                if (e.LeftSurface == surfaceIndex)
                {
                    ring.Add(e.StartVertex);
                    next = e.ForwardEdge;
                }
                else if (e.RightSurface == surfaceIndex)
                {
                    ring.Add(e.EndVertex);
                    next = e.ReverseEdge;
                }
                else
                {
                    return new List<int>(); // edge not owned by this surface — malformed BSP
                }

                if (next == firstEdge)
                    break;

                current = next;
                steps++;
                if (steps > maxSteps)
                    return new List<int>(); // non-terminating ring
            }

            return ring;
        }

        /// <summary>
        /// Triangulate a polygon ring using projected 2D ear clipping.
        /// Returns local vertex indices (0..ringPoints.Count-1) as triangle triplets.
        /// </summary>
        public static bool TryTriangulatePolygon(
            IList<RealPoint3d> ringPoints,
            out List<int> localTriangles,
            out bool nonConvex,
            out bool nonPlanar)
        {
            localTriangles = new List<int>();
            nonConvex = false;
            nonPlanar = false;

            if (ringPoints == null || ringPoints.Count < 3)
                return false;

            if (!TryComputeNewellNormal(ringPoints, out var normal))
                return false;

            float normalLen = normal.Length();
            if (normalLen < 1e-8f)
                return false;
            var normalUnit = normal / normalLen;

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            var origin = ToVector3(ringPoints[0]);
            float maxPlaneDist = 0.0f;
            for (int i = 0; i < ringPoints.Count; i++)
            {
                var p = ringPoints[i];
                minX = MathF.Min(minX, p.X); minY = MathF.Min(minY, p.Y); minZ = MathF.Min(minZ, p.Z);
                maxX = MathF.Max(maxX, p.X); maxY = MathF.Max(maxY, p.Y); maxZ = MathF.Max(maxZ, p.Z);

                var pv = ToVector3(p);
                float d = MathF.Abs(Vector3.Dot(normalUnit, pv - origin));
                if (d > maxPlaneDist) maxPlaneDist = d;
            }
            float dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            float diag = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            nonPlanar = maxPlaneDist > MathF.Max(0.001f, diag * 0.001f);

            int dropAxis = DominantAxis(normalUnit);
            var projected = new Vector2[ringPoints.Count];
            for (int i = 0; i < ringPoints.Count; i++)
                projected[i] = ProjectTo2D(ringPoints[i], dropAxis);

            float signedArea = SignedArea2D(projected);
            if (MathF.Abs(signedArea) < 1e-9f)
                return false;
            float orientation = signedArea > 0.0f ? 1.0f : -1.0f;

            for (int i = 0; i < ringPoints.Count; i++)
            {
                int prev = (i - 1 + ringPoints.Count) % ringPoints.Count;
                int next = (i + 1) % ringPoints.Count;
                if (!IsConvex(projected[prev], projected[i], projected[next], orientation))
                    nonConvex = true;
            }

            var remaining = new List<int>(ringPoints.Count);
            for (int i = 0; i < ringPoints.Count; i++)
                remaining.Add(i);

            int guard = ringPoints.Count * ringPoints.Count * 4;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int prev = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    int curr = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];

                    var a = projected[prev];
                    var b = projected[curr];
                    var c = projected[next];

                    if (!IsConvex(a, b, c, orientation))
                        continue;

                    bool containsOther = false;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        int test = remaining[j];
                        if (test == prev || test == curr || test == next)
                            continue;
                        if (PointInTriangle(projected[test], a, b, c, orientation))
                        {
                            containsOther = true;
                            break;
                        }
                    }
                    if (containsOther)
                        continue;

                    localTriangles.Add(prev);
                    localTriangles.Add(curr);
                    localTriangles.Add(next);
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    break;
            }

            if (remaining.Count == 3)
            {
                localTriangles.Add(remaining[0]);
                localTriangles.Add(remaining[1]);
                localTriangles.Add(remaining[2]);
                return true;
            }

            localTriangles.Clear();
            return false;
        }

        private static bool TryComputeNewellNormal(IList<RealPoint3d> points, out Vector3 normal)
        {
            normal = Vector3.Zero;
            if (points == null || points.Count < 3)
                return false;

            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                normal.X += (a.Y - b.Y) * (a.Z + b.Z);
                normal.Y += (a.Z - b.Z) * (a.X + b.X);
                normal.Z += (a.X - b.X) * (a.Y + b.Y);
            }
            return normal.LengthSquared() > 1e-12f;
        }

        private static Vector3 ToVector3(RealPoint3d p) => new Vector3(p.X, p.Y, p.Z);

        private static int DominantAxis(Vector3 n)
        {
            float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
            if (ax >= ay && ax >= az) return 0;
            if (ay >= az) return 1;
            return 2;
        }

        private static Vector2 ProjectTo2D(RealPoint3d p, int dropAxis)
        {
            return dropAxis switch
            {
                0 => new Vector2(p.Y, p.Z),
                1 => new Vector2(p.X, p.Z),
                _ => new Vector2(p.X, p.Y),
            };
        }

        private static float SignedArea2D(IList<Vector2> poly)
        {
            float area = 0.0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                area += (a.X * b.Y) - (b.X * a.Y);
            }
            return area * 0.5f;
        }

        private static float Cross2D(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

        private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c, float orientation)
        {
            float turn = Cross2D(b - a, c - b);
            const float Epsilon = 1e-7f;
            return orientation > 0.0f ? turn > Epsilon : turn < -Epsilon;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float orientation)
        {
            const float Epsilon = 1e-7f;
            float c1 = Cross2D(b - a, p - a);
            float c2 = Cross2D(c - b, p - b);
            float c3 = Cross2D(a - c, p - c);

            if (orientation > 0.0f)
                return c1 >= -Epsilon && c2 >= -Epsilon && c3 >= -Epsilon;
            return c1 <= Epsilon && c2 <= Epsilon && c3 <= Epsilon;
        }
    }
}
