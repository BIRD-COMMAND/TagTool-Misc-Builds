using System;
using System.Collections.Generic;
using System.Linq;
using TagTool.Tags.Definitions;
using System.IO;
using TagTool.Cache;
using TagTool.Common;
using System.Numerics;
using TagTool.Geometry.BspCollisionGeometry;
using static TagTool.IO.ConsoleHistory;
using TagTool.Geometry.Utils;
using TagTool.Geometry.Export;

namespace TagTool.Geometry.Jms
{
    public class JmsCollExporter
    {
        GameCache Cache { get; set; }
        JmsFormat Jms { get; set; }

        public JmsCollExporter(GameCache cacheContext, JmsFormat jms)
        {
            Cache = cacheContext;
            Jms = jms;
        }

        // -----------------------------------------------------------------------
        // New path: ExportCollisionModel DTO → JMS
        // Materials are per-surface (deduplicated by adapter).
        // Triangle-fan triangulation matches donor jms.rs build_collision_model().
        // JMS collision export uses donor blam-tags scale factor 100.0 for positions.
        // -----------------------------------------------------------------------

        public void Export(ExportCollisionModel dto)
        {
            int materialCount  = 0;
            int surfaceCount   = 0;
            int triangleCount  = 0;
            int vertexCount    = 0;
            int skippedCount   = 0;
            int negDetCount    = 0;

            bool hasNodeTransforms = Jms.Nodes.Count > 0;

            // Precompute which node indices have negative-determinant transforms (handedness flip).
            // Pure quaternion+translation nodes always have det = +1, but we check explicitly to
            // catch any unexpected reflections introduced upstream.
            var checkedNodes = new HashSet<int>();
            if (hasNodeTransforms)
            {
                foreach (var region in dto.Regions)
                foreach (var perm  in region.Permutations)
                foreach (var bsp   in perm.Bsps)
                {
                    int ni = bsp.NodeIndex;
                    if (ni < 0 || ni >= Jms.Nodes.Count || !checkedNodes.Add(ni))
                        continue;
                    float det = NodeMatrixDeterminant(ni);
                    if (det < 0f)
                    {
                        negDetCount++;
                        Console.Error.WriteLine(
                            $"[JmsCollExporter] Node {ni} transform has negative determinant ({det:F4}) — " +
                            $"handedness flip detected. Winding may be incorrect after transform.");
                    }
                }
            }

            // Record base offset so material indices stay correct if render was already written.
            int materialBase = Jms.Materials.Count;

            // Add deduplicated materials. ShaderPath = collision material name.
            // LightmapPath = cell label "permName regionName".
            for (int i = 0; i < dto.Materials.Count; i++)
            {
                var mat  = dto.Materials[i];
                int slot = Jms.Materials.Count + 1;
                Jms.Materials.Add(new JmsFormat.JmsMaterial
                {
                    Name         = mat.ShaderPath,
                    MaterialName = $"({slot}) {mat.LightmapPath}",
                });
                materialCount++;
            }

            // Geometry: region → permutation → BSP → surface.
            // Each surface's vertex ring has already had winding corrected by the adapter
            // (ring reversed if dot(triangleNormal, planeNormal) < 0).
            // We fan each corrected polygon (hub = ring[0]) into triangles here.
            // Vertices are unshared (one per triangle corner), matching donor convention.
            foreach (var region in dto.Regions)
            {
                foreach (var perm in region.Permutations)
                {
                    foreach (var bsp in perm.Bsps)
                    {
                        int nodeIdx = bsp.NodeIndex;

                        foreach (var surface in bsp.Surfaces)
                        {
                            var ring = surface.VertexIndices;
                            if (ring.Count < 3) { skippedCount++; continue; }

                            surfaceCount++;

                            // Triangle fan: (ring[0], ring[k], ring[k+1]) for k in 1..n-2.
                            // Winding is correct because the adapter reversed the ring when needed.
                            for (int k = 1; k + 1 < ring.Count; k++)
                            {
                                int ia = ring[0], ib = ring[k], ic = ring[k + 1];
                                if (ia >= bsp.Vertices.Count || ib >= bsp.Vertices.Count || ic >= bsp.Vertices.Count)
                                {
                                    skippedCount++;
                                    continue;
                                }

                                int triBase = Jms.Vertices.Count;

                                foreach (int vi in new[] { ia, ib, ic })
                                {
                                    // Apply JMS scale ×100; apply node world transform if skeleton is present.
                                    RealPoint3d pos = bsp.Vertices[vi] * 100.0f;
                                    if (hasNodeTransforms && nodeIdx >= 0 && nodeIdx < Jms.Nodes.Count)
                                        pos = TransformPointByNode(pos, nodeIdx);

                                    Jms.Vertices.Add(new JmsFormat.JmsVertex
                                    {
                                        Position = pos,
                                        Normal   = new RealVector3d(0, 0, 1),
                                        NodeSets = new List<JmsFormat.JmsVertex.NodeSet>
                                        {
                                            new JmsFormat.JmsVertex.NodeSet
                                            {
                                                NodeIndex  = nodeIdx,
                                                NodeWeight = 1.0f,
                                            }
                                        },
                                        UvSets = new List<JmsFormat.JmsVertex.UvSet>
                                        {
                                            new JmsFormat.JmsVertex.UvSet
                                            {
                                                TextureCoordinates = new RealPoint2d(0, 0),
                                            }
                                        },
                                    });
                                }
                                vertexCount += 3;

                                Jms.Triangles.Add(new JmsFormat.JmsTriangle
                                {
                                    MaterialIndex = materialBase + surface.MaterialIndex,
                                    VertexIndices = new List<int> { triBase, triBase + 1, triBase + 2 },
                                });
                                triangleCount++;
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"  Collision DTO path: corrected surface winding against BSP plane normals.");
            Console.WriteLine($"  Tag:                  {dto.TagPath}");
            Console.WriteLine($"  Materials:            {materialCount}");
            Console.WriteLine($"  Nodes:                {dto.Nodes.Count} (skeleton transforms: {(hasNodeTransforms ? "yes" : "no")})");
            Console.WriteLine($"  Regions:              {dto.Regions.Count}");
            Console.WriteLine($"  Surfaces total:       {dto.WindingTotal + dto.WindingMalformed}");
            Console.WriteLine($"    Kept (no flip):     {dto.WindingKept}");
            Console.WriteLine($"    Flipped:            {dto.WindingFlipped}");
            Console.WriteLine($"    Degenerate skipped: {dto.WindingDegenerate}");
            Console.WriteLine($"    Malformed skipped:  {dto.WindingMalformed}");
            if (dto.WindingMissingPlane > 0)
                Console.WriteLine($"    Missing plane data: {dto.WindingMissingPlane} (treated as kept)");
            Console.WriteLine($"  Triangles emitted:    {triangleCount}");
            Console.WriteLine($"  Vertices emitted:     {vertexCount}");
            if (skippedCount > 0)
                Console.WriteLine($"  Skipped (OOB index):  {skippedCount}");
            if (negDetCount > 0)
                Console.Error.WriteLine($"  WARNING: {negDetCount} node transform(s) with negative determinant (handedness flip).");
            else
                Console.WriteLine($"  Node transforms:      all positive determinant (no handedness flip)");
            Console.WriteLine($"  Scale:                x100.0 applied");
        }

        // Compute the 3×3 determinant of a node's rotation matrix built from its quaternion.
        // Pure rotation matrices always have det = +1; a negative result indicates reflection.
        private float NodeMatrixDeterminant(int nodeIdx)
        {
            var node = Jms.Nodes[nodeIdx];
            var q    = new Quaternion(node.Rotation.I, node.Rotation.J, node.Rotation.K, node.Rotation.W);
            Matrix4x4 m = Matrix4x4.CreateFromQuaternion(q);
            // 3×3 upper-left determinant.
            return m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
                 - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
                 + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
        }

        // -----------------------------------------------------------------------
        // Legacy path: CollisionModel → JMS directly
        // -----------------------------------------------------------------------

        public void Export(CollisionModel coll)
        {
            //geometry
            foreach (var region in coll.Regions)
            {
                foreach(var permutation in region.Permutations)
                {
                    int material_index = -1;
                    foreach(var bsp in permutation.Bsps)
                    {
                        List<Polygon> polygons = new List<Polygon>();
                        for (var i = 0; i < bsp.Geometry.Surfaces.Count; i++)
                            polygons.Add(SurfaceToPolygon(i, bsp.Geometry));

                        material_index = polygons[0].MaterialIndex;

                        //triangulate polygons as needed
                        List<Polygon> triangles = new List<Polygon>();
                        foreach(var polygon in polygons)
                        {
                            if (polygon.Vertices.Count == 3)
                                triangles.Add(polygon);
                            else
                            {
                                List<List<RealPoint3d>> newTriangles = Triangulator.Triangulate(polygon.Vertices, polygon.Plane);
                                foreach (var newTriangle in newTriangles)
                                    triangles.Add(new Polygon
                                    {
                                        Vertices = newTriangle,
                                        Plane = polygon.Plane,
                                        MaterialIndex = polygon.MaterialIndex
                                    });
                            }
                        }

                        //build vertex and index buffers
                        foreach(var triangle in triangles)
                        {
                            List<int> indices = new List<int>();
                            foreach(var point in triangle.Vertices)
                            {
                                indices.Add(Jms.Vertices.Count);
                                Jms.Vertices.Add(new JmsFormat.JmsVertex
                                {
                                    Position = point,
                                    NodeSets = new List<JmsFormat.JmsVertex.NodeSet>
                                            {
                                                new JmsFormat.JmsVertex.NodeSet
                                                {
                                                    NodeIndex = bsp.NodeIndex,
                                                    NodeWeight = 1.0f
                                                }
                                            }
                                });
                            }
                            Jms.Triangles.Add(new JmsFormat.JmsTriangle
                            {
                                VertexIndices = indices,
                                MaterialIndex = Jms.Materials.Count
                            });
                        }
                    }
                    Jms.Materials.Add(new JmsFormat.JmsMaterial
                    {
                        Name = material_index == -1 ? "default" : Cache.StringTable.GetString(coll.Materials[material_index].Name),
                        MaterialName = $"({Jms.Materials.Count + 1}) {Cache.StringTable.GetString(permutation.Name)} {Cache.StringTable.GetString(region.Name)}"
                    });
                }
            }

            //finally, transform points to node space
            foreach (var vert in Jms.Vertices)
            {
                vert.Position = TransformPointByNode(vert.Position, vert.NodeSets[0].NodeIndex);
            }
        }

        struct Polygon
        {
            public List<RealPoint3d> Vertices;
            public RealPlane3d Plane;
            public int MaterialIndex;
        }

        Polygon SurfaceToPolygon(int surface_index, CollisionGeometry bsp)
        {
            List<RealPoint3d> vertices = new List<RealPoint3d>();

            var surface = bsp.Surfaces[surface_index];
            var edge = bsp.Edges[surface.FirstEdge];

            while (true)
            {
                if (edge.LeftSurface == surface_index)
                {
                    vertices.Add(bsp.Vertices[edge.StartVertex].Point * 100.0f);

                    if (edge.ForwardEdge == surface.FirstEdge)
                        break;
                    else
                        edge = bsp.Edges[edge.ForwardEdge];
                }
                else if (edge.RightSurface == surface_index)
                {
                    vertices.Add(bsp.Vertices[edge.EndVertex].Point * 100.0f);

                    if (edge.ReverseEdge == surface.FirstEdge)
                        break;
                    else
                        edge = bsp.Edges[edge.ReverseEdge];
                }
            }

            //offset plane for later use
            RealPlane3d surfacePlane = bsp.Planes[surface.Plane & 0x7FFF].Value;
            surfacePlane = new RealPlane3d(surfacePlane.I, surfacePlane.J, surfacePlane.K, surfacePlane.D * 100.0f);

            return new Polygon
            {
                Vertices = vertices,
                MaterialIndex = surface.MaterialIndex,
                Plane = surfacePlane
            };
        }

        RealPoint3d TransformPointByNode(RealPoint3d point, int node_index)
        {
            Vector3 newPoint = new Vector3(point.X, point.Y, point.Z);
            RealQuaternion nodeRotation = Jms.Nodes[node_index].Rotation;
            RealVector3d nodeTranslation = Jms.Nodes[node_index].Position;
            Matrix4x4 nodeMatrix = Matrix4x4.CreateFromQuaternion(new Quaternion(nodeRotation.I, nodeRotation.J, nodeRotation.K, nodeRotation.W));
            nodeMatrix.Translation = new Vector3(nodeTranslation.I, nodeTranslation.J, nodeTranslation.K);
            newPoint = Vector3.Transform(newPoint, nodeMatrix);
            return new RealPoint3d(newPoint.X, newPoint.Y, newPoint.Z);
        }
    }
}
