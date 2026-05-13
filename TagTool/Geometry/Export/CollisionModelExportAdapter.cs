using System;
using System.Collections.Generic;
using System.Numerics;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Geometry.BspCollisionGeometry;
using TagTool.Geometry.Utils;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Export
{
    public class CollisionModelExportAdapter : IExportCollisionModelAdapter
    {
        public ExportCollisionModel AdaptCollisionModel(CollisionModel coll, GameCache cache, string tagPath)
        {
            if (coll  == null) throw new ArgumentNullException(nameof(coll));
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            var em = new ExportCollisionModel { TagPath = tagPath };

            // Collision model nodes carry only name/parent/sibling/child links — no transforms.
            if (coll.Nodes != null)
            {
                foreach (var node in coll.Nodes)
                {
                    em.Nodes.Add(new ExportNode
                    {
                        Name             = cache.StringTable.GetString(node.Name) ?? string.Empty,
                        ParentIndex      = node.ParentNode,
                        FirstChildIndex  = node.FirstChildNode,
                        NextSiblingIndex = node.NextSiblingNode,
                    });
                }
            }

            if (coll.Regions == null)
                return em;

            // Material deduplication: (shaderName, cellLabel) → index into em.Materials.
            // One material per unique (collision material name, "permName regionName") pair.
            // Mirrors donor jms.rs build_collision_model() material dedup logic.
            var seen = new Dictionary<(string, string), int>(32);

            foreach (var region in coll.Regions)
            {
                var regionName   = cache.StringTable.GetString(region.Name) ?? string.Empty;
                var exportRegion = new ExportCollisionRegion { Name = regionName };

                if (region.Permutations == null)
                {
                    em.Regions.Add(exportRegion);
                    continue;
                }

                foreach (var perm in region.Permutations)
                {
                    var permName    = cache.StringTable.GetString(perm.Name) ?? string.Empty;
                    var exportPerm  = new ExportCollisionPermutation { Name = permName };
                    string cellLabel = $"{permName} {regionName}";

                    if (perm.Bsps != null)
                    {
                        foreach (var bsp in perm.Bsps)
                        {
                            var exportBsp = new ExportCollisionBsp { NodeIndex = bsp.NodeIndex };

                            // Vertices stored in BSP-local space, no scale applied here.
                            // The JMS writer applies ×100 when emitting positions.
                            if (bsp.Geometry.Vertices != null)
                            {
                                foreach (var v in bsp.Geometry.Vertices)
                                    exportBsp.Vertices.Add(v.Point);
                            }

                            var edgeCache   = BuildEdgeCache(bsp.Geometry);
                            int bspMalformed = 0;
                            int vertCount   = bsp.Geometry.Vertices?.Count ?? 0;
                            bool hasPlanes  = bsp.Geometry.Planes != null && bsp.Geometry.Planes.Count > 0;

                            if (bsp.Geometry.Surfaces != null)
                            {
                                for (int si = 0; si < bsp.Geometry.Surfaces.Count; si++)
                                {
                                    var surface   = bsp.Geometry.Surfaces[si];
                                    int firstEdge = surface.FirstEdge;

                                    // Walk the edge ring — returns empty list on malformed BSPs.
                                    var ring = GeometryUtils.WalkSurfaceRing(si, firstEdge, edgeCache);
                                    if (ring.Count < 3)
                                    {
                                        em.WindingMalformed++;
                                        bspMalformed++;
                                        continue;
                                    }

                                    // Bounds-check the first three ring vertices (needed for winding).
                                    if (ring[0] >= vertCount || ring[1] >= vertCount || ring[2] >= vertCount)
                                    {
                                        em.WindingMalformed++;
                                        bspMalformed++;
                                        continue;
                                    }

                                    // -------------------------------------------------------
                                    // Winding validation against the BSP surface plane.
                                    //
                                    // Surface.Plane bits 0-14 = plane index.
                                    // SurfaceFlags.PlaneNegated: the plane was stored with an
                                    // inverted normal relative to the surface's geometric normal.
                                    // Uniform scale does not affect winding, so we compare in
                                    // BSP-local (unscaled) space.
                                    // -------------------------------------------------------
                                    int planeIdx = surface.Plane & 0x7FFF;

                                    if (!hasPlanes || planeIdx >= bsp.Geometry.Planes.Count)
                                    {
                                        // No plane data — cannot validate winding; treat as kept.
                                        em.WindingMissingPlane++;
                                        em.WindingKept++;
                                        em.WindingTotal++;
                                    }
                                    else
                                    {
                                        var planeVal   = bsp.Geometry.Planes[planeIdx].Value;
                                        var planeNormal = new Vector3(planeVal.I, planeVal.J, planeVal.K);

                                        // Negate reference normal when PlaneNegated is set.
                                        if ((surface.Flags & SurfaceFlags.PlaneNegated) != 0)
                                            planeNormal = -planeNormal;

                                        var v0 = bsp.Geometry.Vertices[ring[0]].Point;
                                        var v1 = bsp.Geometry.Vertices[ring[1]].Point;
                                        var v2 = bsp.Geometry.Vertices[ring[2]].Point;
                                        var e1 = new Vector3(v1.X - v0.X, v1.Y - v0.Y, v1.Z - v0.Z);
                                        var e2 = new Vector3(v2.X - v0.X, v2.Y - v0.Y, v2.Z - v0.Z);
                                        var cross = Vector3.Cross(e1, e2);

                                        if (cross.LengthSquared() < 1e-10f)
                                        {
                                            // First triangle is degenerate (zero area) — skip surface.
                                            em.WindingDegenerate++;
                                            em.WindingTotal++;
                                            bspMalformed++;
                                            continue;
                                        }

                                        // Use raw cross-product for sign comparison (no normalize needed).
                                        float dot = Vector3.Dot(cross, planeNormal);
                                        if (dot < -1e-4f)
                                        {
                                            // Ring winding is opposite to plane normal — reverse ring.
                                            ring.Reverse();
                                            em.WindingFlipped++;
                                        }
                                        else
                                        {
                                            em.WindingKept++;
                                        }
                                        em.WindingTotal++;
                                    }

                                    // -------------------------------------------------------
                                    // Material dedup and surface emit.
                                    // -------------------------------------------------------
                                    string shaderName = ResolveMaterialName(coll, cache, surface.MaterialIndex);
                                    var    matKey     = (shaderName, cellLabel);

                                    if (!seen.TryGetValue(matKey, out int matIdx))
                                    {
                                        matIdx       = em.Materials.Count;
                                        seen[matKey] = matIdx;
                                        em.Materials.Add(new ExportMaterial
                                        {
                                            ShaderPath   = shaderName,
                                            LightmapPath = cellLabel,
                                        });
                                    }

                                    exportBsp.Surfaces.Add(new ExportCollisionSurface
                                    {
                                        MaterialIndex = matIdx,
                                        TwoSided      = (surface.Flags & SurfaceFlags.TwoSided)  != 0,
                                        Invisible     = (surface.Flags & SurfaceFlags.Invisible) != 0,
                                        VertexIndices = ring,
                                    });
                                }
                            }

                            if (bspMalformed > 0)
                                Console.Error.WriteLine(
                                    $"[CollisionModelExportAdapter] {tagPath}: {bspMalformed} surface(s) skipped " +
                                    $"(malformed ring, OOB vertex, or degenerate triangle in {regionName}/{permName}).");

                            exportPerm.Bsps.Add(exportBsp);
                        }
                    }

                    exportRegion.Permutations.Add(exportPerm);
                }

                em.Regions.Add(exportRegion);
            }

            return em;
        }

        private static string ResolveMaterialName(CollisionModel coll, GameCache cache, short materialIndex)
        {
            if (materialIndex < 0 || coll.Materials == null || materialIndex >= coll.Materials.Count)
                return "default";
            return cache.StringTable.GetString(coll.Materials[materialIndex].Name) ?? "default";
        }

        // Build a flat edge-cache array for fast walk loop access.
        // Mirrors the EdgeRow pre-cache in donor jms.rs build_collision_model().
        private static BspEdgeRow[] BuildEdgeCache(CollisionGeometry geom)
        {
            if (geom.Edges == null || geom.Edges.Count == 0)
                return Array.Empty<BspEdgeRow>();

            var cache = new BspEdgeRow[geom.Edges.Count];
            for (int i = 0; i < geom.Edges.Count; i++)
            {
                var e = geom.Edges[i];
                cache[i] = new BspEdgeRow
                {
                    StartVertex  = e.StartVertex,
                    EndVertex    = e.EndVertex,
                    ForwardEdge  = e.ForwardEdge,
                    ReverseEdge  = e.ReverseEdge,
                    LeftSurface  = e.LeftSurface,
                    RightSurface = e.RightSurface,
                };
            }
            return cache;
        }
    }
}
