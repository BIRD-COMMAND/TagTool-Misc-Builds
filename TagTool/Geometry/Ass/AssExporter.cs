using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Geometry.BspCollisionGeometry;
using TagTool.Geometry.Export;
using TagTool.Geometry.Utils;
using TagTool.Tags.Definitions;
using static TagTool.Geometry.Ass.AssFormat;
using static TagTool.Geometry.Ass.AssFormat.AssObject;

namespace TagTool.Geometry.Ass
{
    public class AssExporter
    {
        GameCache Cache { get; set; }
        AssFormat Ass { get; set; }

        public readonly float ScaleFactor;

        public AssExporter(GameCache cacheContext, AssFormat ass, float scaleFactor = 100.0f)
        {
            Cache = cacheContext;
            Ass = ass;
            ScaleFactor = scaleFactor;
        }

        // -----------------------------------------------------------------------
        // DTO path: ExportScenarioBsp → AssFormat
        // Mirrors donor blam-tags/src/ass.rs AssFile::from_scenario_structure_bsp()
        // -----------------------------------------------------------------------

        public void Export(ExportScenarioBsp dto)
        {
            int renderTris   = 0, portalTris  = 0, weatherTris  = 0;
            int collTris     = 0, skippedPort = 0, skippedWeath = 0;
            int skippedColl  = 0;
            int collTotalSurfaces = 0, collEmittedSurfaces = 0;
            int collSkippedMalformed = 0, collSkippedDegenerate = 0;
            int collTriangulationFallbacks = 0, collNonConvexRings = 0, collNonPlanarRings = 0;
            int collDegenerateTrianglesSkipped = 0;
            int collMaxRingVertexCount = 0;
            float collMaxRingEdgeLength = 0.0f, collMaxTriangleArea = 0.0f, collMaxTriangleEdgeLength = 0.0f;
            bool collHasBounds = false;
            RealPoint3d collMin = new RealPoint3d(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            RealPoint3d collMax = new RealPoint3d(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            int suspiciousRingCount = 0;
            bool loggedFirstSuspicious = false;

            // INSTANCE 0: Scene Root (object_index = -1, parent_id = -1).
            Ass.Instances.Add(new AssInstance { Name = "Scene Root", ParentID = -1 });

            // Regular sbsp materials first; special materials added on demand.
            foreach (var mat in dto.Materials)
            {
                Ass.Materials.Add(new AssMaterial
                {
                    Name           = mat.ShaderPath,
                    MaterialEffect = string.Empty,
                });
            }

            int portalMatIdx    = -1;
            int weatherMatIdx   = -1;
            int collisionMatIdx = -1;

            // Cluster MESHes + identity INSTANCEs.
            for (int ci = 0; ci < dto.Clusters.Count; ci++)
            {
                var cluster = dto.Clusters[ci];
                var assObj  = BuildMeshObject(cluster.Meshes, dto.Materials, ref renderTris);
                if (assObj.Vertices.Count == 0) continue;
                int objIdx = Ass.Objects.Count;
                Ass.Objects.Add(assObj);
                Ass.Instances.Add(new AssInstance
                {
                    ObjectIndex = objIdx,
                    Name        = $"cluster_{ci}",
                    UniqueID    = Ass.Instances.Count - 1,
                });
            }

            // Cluster portal MESHes + INSTANCEs.
            for (int pi = 0; pi < dto.ClusterPortals.Count; pi++)
            {
                var portal = dto.ClusterPortals[pi];
                if (portal.Vertices.Count < 3) { skippedPort++; continue; }

                portalMatIdx = EnsureSpecialMaterial("+portal");

                var assObj = new AssObject { ObjectType = AssObjectType.Mesh };
                foreach (var v in portal.Vertices)
                {
                    assObj.Vertices.Add(new AssVertex
                    {
                        Position = v * ScaleFactor,
                        Normal   = new RealVector3d(0, 0, 1),
                        UvSets   = new List<AssVertex.UvSet> { new AssVertex.UvSet() },
                    });
                }
                int n = assObj.Vertices.Count;
                for (int k = 1; k + 1 < n; k++)
                {
                    assObj.Triangles.Add(new AssTriangle
                    {
                        MaterialIndex = portalMatIdx,
                        VertexIndices = new List<int> { 0, k, k + 1 },
                    });
                    portalTris++;
                }

                if (assObj.Triangles.Count == 0) { skippedPort++; continue; }
                int objIdx = Ass.Objects.Count;
                Ass.Objects.Add(assObj);
                Ass.Instances.Add(new AssInstance
                {
                    ObjectIndex = objIdx,
                    Name        = $"+portal_{pi}",
                    UniqueID    = Ass.Instances.Count - 1,
                });
            }

            // Weather polyhedron MESHes (vertices populated only if triple-intersection ran).
            for (int wi = 0; wi < dto.WeatherPolyhedra.Count; wi++)
            {
                var wp = dto.WeatherPolyhedra[wi];
                if (wp.Vertices == null || wp.Vertices.Count < 3) { skippedWeath++; continue; }

                weatherMatIdx = EnsureSpecialMaterial("+weather");

                var assObj = BuildWeatherObject(wp, weatherMatIdx, ref weatherTris);
                if (assObj.Vertices.Count == 0) { skippedWeath++; continue; }

                int objIdx = Ass.Objects.Count;
                Ass.Objects.Add(assObj);
                Ass.Instances.Add(new AssInstance
                {
                    ObjectIndex = objIdx,
                    Name        = $"+weather_{wi}",
                    UniqueID    = Ass.Instances.Count - 1,
                });
            }

            // Instanced geometry defs → OBJECTs; placements → INSTANCEs.
            var defToObjIdx = new int[dto.InstancedGeometryDefs.Count];
            for (int i = 0; i < defToObjIdx.Length; i++) defToObjIdx[i] = -1;

            for (int di = 0; di < dto.InstancedGeometryDefs.Count; di++)
            {
                var def    = dto.InstancedGeometryDefs[di];
                var assObj = BuildMeshObject(def.Meshes, dto.Materials, ref renderTris);
                if (assObj.Vertices.Count == 0) continue;
                defToObjIdx[di] = Ass.Objects.Count;
                Ass.Objects.Add(assObj);
            }

            foreach (var placement in dto.InstancedGeometryPlacements)
            {
                int di = placement.DefinitionIndex;
                if (di < 0 || di >= defToObjIdx.Length || defToObjIdx[di] < 0) continue;

                // Quaternion from 4×3 matrix (rows 1-3 = forward/left/up basis vectors).
                // Mirrors donor: RealQuaternion::from_basis_columns(f, l, u).
                var mat = placement.Transform;
                var rotMat = new Matrix4x4(
                    mat.m11, mat.m12, mat.m13, 0f,
                    mat.m21, mat.m22, mat.m23, 0f,
                    mat.m31, mat.m32, mat.m33, 0f,
                    0f, 0f, 0f, 1f);
                var qn  = Quaternion.CreateFromRotationMatrix(rotMat);
                var rot = new RealQuaternion(qn.X, qn.Y, qn.Z, qn.W);
                var pos = new RealVector3d(mat.m41 * ScaleFactor, mat.m42 * ScaleFactor, mat.m43 * ScaleFactor);

                Ass.Instances.Add(new AssInstance
                {
                    ObjectIndex = defToObjIdx[di],
                    Name        = placement.InstanceName,
                    UniqueID    = Ass.Instances.Count - 1,
                    LocalTransform = new AssInstance.Transform
                    {
                        Rotation    = rot,
                        Translation = pos,
                        Scale       = placement.Scale,
                    },
                });
            }

            // Markers: one SPHERE OBJECT per marker + named INSTANCE.
            foreach (var marker in dto.Markers)
            {
                int objIdx = Ass.Objects.Count;
                Ass.Objects.Add(new AssObject
                {
                    ObjectType     = AssObjectType.Sphere,
                    SphereMaterial = -1,
                    SphereRadius   = 10.0f,
                });
                Ass.Instances.Add(new AssInstance
                {
                    ObjectIndex = objIdx,
                    Name        = marker.GroupName,
                    UniqueID    = Ass.Instances.Count - 1,
                    LocalTransform = new AssInstance.Transform
                    {
                        Rotation    = marker.Rotation,
                        Translation = new RealVector3d(
                            marker.Translation.X * ScaleFactor,
                            marker.Translation.Y * ScaleFactor,
                            marker.Translation.Z * ScaleFactor),
                        Scale = 1.0f,
                    },
                });
            }

            // Environment objects: xref MESH per palette + INSTANCE per placement.
            var envPaletteObjIdx = new Dictionary<int, int>();
            for (int ei = 0; ei < dto.EnvironmentObjects.Count; ei++)
            {
                var env = dto.EnvironmentObjects[ei];

                // Each unique EnvironmentObject (by TagPath) gets one xref MESH object.
                // We key by list position since dto builds one per sbsp placement, but
                // if the same xref appears in multiple placements we still emit one OBJECT
                // for the palette index. Use ei as the key (one per palette entry in dto).
                int objIdx = Ass.Objects.Count;
                Ass.Objects.Add(new AssObject
                {
                    ObjectType = AssObjectType.Mesh,
                    XrefPath   = env.TagPath,
                    XrefName   = env.ObjectName,
                });

                var mat = env.Transform;
                var rotMat = new Matrix4x4(
                    mat.m11, mat.m12, mat.m13, 0f,
                    mat.m21, mat.m22, mat.m23, 0f,
                    mat.m31, mat.m32, mat.m33, 0f,
                    0f, 0f, 0f, 1f);
                var qn  = Quaternion.CreateFromRotationMatrix(rotMat);
                var rot = new RealQuaternion(qn.X, qn.Y, qn.Z, qn.W);
                var pos = new RealVector3d(
                    mat.m41 * ScaleFactor, mat.m42 * ScaleFactor, mat.m43 * ScaleFactor);

                Ass.Instances.Add(new AssInstance
                {
                    ObjectIndex = objIdx,
                    Name        = env.ObjectName,
                    UniqueID    = Ass.Instances.Count - 1,
                    LocalTransform = new AssInstance.Transform
                    {
                        Rotation    = rot,
                        Translation = pos,
                        Scale       = env.Scale,
                    },
                });
            }

            // Structure collision BSP: one merged MESH + @CollideOnly INSTANCE.
            if (dto.CollisionSurfaces.Count > 0)
            {
                collisionMatIdx = EnsureSpecialMaterial("@collision_only");

                var collObj = new AssObject { ObjectType = AssObjectType.Mesh };

                foreach (var surface in dto.CollisionSurfaces)
                {
                    collTotalSurfaces++;
                    var ring = surface.VertexIndices;
                    if (ring.Count < 3) { skippedColl++; collSkippedMalformed++; continue; }

                    var ringPoints = new List<RealPoint3d>(ring.Count);
                    bool badIndex = false;
                    foreach (int vi in ring)
                    {
                        if (vi < 0 || vi >= dto.CollisionVertices.Count)
                        {
                            badIndex = true;
                            break;
                        }
                        ringPoints.Add(dto.CollisionVertices[vi]);
                    }
                    if (badIndex) { skippedColl++; collSkippedMalformed++; continue; }

                    ComputeRingEdgeStats(ringPoints, out float ringMaxEdge, out float ringAvgEdge);
                    if (ring.Count > collMaxRingVertexCount) collMaxRingVertexCount = ring.Count;
                    if (ringMaxEdge > collMaxRingEdgeLength) collMaxRingEdgeLength = ringMaxEdge;

                    if (!GeometryUtils.TryTriangulatePolygon(ringPoints, out var localTriangles, out bool nonConvex, out bool nonPlanar))
                    {
                        // Fallback to fan only when triangulation fails entirely.
                        localTriangles = GeometryUtils.TriangleFanToList(BuildSequentialIndices(ring.Count));
                        collTriangulationFallbacks++;
                    }
                    if (nonConvex) collNonConvexRings++;
                    if (nonPlanar) collNonPlanarRings++;

                    if (localTriangles.Count < 3)
                    {
                        skippedColl++;
                        collSkippedDegenerate++;
                        continue;
                    }

                    int fanBase = collObj.Vertices.Count;
                    foreach (var p in ringPoints)
                    {
                        ExpandBounds(p, ref collHasBounds, ref collMin, ref collMax);
                        collObj.Vertices.Add(new AssVertex
                        {
                            Position = p * ScaleFactor,
                            Normal   = new RealVector3d(0, 0, 1),
                            UvSets   = new List<AssVertex.UvSet> { new AssVertex.UvSet() },
                        });
                    }

                    float surfaceMaxTriEdge = 0.0f;
                    float surfaceMaxTriArea = 0.0f;
                    bool hasNonZeroArea = false;

                    for (int i = 0; i + 2 < localTriangles.Count; i += 3)
                    {
                        int a = localTriangles[i];
                        int b = localTriangles[i + 1];
                        int c = localTriangles[i + 2];
                        if (a < 0 || b < 0 || c < 0 || a >= ringPoints.Count || b >= ringPoints.Count || c >= ringPoints.Count)
                            continue;

                        var pa = ringPoints[a];
                        var pb = ringPoints[b];
                        var pc = ringPoints[c];
                        ComputeTriangleStats(pa, pb, pc, out float triArea, out float triMaxEdge);
                        if (triArea <= 1e-8f)
                        {
                            collDegenerateTrianglesSkipped++;
                            continue;
                        }
                        hasNonZeroArea = true;
                        if (triMaxEdge > surfaceMaxTriEdge) surfaceMaxTriEdge = triMaxEdge;
                        if (triArea > surfaceMaxTriArea) surfaceMaxTriArea = triArea;

                        collObj.Triangles.Add(new AssTriangle
                        {
                            MaterialIndex = collisionMatIdx,
                            VertexIndices = new List<int> { fanBase + a, fanBase + b, fanBase + c },
                        });
                        collTris++;
                    }

                    if (!hasNonZeroArea)
                    {
                        skippedColl++;
                        collSkippedDegenerate++;
                        continue;
                    }

                    if (surfaceMaxTriArea > collMaxTriangleArea) collMaxTriangleArea = surfaceMaxTriArea;
                    if (surfaceMaxTriEdge > collMaxTriangleEdgeLength) collMaxTriangleEdgeLength = surfaceMaxTriEdge;

                    // Diagnostic only: don't cull. This flags triangles that are much larger than ring edge scale.
                    bool suspicious = ringAvgEdge > 0.0f &&
                                      surfaceMaxTriEdge > (ringAvgEdge * 8.0f) &&
                                      surfaceMaxTriEdge > 10.0f;
                    if (suspicious)
                    {
                        suspiciousRingCount++;
                        if (!loggedFirstSuspicious)
                        {
                            loggedFirstSuspicious = true;
                            Console.Error.WriteLine(
                                $"[AssExporter] {dto.TagPath}: first suspicious collision ring " +
                                $"bsp={surface.SourceBspIndex} surface={surface.SourceSurfaceIndex} first_edge={surface.FirstEdge} " +
                                $"plane={surface.PlaneIndex} flags={surface.Flags} material={surface.MaterialIndex} " +
                                $"ring_count={ring.Count} ring_edge_avg={ringAvgEdge:F3} " +
                                $"tri_max_edge={surfaceMaxTriEdge:F3} tri_max_area={surfaceMaxTriArea:F3}");
                        }
                    }

                    collEmittedSurfaces++;
                }

                if (collObj.Vertices.Count > 0)
                {
                    int objIdx = Ass.Objects.Count;
                    Ass.Objects.Add(collObj);
                    Ass.Instances.Add(new AssInstance
                    {
                        ObjectIndex = objIdx,
                        Name        = "@CollideOnly",
                        UniqueID    = Ass.Instances.Count - 1,
                    });
                }
            }

            // Diagnostics.
            Console.WriteLine($"  Scenario BSP ASS export (DTO path): {dto.TagPath}");
            Console.WriteLine($"  Materials:            {dto.Materials.Count} (+ {Ass.Materials.Count - dto.Materials.Count} special)");
            Console.WriteLine($"  Clusters:             {dto.Clusters.Count}");
            Console.WriteLine($"  Cluster portals:      {dto.ClusterPortals.Count} ({skippedPort} skipped)");
            Console.WriteLine($"  Weather polyhedra:    {dto.WeatherPolyhedra.Count} ({skippedWeath} skipped)");
            Console.WriteLine($"  Instance defs:        {dto.InstancedGeometryDefs.Count}");
            Console.WriteLine($"  Instance placements:  {dto.InstancedGeometryPlacements.Count}");
            Console.WriteLine($"  Markers:              {dto.Markers.Count}");
            Console.WriteLine($"  Environment objects:  {dto.EnvironmentObjects.Count}");
            Console.WriteLine($"  Collision input:      {dto.CollisionSurfaces.Count} walked surface rings");
            Console.WriteLine($"  Render triangles:     {renderTris}");
            Console.WriteLine($"  Portal triangles:     {portalTris}");
            Console.WriteLine($"  Weather triangles:    {weatherTris}");
            Console.WriteLine($"  Collision triangles:  {collTris}");
            Console.WriteLine($"  Collision surfaces:   total={collTotalSurfaces}, emitted={collEmittedSurfaces}, skipped={skippedColl}");
            Console.WriteLine($"  Collision skips:      malformed={collSkippedMalformed}, degenerate={collSkippedDegenerate}");
            Console.WriteLine($"  Collision rings:      non_convex={collNonConvexRings}, non_planar={collNonPlanarRings}, fallback_fans={collTriangulationFallbacks}");
            Console.WriteLine($"  Collision degenerate: skipped_triangles={collDegenerateTrianglesSkipped}");
            Console.WriteLine($"  Collision maxima:     ring_vertices={collMaxRingVertexCount}, ring_edge={collMaxRingEdgeLength:F3}, tri_edge={collMaxTriangleEdgeLength:F3}, tri_area={collMaxTriangleArea:F3}");
            Console.WriteLine($"  Collision suspicious: {suspiciousRingCount}");
            if (collHasBounds)
            {
                float cdx = collMax.X - collMin.X, cdy = collMax.Y - collMin.Y, cdz = collMax.Z - collMin.Z;
                Console.WriteLine($"  Collision bounds:     min=({collMin.X:F3},{collMin.Y:F3},{collMin.Z:F3}) max=({collMax.X:F3},{collMax.Y:F3},{collMax.Z:F3})");
                if (TryGetRenderBounds(dto, out var renderMin, out var renderMax))
                {
                    float rdx = renderMax.X - renderMin.X, rdy = renderMax.Y - renderMin.Y, rdz = renderMax.Z - renderMin.Z;
                    float rx = MathF.Abs(rdx) > 1e-6f ? MathF.Abs(cdx / rdx) : 0.0f;
                    float ry = MathF.Abs(rdy) > 1e-6f ? MathF.Abs(cdy / rdy) : 0.0f;
                    float rz = MathF.Abs(rdz) > 1e-6f ? MathF.Abs(cdz / rdz) : 0.0f;
                    Console.WriteLine($"  Render bounds:        min=({renderMin.X:F3},{renderMin.Y:F3},{renderMin.Z:F3}) max=({renderMax.X:F3},{renderMax.Y:F3},{renderMax.Z:F3})");
                    Console.WriteLine($"  Coll/Render ratio:    x={rx:F3} y={ry:F3} z={rz:F3}");
                }
            }
            Console.WriteLine($"  Objects:              {Ass.Objects.Count}");
            Console.WriteLine($"  Instances:            {Ass.Instances.Count}");
            Console.WriteLine($"  Scale:                x{ScaleFactor:F1} applied");
        }

        // Build an AssObject MESH from a list of ExportMesh parts.
        // UV: V-flip to 1.0 - V (ASS v5+ convention, donor ass.rs write() vertex emit).
        private AssObject BuildMeshObject(List<ExportMesh> parts, List<ExportMaterial> materials,
            ref int triCounter)
        {
            var assObj = new AssObject { ObjectType = AssObjectType.Mesh };
            foreach (var part in parts)
            {
                int matIdx = (part.MaterialIndex >= 0 && part.MaterialIndex < materials.Count)
                    ? part.MaterialIndex : -1;
                int vtxBase = assObj.Vertices.Count;

                foreach (var v in part.Vertices)
                {
                    // Normal: ConvertVectorSpace was applied in the adapter; use directly.
                    assObj.Vertices.Add(new AssVertex
                    {
                        Position = v.Position * ScaleFactor,
                        Normal   = v.Normal,
                        UvSets   = new List<AssVertex.UvSet>
                        {
                            new AssVertex.UvSet
                            {
                                TextureCoordinates = new RealPoint3d(
                                    v.TexCoords[0].I,
                                    1.0f - v.TexCoords[0].J, // V-flip for ASS v5+
                                    0f),
                            }
                        },
                    });
                }

                for (int t = 0; t + 2 < part.Indices.Count; t += 3)
                {
                    assObj.Triangles.Add(new AssTriangle
                    {
                        MaterialIndex = matIdx,
                        VertexIndices = new List<int>
                        {
                            vtxBase + part.Indices[t],
                            vtxBase + part.Indices[t + 1],
                            vtxBase + part.Indices[t + 2],
                        },
                    });
                    triCounter++;
                }
            }
            return assObj;
        }

        // Build weather polyhedron MESH from pre-computed vertices + per-plane fan triangulation.
        // If Vertices is empty (donor triple-intersection not run), returns empty object.
        private static AssObject BuildWeatherObject(ExportBspWeatherPolyhedron wp, int matIdx,
            ref int triCounter)
        {
            var assObj = new AssObject { ObjectType = AssObjectType.Mesh };
            if (wp.Vertices == null || wp.Vertices.Count == 0) return assObj;

            // Simple convex hull fan: treat first vertex as hub, fan to all others.
            // Proper per-plane sort would require triple-intersection vertices, which the
            // adapter skips (NullBlock). When vertices are populated via future work, this fan
            // gives an approximation; the donor's full per-face sort is in ass.rs polyhedron_from_planes().
            foreach (var v in wp.Vertices)
            {
                assObj.Vertices.Add(new AssVertex
                {
                    Position = v * 100.0f,
                    Normal   = new RealVector3d(0, 0, 1),
                    UvSets   = new List<AssVertex.UvSet> { new AssVertex.UvSet() },
                });
            }
            int n = assObj.Vertices.Count;
            for (int k = 1; k + 1 < n; k++)
            {
                assObj.Triangles.Add(new AssTriangle
                {
                    MaterialIndex = matIdx,
                    VertexIndices = new List<int> { 0, k, k + 1 },
                });
                triCounter++;
            }
            return assObj;
        }

        private static List<int> BuildSequentialIndices(int count)
        {
            var list = new List<int>(count);
            for (int i = 0; i < count; i++) list.Add(i);
            return list;
        }

        private static void ComputeRingEdgeStats(List<RealPoint3d> points, out float maxEdge, out float avgEdge)
        {
            maxEdge = 0.0f;
            avgEdge = 0.0f;
            if (points == null || points.Count == 0) return;

            float sum = 0.0f;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                float dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                sum += len;
                if (len > maxEdge) maxEdge = len;
            }
            avgEdge = sum / points.Count;
        }

        private static void ComputeTriangleStats(
            RealPoint3d a, RealPoint3d b, RealPoint3d c,
            out float area, out float maxEdge)
        {
            var ab = new Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            var ac = new Vector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
            var bc = new Vector3(c.X - b.X, c.Y - b.Y, c.Z - b.Z);
            area = 0.5f * Vector3.Cross(ab, ac).Length();
            float abLen = ab.Length();
            float acLen = ac.Length();
            float bcLen = bc.Length();
            maxEdge = MathF.Max(abLen, MathF.Max(acLen, bcLen));
        }

        private static void ExpandBounds(RealPoint3d p, ref bool hasBounds, ref RealPoint3d min, ref RealPoint3d max)
        {
            if (!hasBounds)
            {
                min = p;
                max = p;
                hasBounds = true;
                return;
            }
            min = new RealPoint3d(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
            max = new RealPoint3d(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));
        }

        private static bool TryGetRenderBounds(ExportScenarioBsp dto, out RealPoint3d min, out RealPoint3d max)
        {
            min = new RealPoint3d(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new RealPoint3d(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;
            foreach (var cluster in dto.Clusters)
            {
                foreach (var mesh in cluster.Meshes)
                {
                    foreach (var v in mesh.Vertices)
                    {
                        var p = v.Position;
                        if (!any)
                        {
                            min = p;
                            max = p;
                            any = true;
                        }
                        else
                        {
                            min = new RealPoint3d(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
                            max = new RealPoint3d(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));
                        }
                    }
                }
            }
            return any;
        }

        // Add a special marker material (+portal, +weather, @collision_only) if not already present.
        // Returns the AssFormat.Materials index. Mirrors donor ass.rs ensure_special_material().
        private int EnsureSpecialMaterial(string name)
        {
            for (int i = 0; i < Ass.Materials.Count; i++)
                if (Ass.Materials[i].Name == name) return i;
            Ass.Materials.Add(new AssMaterial
            {
                Name           = name,
                MaterialEffect = string.Empty,
                MaterialStrings = new List<string>
                {
                    "BM_FLAGS 0000000000000000000000",
                    "BM_LMRES 1.0000000000 1 0.0000000000 0.0000000000 0.0000000000 0 0.0000000000 0.0000000000 0.0000000000 0",
                },
            });
            return Ass.Materials.Count - 1;
        }

        public void Export(ScenarioStructureBsp sbsp)
        {
            throw new NotImplementedException(
                "Use ExportAssCommand which calls the ScenarioBspExportAdapter before invoking Export(ExportScenarioBsp).");
        }

        public void Export(StructureDesign sddt)
        {
            Ass.Instances.Add(new() { Name = "Scene Root", ParentID = -1 });

            RealVector3d globalForward = RealMatrix4x3.Identity.Forward;
            RealVector3d globalUp = RealMatrix4x3.Identity.Up;

            // Soft Ceilings
            foreach (var ceiling in sddt.SoftCeilings)
            {
                string name = Cache.StringTable.GetString(ceiling.Name);
                string prefix = ceiling.Type switch
                {
                    StructureDesign.SoftCeilingType.SoftKill => "soft_kill",
                    StructureDesign.SoftCeilingType.SlipSurface => "slip_surface",
                    _ => "soft_ceiling"
                };

                Ass.Materials.Add(new() { Name = $"+{prefix}:{name}" });

                AssObject assObj = new();
                List<AssVertex.UvSet> uvSets = [new()];
                int materialIndex = Ass.Materials.Count - 1;

                foreach (var triangle in ceiling.SoftCeilingTriangles)
                {
                    List<RealPoint3d> points = [triangle.Point1, triangle.Point2, triangle.Point3];
                    AddStructureDesignTriangle(assObj, points, triangle.Plane, uvSets, materialIndex);
                }

                Ass.Objects.Add(assObj);
                Ass.Instances.Add(new()
                {
                    Name = name,
                    ObjectIndex = Ass.Objects.Count - 1,
                    UniqueID = Ass.Instances.Count - 1,
                });
            }

            // Water Instances
            foreach (var instance in sddt.WaterInstances)
            {
                string waterGroup = Cache.StringTable.GetString(sddt.WaterGroups[instance.WaterNameIndex].Name);
                string name = $"~{waterGroup}";

                AssObject assObj = new();
                List<AssVertex.UvSet> uvSets = [new()];
                List<RealPoint3d> vertices = [];
                List<RealPlane3d> planes = [.. instance.WaterPlanes.Select(n => n.Plane)];
                float tolerance = 2.0e-4f; // looser than ideal but necessary

                foreach (var triangle in instance.WaterDebugTriangles)
                {
                    List<RealPoint3d> points = [triangle.Point1,  triangle.Point2, triangle.Point3];
                    RealPlane3d plane = planes.Find(p => p.ContainsAllPoints(points, tolerance));

                    AddStructureDesignTriangle(assObj, points, plane, uvSets, -1);
                    vertices.AddRange(points);
                }

                Ass.Objects.Add(assObj);
                Ass.Instances.Add(new()
                {
                    Name = name,
                    ObjectIndex = Ass.Objects.Count - 1,
                    UniqueID = Ass.Instances.Count - 1,
                });

                if (instance.FlowVelocity != default)
                {
                    AssInstance direction = new()
                    {
                        Name = $"#{waterGroup}_direction",
                        UniqueID = Ass.Instances.Count,
                        ParentID = Ass.Instances.Count - 1,
                    };

                    // create a decent position for the direction empty
                    RealPoint3d center = RealPoint3d.CenterOf(vertices) * ScaleFactor;
                    float offsetZ = (vertices.Max(v => v.Z) * ScaleFactor) + 50.0f;

                    direction.LocalTransform.Translation = new(center.X, center.Y, offsetZ);
                    direction.LocalTransform.Rotation = RealQuaternion.RotationFromVector(globalForward, instance.FlowVelocity);
                    direction.LocalTransform.Scale = 300.0f; // arbitrary scale for visibility

                    Ass.Instances.Add(direction);
                }
            }
        }

        public void AddStructureDesignTriangle(AssObject assObj, List<RealPoint3d> points, RealPlane3d plane, List<AssVertex.UvSet> uvSets, int materialIndex)
        {
            if (points.Count != 3)
                throw new ArgumentOutOfRangeException(nameof(points), "Invalid vertex count");

            var normal = RealVector3d.Normalize(plane.Normal);
            int firstIndex = assObj.Vertices.Count;

            foreach (var point in points)
            {
                assObj.Vertices.Add(new()
                {
                    Position = point * ScaleFactor,
                    Normal = normal,
                    UvSets = uvSets
                });
            }

            assObj.Triangles.Add(new()
            {
                MaterialIndex = materialIndex,
                VertexIndices = [firstIndex, firstIndex + 1, firstIndex + 2]
            });
        }

        public static int FindNearestNormalizedIndex(RealVector3d target, IList<RealVector3d> vectors)
        {
            var normalizedTarget = RealVector3d.Normalize(target);
            int result = 0;
            float maxFound = 0.0f;

            for (int i = 0; i < vectors.Count; i++)
            {
                RealVector3d normalized = RealVector3d.Normalize(vectors[i]);
                float product = RealVector3d.DotProduct(normalized, normalizedTarget);
                if (product > maxFound)
                {
                    maxFound = product;
                    result = i;
                }
            }

            return result;
        }
    }
}
