using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Geometry.BspCollisionGeometry;
using TagTool.Geometry.Utils;
using TagTool.Tags;
using TagTool.Tags.Definitions;
using TagTool.Tags.Resources;

namespace TagTool.Geometry.Export
{
    // Adapts a ScenarioStructureBsp tag into an ExportScenarioBsp DTO.
    // Mirrors donor blam-tags/src/ass.rs AssFile::from_scenario_structure_bsp() decomposed
    // into adapter + DTO pattern so the ASS writer consumes the DTO without tag coupling.
    public class ScenarioBspExportAdapter : IExportScenarioBspAdapter
    {
        public ExportScenarioBsp AdaptScenarioBsp(ScenarioStructureBsp bsp, GameCache cache, string tagPath)
        {
            if (bsp   == null) throw new ArgumentNullException(nameof(bsp));
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            // Load collision / instanced-geometry resource.
            StructureBspTagResources collRes = null;
            try
            {
                if (bsp.CollisionBspResource != null)
                    collRes = cache.ResourceCache.GetStructureBspTagResources(bsp.CollisionBspResource);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ScenarioBspExportAdapter] {tagPath}: collision resource load failed: {ex.Message}");
            }

            // Load and set render geometry vertex buffers.
            if (bsp.Geometry?.Resource != null)
            {
                try
                {
                    var renderDef = cache.ResourceCache.GetRenderGeometryApiResourceDefinition(bsp.Geometry.Resource);
                    bsp.Geometry.SetResourceBuffers(renderDef, false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ScenarioBspExportAdapter] {tagPath}: render resource load failed: {ex.Message}");
                }
            }

            bool useReach = cache.Version >= CacheVersion.HaloReach;

            var dto = new ExportScenarioBsp { TagPath = tagPath };

            Console.Error.WriteLine(
                $"[ScenarioBspExportAdapter] {tagPath}: inline_collision_bsps={bsp.CollisionBsp?.Count ?? 0}, " +
                $"resource_collision_bsps={collRes?.CollisionBsps?.Count ?? 0}");

            BuildMaterials(dto, bsp);
            BuildClusters(dto, bsp, cache, useReach);
            BuildPortals(dto, bsp);
            BuildInstancedGeometry(dto, bsp, cache, useReach, collRes, tagPath);
            BuildWeatherPolyhedra(dto, bsp, tagPath);
            BuildCollisionBsp(dto, tagPath, collRes);
            BuildMarkers(dto, bsp);
            BuildEnvironmentObjects(dto, bsp);

            return dto;
        }

        // -----------------------------------------------------------------------
        // Materials: one ExportMaterial per sbsp.Materials[] entry.
        // ShaderPath = shader tag basename; LightmapPath unused for sbsp.
        // Mirrors donor ass.rs read_materials() (sbsp has no perm/region cells).
        // -----------------------------------------------------------------------

        private static void BuildMaterials(ExportScenarioBsp dto, ScenarioStructureBsp bsp)
        {
            if (bsp.Materials == null) return;
            foreach (var mat in bsp.Materials)
                dto.Materials.Add(new ExportMaterial { ShaderPath = ResolveMaterialName(mat), LightmapPath = string.Empty });
        }

        private static string ResolveMaterialName(RenderMaterial mat)
        {
            if (mat?.RenderMethod == null) return "default";
            var name = mat.RenderMethod.Name ?? $"{mat.RenderMethod.Group.Tag}_0x{mat.RenderMethod.Index:X4}";
            string[] delimiters = { "\\shaders\\", "\\materials\\", "\\" };
            foreach (var delim in delimiters)
            {
                var parts = name.Split(new[] { delim }, StringSplitOptions.None);
                if (parts.Length > 1) return parts.Last();
            }
            return name;
        }

        // -----------------------------------------------------------------------
        // Clusters: one ExportBspCluster per sbsp cluster, identity compression.
        // Cluster meshes are world-space — vertices are NOT compressed; no decompressor needed.
        // Donor confirms: mesh_idx >= compression_info.count → world-space, identity bounds.
        // -----------------------------------------------------------------------

        private static void BuildClusters(ExportScenarioBsp dto, ScenarioStructureBsp bsp,
            GameCache cache, bool useReach)
        {
            if (bsp.Clusters == null || bsp.Geometry?.Meshes == null) return;

            for (int ci = 0; ci < bsp.Clusters.Count; ci++)
            {
                var cluster = bsp.Clusters[ci];
                int meshIdx = cluster.MeshIndex;
                if (meshIdx < 0 || meshIdx >= bsp.Geometry.Meshes.Count) continue;

                var exportCluster = new ExportBspCluster { ClusterIndex = ci };
                ExtractMeshParts(exportCluster.Meshes, bsp, cache, meshIdx, null, useReach, $"cluster_{ci}");
                if (exportCluster.Meshes.Count > 0 || bsp.Geometry.Meshes[meshIdx].Parts.Count > 0)
                    dto.Clusters.Add(exportCluster);
            }
        }

        // -----------------------------------------------------------------------
        // Portals: fan-order vertices per cluster portal.
        // Donor ass.rs reads cluster portals[i].vertices directly (world-space points).
        // -----------------------------------------------------------------------

        private static void BuildPortals(ExportScenarioBsp dto, ScenarioStructureBsp bsp)
        {
            if (bsp.ClusterPortals == null) return;
            foreach (var portal in bsp.ClusterPortals)
            {
                if (portal.Vertices == null || portal.Vertices.Count < 3) continue;
                var ep = new ExportBspClusterPortal();
                foreach (var v in portal.Vertices)
                    ep.Vertices.Add(v.Position);
                dto.ClusterPortals.Add(ep);
            }
        }

        // -----------------------------------------------------------------------
        // Instanced geometry: defs from collresource, placements from sbsp.
        // Each def uses its own compression_index into bsp.Geometry.Compression.
        // Donor deduplicates content-identical defs; we skip dedup and emit one
        // OBJECT per def — functionally correct, slightly larger output.
        // -----------------------------------------------------------------------

        private static void BuildInstancedGeometry(ExportScenarioBsp dto, ScenarioStructureBsp bsp,
            GameCache cache, bool useReach, StructureBspTagResources collRes, string tagPath)
        {
            if (collRes?.InstancedGeometry == null || bsp.Geometry?.Meshes == null) return;

            // Build one def per collresource.InstancedGeometry entry.
            for (int di = 0; di < collRes.InstancedGeometry.Count; di++)
            {
                var def    = collRes.InstancedGeometry[di];
                int meshIdx = def.MeshIndex;
                int compIdx = def.CompressionIndex;

                VertexCompressor compressor = null;
                ExportCompressionBounds bounds = ExportCompressionBounds.Identity;

                if (meshIdx >= 0 && meshIdx < bsp.Geometry.Meshes.Count
                    && bsp.Geometry.Compression != null
                    && compIdx >= 0 && compIdx < bsp.Geometry.Compression.Count)
                {
                    var comp  = bsp.Geometry.Compression[compIdx];
                    compressor = new VertexCompressor(comp);
                    bounds     = ExportCompressionBounds.FromRenderGeometryCompression(
                        comp.X, comp.Y, comp.Z, comp.U, comp.V);
                }

                var exportDef = new ExportBspInstancedGeometryDef
                {
                    DefinitionIndex = di,
                    Compression     = bounds,
                };

                if (meshIdx >= 0 && meshIdx < bsp.Geometry.Meshes.Count)
                    ExtractMeshParts(exportDef.Meshes, bsp, cache, meshIdx, compressor, useReach, $"def_{di}");

                dto.InstancedGeometryDefs.Add(exportDef);
            }

            // Placements: one per InstancedGeometryInstance.
            if (bsp.InstancedGeometryInstances == null) return;

            for (int ii = 0; ii < bsp.InstancedGeometryInstances.Count; ii++)
            {
                var inst   = bsp.InstancedGeometryInstances[ii];
                int defIdx = inst.DefinitionIndex;

                if (defIdx < 0 || defIdx >= dto.InstancedGeometryDefs.Count) continue;

                string name = useReach
                    ? (cache.StringTable.GetString(inst.NameReach) ?? $"instance_{ii}")
                    : (cache.StringTable.GetString(inst.Name)      ?? $"instance_{ii}");

                dto.InstancedGeometryPlacements.Add(new ExportBspInstancedGeometryPlacement
                {
                    DefinitionIndex = defIdx,
                    InstanceName    = name,
                    Scale           = inst.Scale,
                    Transform       = inst.Matrix,
                });
            }
        }

        // -----------------------------------------------------------------------
        // Weather polyhedra: List<NullBlock> in TagTool → data not deserializable.
        // Log diagnostic; donor reads raw blay tree which TagTool cannot replicate.
        // -----------------------------------------------------------------------

        private static void BuildWeatherPolyhedra(ExportScenarioBsp dto, ScenarioStructureBsp bsp,
            string tagPath)
        {
            if (bsp.WeatherPolyhedra == null || bsp.WeatherPolyhedra.Count == 0) return;
            Console.Error.WriteLine(
                $"[ScenarioBspExportAdapter] {tagPath}: {bsp.WeatherPolyhedra.Count} weather polyhedra " +
                $"found but TagTool reads this block as NullBlock — skipping. " +
                $"+weather geometry will be absent from the ASS output.");
        }

        // -----------------------------------------------------------------------
        // Collision BSP: collresource.CollisionBsps → CollisionGeometry list.
        // Walk surfaces via GeometryUtils.WalkSurfaceRing; apply Phase 3C winding
        // correction (dot product against BSP plane normal, reverse ring if needed).
        // All surfaces share one @collision_only material assigned by the ASS writer.
        // -----------------------------------------------------------------------

        private static void BuildCollisionBsp(ExportScenarioBsp dto, string tagPath,
            StructureBspTagResources collRes)
        {
            if (collRes?.CollisionBsps == null) return;

            int totalSurfaces = 0, totalMalformed = 0, totalDegenerate = 0, totalKept = 0, totalFlipped = 0;
            int totalCleanedRings = 0;
            int maxRingVertexCount = 0;
            float maxRingEdgeLength = 0.0f;
            float maxRingBBoxDiagonal = 0.0f;
            int mirrorMappingBetter = 0, mirrorMappingEvaluated = 0;
            int maxEdgeBsp = -1, maxEdgeSurface = -1, maxEdgeFirstEdge = -1;
            int maxEdgePlane = -1;
            short maxEdgeMaterial = -1;
            SurfaceFlags maxEdgeFlags = SurfaceFlags.None;
            string maxEdgeRingIndices = string.Empty;
            string maxEdgeRingPoints = string.Empty;
            const int surfaceDumpLimit = 4;
            int dumpedSurfaces = 0;

            for (int gi = 0; gi < collRes.CollisionBsps.Count; gi++)
            {
                var geom = collRes.CollisionBsps[gi];
                if (geom.Vertices == null || geom.Surfaces == null || geom.Edges == null) continue;

                int localBase = dto.CollisionVertices.Count;
                foreach (var v in geom.Vertices)
                    dto.CollisionVertices.Add(v.Point);

                var    edgeCache = BuildEdgeCache(geom);
                bool   hasPlanes = geom.Planes != null && geom.Planes.Count > 0;
                int    vertCount = geom.Vertices.Count;

                for (int si = 0; si < geom.Surfaces.Count; si++)
                {
                    totalSurfaces++;
                    var surface   = geom.Surfaces[si];
                    int firstEdge = surface.FirstEdge;
                    int planeIdx  = surface.Plane & 0x7FFF;

                    var ring = GeometryUtils.WalkSurfaceRing(si, firstEdge, edgeCache);
                    if (ring.Count < 3) { totalMalformed++; continue; }

                    bool invalidIndex = false;
                    foreach (int vi in ring)
                    {
                        if (vi < 0 || vi >= vertCount) { invalidIndex = true; break; }
                    }
                    if (invalidIndex) { totalMalformed++; continue; }

                    var cleanedRing = CleanRingByPosition(ring, geom.Vertices);
                    if (cleanedRing.Count != ring.Count)
                    {
                        totalCleanedRings++;
                        ring = cleanedRing;
                    }
                    if (ring.Count < 3) { totalDegenerate++; continue; }

                    var ringPoints = new List<RealPoint3d>(ring.Count);
                    foreach (int vi in ring)
                        ringPoints.Add(geom.Vertices[vi].Point);

                    ComputeRingBoundsAndEdges(ringPoints,
                        out var bmin, out var bmax, out float bboxDiagonal,
                        out float ringMaxEdge, out float ringAvgEdge);

                    var mirrorRing = WalkSurfaceRingMirror(si, firstEdge, edgeCache);
                    if (mirrorRing.Count >= 3)
                    {
                        bool mirrorValid = true;
                        var mirrorPoints = new List<RealPoint3d>(mirrorRing.Count);
                        foreach (int mvi in mirrorRing)
                        {
                            if (mvi < 0 || mvi >= vertCount) { mirrorValid = false; break; }
                            mirrorPoints.Add(geom.Vertices[mvi].Point);
                        }
                        if (mirrorValid)
                        {
                            mirrorMappingEvaluated++;
                            ComputeRingBoundsAndEdges(mirrorPoints,
                                out _, out _, out _,
                                out float mirrorMaxEdge, out float mirrorAvgEdge);
                            if (mirrorMaxEdge + 1e-3f < ringMaxEdge && mirrorAvgEdge + 1e-3f < ringAvgEdge)
                                mirrorMappingBetter++;
                        }
                    }

                    int uniqueCount = ring.Distinct().Count();
                    if (ring.Count > maxRingVertexCount) maxRingVertexCount = ring.Count;
                    if (ringMaxEdge > maxRingEdgeLength)
                    {
                        maxRingEdgeLength = ringMaxEdge;
                        maxEdgeBsp = gi;
                        maxEdgeSurface = si;
                        maxEdgeFirstEdge = firstEdge;
                        maxEdgePlane = planeIdx;
                        maxEdgeMaterial = surface.MaterialIndex;
                        maxEdgeFlags = surface.Flags;
                        maxEdgeRingIndices = FormatIndexList(ring, 64);
                        maxEdgeRingPoints = FormatPointList(ringPoints, 24);
                    }
                    if (bboxDiagonal > maxRingBBoxDiagonal) maxRingBBoxDiagonal = bboxDiagonal;

                    if (dumpedSurfaces < surfaceDumpLimit)
                    {
                        dumpedSurfaces++;
                        Console.Error.WriteLine(
                            $"[ScenarioBspExportAdapter] {tagPath}: sbsp_coll[{gi}] surface {si} " +
                            $"first_edge={firstEdge} plane={planeIdx} flags={surface.Flags} material={surface.MaterialIndex} " +
                            $"ring_count={ring.Count} unique={uniqueCount} " +
                            $"bbox_min=({bmin.X:F3},{bmin.Y:F3},{bmin.Z:F3}) " +
                            $"bbox_max=({bmax.X:F3},{bmax.Y:F3},{bmax.Z:F3}) bbox_diag={bboxDiagonal:F3} " +
                            $"edge_max={ringMaxEdge:F3} edge_avg={ringAvgEdge:F3}");
                        Console.Error.WriteLine(
                            $"[ScenarioBspExportAdapter] {tagPath}: sbsp_coll[{gi}] surface {si} ring_indices={FormatIndexList(ring, 24)}");
                        Console.Error.WriteLine(
                            $"[ScenarioBspExportAdapter] {tagPath}: sbsp_coll[{gi}] surface {si} ring_points={FormatPointList(ringPoints, 12)}");
                    }

                    // Winding validation: same approach as Phase 3C CollisionModelExportAdapter.
                    if (hasPlanes && planeIdx < geom.Planes.Count)
                    {
                        var pv = geom.Planes[planeIdx].Value;
                        var pn = new Vector3(pv.I, pv.J, pv.K);
                        if ((surface.Flags & SurfaceFlags.PlaneNegated) != 0) pn = -pn;

                        var v0 = geom.Vertices[ring[0]].Point;
                        var v1 = geom.Vertices[ring[1]].Point;
                        var v2 = geom.Vertices[ring[2]].Point;
                        var e1 = new Vector3(v1.X - v0.X, v1.Y - v0.Y, v1.Z - v0.Z);
                        var e2 = new Vector3(v2.X - v0.X, v2.Y - v0.Y, v2.Z - v0.Z);
                        var cross = Vector3.Cross(e1, e2);

                        if (cross.LengthSquared() < 1e-10f) { totalDegenerate++; continue; }

                        if (Vector3.Dot(cross, pn) < -1e-4f) { ring.Reverse(); totalFlipped++; }
                        else                                   { totalKept++; }
                    }
                    else
                    {
                        totalKept++; // no plane data — keep as-is
                    }

                    // Remap local vertex indices to global dto list.
                    var globalRing = new List<int>(ring.Count);
                    foreach (int vi in ring)
                        globalRing.Add(localBase + vi);

                    dto.CollisionSurfaces.Add(new ExportBspCollisionSurface
                    {
                        VertexIndices = globalRing,
                        SourceBspIndex = gi,
                        SourceSurfaceIndex = si,
                        FirstEdge = firstEdge,
                        PlaneIndex = planeIdx,
                        MaterialIndex = surface.MaterialIndex,
                        Flags = surface.Flags,
                    });
                }
            }

            Console.Error.WriteLine(
                $"[ScenarioBspExportAdapter] {tagPath}: collision summary: " +
                $"total_surfaces={totalSurfaces}, kept={totalKept}, flipped={totalFlipped}, " +
                $"skipped_malformed={totalMalformed}, skipped_degenerate={totalDegenerate}, " +
                $"max_ring_vertex_count={maxRingVertexCount}, max_ring_edge_len={maxRingEdgeLength:F3}, " +
                $"max_ring_bbox_diag={maxRingBBoxDiagonal:F3}, " +
                $"cleaned_rings={totalCleanedRings}, " +
                $"mirror_mapping_better={mirrorMappingBetter}/{mirrorMappingEvaluated}, " +
                $"max_edge_surface=({maxEdgeBsp}:{maxEdgeSurface}) first_edge={maxEdgeFirstEdge} plane={maxEdgePlane} material={maxEdgeMaterial} flags={maxEdgeFlags}");
            if (!string.IsNullOrEmpty(maxEdgeRingIndices))
                Console.Error.WriteLine($"[ScenarioBspExportAdapter] {tagPath}: max_edge_ring_indices={maxEdgeRingIndices}");
            if (!string.IsNullOrEmpty(maxEdgeRingPoints))
                Console.Error.WriteLine($"[ScenarioBspExportAdapter] {tagPath}: max_edge_ring_points={maxEdgeRingPoints}");
        }

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

        // Alternate interpretation for side traversal:
        // left -> emit EndVertex, follow ReverseEdge
        // right -> emit StartVertex, follow ForwardEdge
        // Used only for diagnostics to verify side-field mapping quality.
        private static List<int> WalkSurfaceRingMirror(int surfaceIndex, int firstEdge, BspEdgeRow[] edges)
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
                    ring.Add(e.EndVertex);
                    next = e.ReverseEdge;
                }
                else if (e.RightSurface == surfaceIndex)
                {
                    ring.Add(e.StartVertex);
                    next = e.ForwardEdge;
                }
                else
                {
                    return new List<int>();
                }

                if (next == firstEdge)
                    break;

                current = next;
                steps++;
                if (steps > maxSteps)
                    return new List<int>();
            }

            return ring;
        }

        private static void ComputeRingBoundsAndEdges(
            List<RealPoint3d> points,
            out RealPoint3d min,
            out RealPoint3d max,
            out float bboxDiagonal,
            out float maxEdgeLength,
            out float avgEdgeLength)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            float edgeSum = 0.0f;
            maxEdgeLength = 0.0f;

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                minX = MathF.Min(minX, p.X); minY = MathF.Min(minY, p.Y); minZ = MathF.Min(minZ, p.Z);
                maxX = MathF.Max(maxX, p.X); maxY = MathF.Max(maxY, p.Y); maxZ = MathF.Max(maxZ, p.Z);

                var q = points[(i + 1) % points.Count];
                float dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
                float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                edgeSum += len;
                if (len > maxEdgeLength) maxEdgeLength = len;
            }

            min = new RealPoint3d(minX, minY, minZ);
            max = new RealPoint3d(maxX, maxY, maxZ);

            float bx = maxX - minX, by = maxY - minY, bz = maxZ - minZ;
            bboxDiagonal = MathF.Sqrt(bx * bx + by * by + bz * bz);
            avgEdgeLength = points.Count > 0 ? edgeSum / points.Count : 0.0f;
        }

        private static string FormatIndexList(List<int> indices, int maxCount)
        {
            if (indices == null || indices.Count == 0)
                return "[]";
            int count = Math.Min(indices.Count, maxCount);
            string joined = string.Join(",", indices.Take(count));
            return count < indices.Count ? $"[{joined},...]" : $"[{joined}]";
        }

        private static string FormatPointList(List<RealPoint3d> points, int maxCount)
        {
            if (points == null || points.Count == 0)
                return "[]";
            int count = Math.Min(points.Count, maxCount);
            var formatted = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                var p = points[i];
                formatted.Add($"({p.X:F3},{p.Y:F3},{p.Z:F3})");
            }
            string joined = string.Join(";", formatted);
            return count < points.Count ? $"[{joined};...]" : $"[{joined}]";
        }

        private static List<int> CleanRingByPosition(List<int> ring, TagBlock<Vertex> vertices)
        {
            if (ring == null || ring.Count == 0)
                return new List<int>();

            const float PositionEpsilonSquared = 1e-8f;
            var cleaned = new List<int>(ring.Count);

            bool HasSamePosition(int a, int b)
            {
                if (a < 0 || b < 0 || a >= vertices.Count || b >= vertices.Count)
                    return a == b;
                var pa = vertices[a].Point;
                var pb = vertices[b].Point;
                float dx = pa.X - pb.X, dy = pa.Y - pb.Y, dz = pa.Z - pb.Z;
                return (dx * dx + dy * dy + dz * dz) <= PositionEpsilonSquared;
            }

            cleaned.Add(ring[0]);
            for (int i = 1; i < ring.Count; i++)
            {
                int prev = cleaned[cleaned.Count - 1];
                int curr = ring[i];
                if (HasSamePosition(prev, curr))
                    continue;
                cleaned.Add(curr);
            }

            // If the ring closes with the same point, drop the duplicate end point.
            if (cleaned.Count > 2 && HasSamePosition(cleaned[0], cleaned[cleaned.Count - 1]))
                cleaned.RemoveAt(cleaned.Count - 1);

            return cleaned;
        }

        // -----------------------------------------------------------------------
        // Markers: one ExportMarker per sbsp.Markers[] entry.
        // Name is the fixed-length string field; Rotation + Position from tag.
        // -----------------------------------------------------------------------

        private static void BuildMarkers(ExportScenarioBsp dto, ScenarioStructureBsp bsp)
        {
            if (bsp.Markers == null) return;
            foreach (var m in bsp.Markers)
            {
                dto.Markers.Add(new ExportMarker
                {
                    GroupName   = m.Name ?? string.Empty,
                    Translation = m.Position,
                    Rotation    = m.Rotation,
                    Scale       = 1.0f,
                });
            }
        }

        // -----------------------------------------------------------------------
        // Environment objects: xref placements to palette scenery tags.
        // Rotation is stored as RealQuaternion in the tag; we bake it into a
        // RealMatrix4x3 (rows 1-3 = rotation matrix, row 4 = position) so the
        // writer can reconstruct the quaternion via CreateFromRotationMatrix.
        // -----------------------------------------------------------------------

        private static void BuildEnvironmentObjects(ExportScenarioBsp dto, ScenarioStructureBsp bsp)
        {
            if (bsp.EnvironmentObjectPalette == null || bsp.EnvironmentObjects == null) return;

            foreach (var obj in bsp.EnvironmentObjects)
            {
                int pi = obj.PaletteIndex;
                if (pi < 0 || pi >= bsp.EnvironmentObjectPalette.Count) continue;

                var    pal      = bsp.EnvironmentObjectPalette[pi];
                string xrefPath = pal.Definition?.Name ?? string.Empty;
                string xrefName = System.IO.Path.GetFileNameWithoutExtension(
                    xrefPath.Replace('\\', '/')) ?? "env_object";

                // Bake quaternion into rotation matrix rows 1-3; position in row 4.
                var q  = obj.Rotation;
                var qn = new Quaternion(q.I, q.J, q.K, q.W);
                var m  = Matrix4x4.CreateFromQuaternion(qn);

                dto.EnvironmentObjects.Add(new ExportBspEnvironmentObject
                {
                    TagPath    = xrefPath,
                    ObjectName = xrefName,
                    Scale      = obj.Scale,
                    Transform  = new RealMatrix4x3
                    {
                        m11 = m.M11, m12 = m.M12, m13 = m.M13,
                        m21 = m.M21, m22 = m.M22, m23 = m.M23,
                        m31 = m.M31, m32 = m.M32, m33 = m.M33,
                        m41 = obj.Position.X, m42 = obj.Position.Y, m43 = obj.Position.Z,
                    },
                });
            }
        }

        // -----------------------------------------------------------------------
        // Common mesh extraction: reads vertex + index buffers for one mesh slot.
        // compressor = null → no decompression (cluster world-space geometry).
        // compressor = non-null → decompress positions + UVs (instance def geometry).
        // -----------------------------------------------------------------------

        private static void ExtractMeshParts(List<ExportMesh> target, ScenarioStructureBsp bsp,
            GameCache cache, int meshIndex, VertexCompressor compressor, bool useReach, string label)
        {
            var mesh       = bsp.Geometry.Meshes[meshIndex];
            var meshReader = new MeshReader(cache, mesh);

            List<ModelExtractor.GenericVertex> vertices;
            try
            {
                vertices = useReach
                    ? ModelExtractor.ReadVerticesReach(meshReader)
                    : ModelExtractor.ReadVertices(meshReader);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ScenarioBspExportAdapter] {label}: vertex read failed: {ex.Message}");
                return;
            }

            ModelExtractor.DecompressVertices(vertices, compressor); // no-op if null

            for (int pi = 0; pi < mesh.Parts.Count; pi++)
            {
                var part = mesh.Parts[pi];
                ushort[] rawIndices;
                try   { rawIndices = ModelExtractor.ReadIndices(meshReader, part); }
                catch { continue; }
                if (rawIndices.Length < 3) continue;

                var exportMesh = new ExportMesh
                {
                    MaterialIndex = part.MaterialIndex,
                    PartLabel     = $"{label}:part{pi}",
                };

                var remap = new Dictionary<ushort, int>();
                for (int t = 0; t + 2 < rawIndices.Length; t += 3)
                {
                    ushort i0 = rawIndices[t], i1 = rawIndices[t + 1], i2 = rawIndices[t + 2];
                    if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count)
                        continue;

                    int r0 = GetOrAddVtx(exportMesh, vertices, remap, i0);
                    int r1 = GetOrAddVtx(exportMesh, vertices, remap, i1);
                    int r2 = GetOrAddVtx(exportMesh, vertices, remap, i2);
                    exportMesh.Indices.Add(r0);
                    exportMesh.Indices.Add(r1);
                    exportMesh.Indices.Add(r2);
                }

                if (exportMesh.Indices.Count > 0)
                    target.Add(exportMesh);
            }
        }

        private static int GetOrAddVtx(ExportMesh mesh,
            List<ModelExtractor.GenericVertex> verts, Dictionary<ushort, int> remap, ushort vi)
        {
            if (!remap.TryGetValue(vi, out int local))
            {
                local = mesh.Vertices.Count;
                remap[vi] = local;
                var gv = verts[vi];
                mesh.Vertices.Add(new ExportVertex
                {
                    Position  = new RealPoint3d(gv.Position.X,   gv.Position.Y,   gv.Position.Z),
                    Normal    = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(gv.Normal.X,    gv.Normal.Y,    gv.Normal.Z)),
                    Tangent   = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(gv.Tangents.X,  gv.Tangents.Y,  gv.Tangents.Z)),
                    Binormal  = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(gv.Binormals.X, gv.Binormals.Y, gv.Binormals.Z)),
                    TexCoords = new[] { new RealVector2d(gv.TexCoords.X, gv.TexCoords.Y), new RealVector2d(0, 0) },
                });
            }
            return local;
        }
    }
}
