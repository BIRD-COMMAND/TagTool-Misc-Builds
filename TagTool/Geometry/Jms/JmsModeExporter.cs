using System;
using System.Collections.Generic;
using System.Linq;
using TagTool.Tags.Definitions;
using System.IO;
using TagTool.Cache;
using TagTool.Common;
using System.Numerics;
using Assimp;
using TagTool.Geometry;
using TagTool.Pathfinding;
using TagTool.Geometry.Export;

namespace TagTool.Geometry.Jms
{
    public class JmsModeExporter
    {
        GameCache Cache { get; set; }
        JmsFormat Jms { get; set; }

        public JmsModeExporter(GameCache cacheContext, JmsFormat jms)
        {
            Cache = cacheContext;
            Jms = jms;
        }

        // -----------------------------------------------------------------------
        // New path: ExportRenderModel DTO → JMS
        // Nodes are already in Jms.Nodes (built by ExportJMSCommand).
        // Adds: markers, materials, vertices, triangles.
        // -----------------------------------------------------------------------

        public void Export(ExportRenderModel dto)
        {
            int markerCount = 0;
            int meshCount   = 0;
            int vertexCount = 0;
            int triCount    = 0;

            // Markers
            foreach (var m in dto.Markers)
            {
                markerCount++;
                Jms.Markers.Add(new JmsFormat.JmsMarker
                {
                    Name        = m.GroupName,
                    NodeIndex   = m.NodeIndex,
                    Rotation    = m.Rotation,
                    Translation = new RealVector3d(m.Translation.X, m.Translation.Y, m.Translation.Z) * 100.0f,
                    Radius      = m.Scale <= 0.0f ? 1.0f : m.Scale,
                });
            }

            // Materials: ShaderPath = JMS Name, LightmapPath = cell label "(slot) perm region".
            // JMS export uses donor blam-tags scale factor 100.0 for positions.
            for (int i = 0; i < dto.Materials.Count; i++)
            {
                var mat  = dto.Materials[i];
                int slot = Jms.Materials.Count + 1;
                Jms.Materials.Add(new JmsFormat.JmsMaterial
                {
                    Name         = mat.ShaderPath,
                    MaterialName = $"({slot}) {mat.LightmapPath}",
                });
            }

            // Geometry: region → permutation → mesh (one mesh per part)
            // Vertices are shared within each ExportMesh — preserves the tag's smooth/hard
            // edge encoding so importers get correct shading without manual fixups.
            foreach (var region in dto.Regions)
            {
                foreach (var perm in region.Permutations)
                {
                    foreach (var mesh in perm.Meshes)
                    {
                        meshCount++;
                        int vertBase = Jms.Vertices.Count;

                        foreach (var v in mesh.Vertices)
                        {
                            Jms.Vertices.Add(ToJmsVertex(v));
                            vertexCount++;
                        }

                        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                        {
                            Jms.Triangles.Add(new JmsFormat.JmsTriangle
                            {
                                MaterialIndex = mesh.MaterialIndex,
                                VertexIndices = new List<int>
                                {
                                    vertBase + mesh.Indices[t],
                                    vertBase + mesh.Indices[t + 1],
                                    vertBase + mesh.Indices[t + 2],
                                },
                            });
                            triCount++;
                        }
                    }
                }
            }

            Console.WriteLine($"  Tag:       {dto.TagPath}");
            Console.WriteLine($"  Nodes:     {Jms.Nodes.Count}");
            Console.WriteLine($"  Materials: {dto.Materials.Count}");
            Console.WriteLine($"  Regions:   {dto.Regions.Count}");
            Console.WriteLine($"  Meshes:    {meshCount}");
            Console.WriteLine($"  Vertices:  {vertexCount}");
            Console.WriteLine($"  Triangles: {triCount}");
            Console.WriteLine($"  Markers:   {markerCount}");
            Console.WriteLine($"  Instances: {dto.InstancePlacements.Count} (geometry not extracted)");
        }

        // Converts ExportVertex to JmsVertex.
        // JMS export uses donor blam-tags scale factor 100.0 for positions.
        // V coordinate is flipped (1 - v) to match Halo bitmap convention.
        private static JmsFormat.JmsVertex ToJmsVertex(ExportVertex v)
        {
            var jv = new JmsFormat.JmsVertex
            {
                Position = v.Position * 100.0f,
                Normal   = new RealVector3d(v.Normal.I, v.Normal.J, v.Normal.K),
                NodeSets = new List<JmsFormat.JmsVertex.NodeSet>(),
                UvSets   = new List<JmsFormat.JmsVertex.UvSet>
                {
                    new JmsFormat.JmsVertex.UvSet
                    {
                        TextureCoordinates = new RealPoint2d(v.TexCoords[0].I, 1.0f - v.TexCoords[0].J),
                    }
                },
            };
            for (int i = 0; i < 4; i++)
            {
                if (v.BoneWeights[i] > 0.0f)
                    jv.NodeSets.Add(new JmsFormat.JmsVertex.NodeSet
                    {
                        NodeIndex  = v.BoneIndices[i],
                        NodeWeight = v.BoneWeights[i],
                    });
            }
            return jv;
        }

        // -----------------------------------------------------------------------
        // Legacy path: RenderModel → JMS directly
        // -----------------------------------------------------------------------

        public void Export(RenderModel mode)
        {
            Export(mode, null);
        }

        internal void Export(RenderModel mode, Sep27RenderModelAnalysis sep27Analysis)
        {
            //build markers
            if (mode.MarkerGroups != null)
            {
                foreach (var markergroup in mode.MarkerGroups)
                {
                    var name = Cache.StringTable.GetString(markergroup.Name);
                    foreach (var marker in markergroup.Markers)
                    {
                        Jms.Markers.Add(new JmsFormat.JmsMarker
                        {
                            Name = name,
                            NodeIndex = marker.NodeIndex,
                            Rotation = marker.Rotation,
                            Translation = new RealVector3d(marker.Translation.X, marker.Translation.Y, marker.Translation.Z) * 100.0f,
                            Radius = marker.Scale <= 0.0 ? 1.0f : marker.Scale //needs to not be zero
                        });
                    }
                }
            }

            List<JmsFormat.JmsMaterial> materialList = new List<JmsFormat.JmsMaterial>();

            //build meshes — vertices are shared within each mesh to preserve smooth/hard edge encoding
            if (mode.Regions != null)
            {
                foreach (var region in mode.Regions)
                {
                    if (region.Permutations == null)
                        continue;

                    string regionName = Cache.StringTable.GetString(region.Name) ?? string.Empty;
                    foreach (var perm in region.Permutations)
                    {
                        if (perm.MeshIndex == -1)
                            continue;

                        int effectiveMeshCount = GetEffectiveMeshCount(region, perm, sep27Analysis != null);
                        if (effectiveMeshCount <= 0)
                            continue;

                        string permutationName = Cache.StringTable.GetString(perm.Name) ?? string.Empty;
                        for (int meshOffset = 0; meshOffset < effectiveMeshCount; meshOffset++)
                        {
                            int meshIndex = perm.MeshIndex + meshOffset;
                            EmitMesh(mode, meshIndex, permutationName, regionName, materialList);
                        }
                    }
                }
            }

            // Instance placement geometry (modular armor, rigid attachments).
            // Each InstancePlacements[i] pairs with SubParts[i] of the instance mesh.
            // Geometry is transformed by each placement's (Forward, Left, Up, Position, Scale)
            // basis and rigidly bound to placement.NodeIndex — matching blam-tags behaviour.
            EmitInstancePlacements(mode, materialList);

            EmitSep27OrphanMeshes(mode, sep27Analysis, materialList);

            //add materials
            foreach (var material in materialList)
                Jms.Materials.Add(new JmsFormat.JmsMaterial
                {
                    Name = material.Name,
                    MaterialName = $"({Jms.Materials.Count + 1}) {material.MaterialName}"
                });

            //add skylights
            if (mode.LightgenLights != null)
            {
                foreach (var skylight in mode.LightgenLights)
                    Jms.Skylights.Add(new JmsFormat.JmsSkylight
                    {
                        Direction = skylight.Direction,
                        RadiantIntensity = new RealVector3d(skylight.RadiantIntensity.Red, skylight.RadiantIntensity.Green, skylight.RadiantIntensity.Blue),
                        SolidAngle = skylight.Magnitude
                    });
            }
        }

        private int GetEffectiveMeshCount(
            RenderModel.Region region,
            RenderModel.Region.Permutation permutation,
            bool strictSep27MeshCounts)
        {
            if (permutation.MeshCount == 65535)
                return 0;

            if (strictSep27MeshCounts)
                return permutation.MeshCount > 0 ? permutation.MeshCount : 0;

            if (permutation.MeshCount == 0)
            {
#if DEBUG
                Console.WriteLine($"[JmsModeExporter] Warning: permutation '{Cache.StringTable.GetString(permutation.Name)}' in region '{Cache.StringTable.GetString(region.Name)}' has MeshCount==0; defaulting to 1. May indicate uninitialized permutation data.");
#endif
                return 1;
            }

            return permutation.MeshCount;
        }

        private void EmitMesh(
            RenderModel mode,
            int meshIndex,
            string permutationName,
            string regionName,
            List<JmsFormat.JmsMaterial> materialList)
        {
            if (mode?.Geometry?.Meshes == null || meshIndex < 0 || meshIndex >= mode.Geometry.Meshes.Count)
                return;

            var mesh = mode.Geometry.Meshes[meshIndex];
            var meshReader = new MeshReader(Cache, mesh);
            var vertices = ReadMeshVertices(mode, meshIndex, mesh, meshReader);

            int jmsVertexBase = Jms.Vertices.Count;
            foreach (var vertex in vertices)
                Jms.Vertices.Add(ToJmsVertex(vertex));

            string materialCellLabel = $"{permutationName} {regionName}";
            for (int partIndex = 0; partIndex < mesh.Parts.Count; partIndex++)
            {
                int newMaterialIndex = -1;
                int partMaterialIndex = mesh.Parts[partIndex].MaterialIndex;
                if (partMaterialIndex != -1)
                {
                    string renderMaterialName = ResolveRenderMaterialName(mode, partMaterialIndex);
                    newMaterialIndex = GetOrAddMaterial(materialList, renderMaterialName, materialCellLabel);
                }

                var indices = ModelExtractor.ReadIndices(meshReader, mesh.Parts[partIndex]);
                for (int indexOffset = 0; indexOffset + 2 < indices.Length; indexOffset += 3)
                {
                    Jms.Triangles.Add(new JmsFormat.JmsTriangle
                    {
                        VertexIndices = new List<int>
                        {
                            jmsVertexBase + indices[indexOffset],
                            jmsVertexBase + indices[indexOffset + 1],
                            jmsVertexBase + indices[indexOffset + 2],
                        },
                        MaterialIndex = newMaterialIndex
                    });
                }
            }
        }

        private List<ModelExtractor.GenericVertex> ReadMeshVertices(
            RenderModel mode,
            int meshIndex,
            Mesh mesh,
            MeshReader meshReader)
        {
            List<ModelExtractor.GenericVertex> vertices;
            if (Cache.Version >= CacheVersion.HaloReach)
            {
                vertices = ModelExtractor.ReadVerticesReach(meshReader);
                if (mesh.ReachType == VertexTypeReach.Rigid || mesh.ReachType == VertexTypeReach.RigidCompressed)
                {
                    vertices.ForEach(vertex => vertex.Indices = new byte[4] { (byte)mesh.RigidNodeIndex, 0, 0, 0 });
                    vertices.ForEach(vertex => vertex.Weights = new float[4] { 1, 0, 0, 0 });
                }

                if (mode.Geometry.PerMeshNodeMaps != null && meshIndex < mode.Geometry.PerMeshNodeMaps.Count)
                {
                    var nodeMap = mode.Geometry.PerMeshNodeMaps[meshIndex]?.NodeIndices;
                    if (nodeMap != null && nodeMap.Count > 0)
                    {
                        foreach (var vertex in vertices)
                        {
                            if (vertex.Indices == null)
                                continue;

                            for (int index = 0; index < vertex.Indices.Length; index++)
                            {
                                if (vertex.Indices[index] < nodeMap.Count)
                                    vertex.Indices[index] = nodeMap[vertex.Indices[index]].Node;
                            }
                        }
                    }
                }
            }
            else
            {
                vertices = ModelExtractor.ReadVertices(meshReader);
                if (mesh.Type == VertexType.Rigid)
                {
                    vertices.ForEach(vertex => vertex.Indices = new byte[4] { (byte)mesh.RigidNodeIndex, 0, 0, 0 });
                    vertices.ForEach(vertex => vertex.Weights = new float[4] { 1, 0, 0, 0 });
                }
            }

            ModelExtractor.DecompressVertices(vertices, new VertexCompressor(mode.Geometry.Compression[0]));
            return vertices;
        }

        private static JmsFormat.JmsVertex ToJmsVertex(ModelExtractor.GenericVertex vertex)
        {
            var newVertex = new JmsFormat.JmsVertex
            {
                Position = new RealPoint3d(vertex.Position.X, vertex.Position.Y, vertex.Position.Z) * 100.0f,
                Normal = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z)),
                NodeSets = new List<JmsFormat.JmsVertex.NodeSet>(),
                UvSets = new List<JmsFormat.JmsVertex.UvSet>
                {
                    new JmsFormat.JmsVertex.UvSet
                    {
                        TextureCoordinates = new RealPoint2d(vertex.TexCoords.X, 1 - vertex.TexCoords.Y)
                    }
                }
            };

            if (vertex.Weights != null)
            {
                for (int weightIndex = 0; weightIndex < vertex.Weights.Length; weightIndex++)
                {
                    if (vertex.Weights[weightIndex] == 0.0f)
                        continue;

                    newVertex.NodeSets.Add(new JmsFormat.JmsVertex.NodeSet
                    {
                        NodeIndex = vertex.Indices[weightIndex],
                        NodeWeight = vertex.Weights[weightIndex]
                    });
                }
            }

            return newVertex;
        }

        private int GetOrAddMaterial(List<JmsFormat.JmsMaterial> materialList, string renderMaterialName, string materialCellLabel)
        {
            int existingIndex = materialList.FindIndex(material =>
                material.Name == renderMaterialName &&
                material.MaterialName == materialCellLabel);

            if (existingIndex >= 0)
                return existingIndex;

            materialList.Add(new JmsFormat.JmsMaterial
            {
                Name = renderMaterialName,
                MaterialName = materialCellLabel
            });

            return materialList.Count - 1;
        }

        internal static string ResolveRenderMaterialName(RenderModel mode, int materialIndex)
        {
            if (mode?.Materials == null || materialIndex < 0 || materialIndex >= mode.Materials.Count)
                return "default";

            CachedTag renderMethod = mode.Materials[materialIndex].RenderMethod;
            if (renderMethod == null)
                return "default";

            string renderMethodName = renderMethod.Name ?? $"{renderMethod.Group.Tag}_0x{renderMethod.Index:X4}";
            foreach (var delimiter in new[] { "\\shaders\\", "\\materials\\", "\\" })
            {
                string[] nameParts = renderMethodName.Split(new[] { delimiter }, StringSplitOptions.None);
                if (nameParts.Length > 1)
                    return nameParts.Last();
            }

            return renderMethodName;
        }

        private void EmitSep27OrphanMeshes(
            RenderModel mode,
            Sep27RenderModelAnalysis sep27Analysis,
            List<JmsFormat.JmsMaterial> materialList)
        {
            if (sep27Analysis?.OrphanMeshes == null || sep27Analysis.OrphanMeshes.Count == 0)
                return;

            var emittedMeshIndices = new List<int>();
            foreach (var orphanMesh in sep27Analysis.OrphanMeshes)
            {
                EmitMesh(
                    mode,
                    orphanMesh.MeshIndex,
                    orphanMesh.SyntheticPermutationName,
                    Sep27RenderModelAnalyzer.SyntheticOrphanRegionName,
                    materialList);
                emittedMeshIndices.Add(orphanMesh.MeshIndex);
            }

            if (emittedMeshIndices.Count > 0)
            {
                Console.WriteLine(
                    $"[JmsModeExporter] Sep27 orphan mesh recovery emitted meshes: {string.Join(", ", emittedMeshIndices)}");
            }
        }

        private Vector3 PointToVector(RealPoint3d point)
        {
            return new Vector3(point.X, point.Y, point.Z);
        }

        private class Triangle
        {
            public List<int> VertexIndices = new List<int>();
            public List<ModelExtractor.GenericVertex> Vertices = new List<ModelExtractor.GenericVertex>();
            public Vector3 Normal;
            public int MaterialIndex;
        }

        public static Vector3[] CalculateVertexNormals(Vector3[] vertices, ushort[] indices)
        {
            Vector3[] vertexNormals = new Vector3[vertices.Length];

            // Initialize all vertex normals to zero
            for (int i = 0; i < vertexNormals.Length; i++)
            {
                vertexNormals[i] = Vector3.Zero;
            }

            // Calculate face normals and add them to vertex normals
            for (int i = 0; i < indices.Length; i += 3)
            {
                int index1 = indices[i];
                int index2 = indices[i + 1];
                int index3 = indices[i + 2];

                Vector3 v1 = vertices[index1];
                Vector3 v2 = vertices[index2];
                Vector3 v3 = vertices[index3];

                Vector3 faceNormal = Vector3.Cross(v2 - v1, v3 - v1);

                vertexNormals[index1] += faceNormal;
                vertexNormals[index2] += faceNormal;
                vertexNormals[index3] += faceNormal;
            }

            // Normalize vertex normals
            for (int i = 0; i < vertexNormals.Length; i++)
            {
                vertexNormals[i] = Vector3.Normalize(vertexNormals[i]);
            }

            return vertexNormals;
        }

        private Vector3 Vector3fromVector3D(Vector3D input)
        {
            return new Vector3(input.X, input.Y, input.Z);
        }
        private Vector3D Vector3DfromVector3(Vector3 input)
        {
            return new Vector3D(input.X, input.Y, input.Z);
        }

        // -----------------------------------------------------------------------
        // Instance placement extraction
        // Mirrors blam-tags append_instance_geometry():
        //   placement i  →  SubParts[i] of Meshes[InstanceStartingMeshIndex]
        //   vertex transform: newPos = Forward*(x*s) + Left*(y*s) + Up*(z*s) + Position
        //   normal transform: rotate by same 3×3 (no scaling)
        //   bone override: rigidly bound to placement.NodeIndex
        // -----------------------------------------------------------------------

        private void EmitInstancePlacements(RenderModel mode, List<JmsFormat.JmsMaterial> materialList)
        {
            if (mode.InstancePlacements == null || mode.InstancePlacements.Count == 0) return;
            if (mode.InstanceStartingMeshIndex < 0 || mode.InstanceStartingMeshIndex >= mode.Geometry.Meshes.Count) return;

            var instanceMesh = mode.Geometry.Meshes[mode.InstanceStartingMeshIndex];
            var instanceMeshReader = new MeshReader(Cache, instanceMesh);
            var indexBuffer = instanceMeshReader.IndexBuffers[0];
            if (indexBuffer == null) return;

            List<ModelExtractor.GenericVertex> verts;
            if (Cache.Version >= CacheVersion.HaloReach)
            {
                verts = ModelExtractor.ReadVerticesReach(instanceMeshReader);
                if (instanceMesh.ReachType == VertexTypeReach.Rigid || instanceMesh.ReachType == VertexTypeReach.RigidCompressed)
                    verts.ForEach(v => { v.Indices = new byte[] { (byte)instanceMesh.RigidNodeIndex, 0, 0, 0 }; v.Weights = new float[] { 1f, 0, 0, 0 }; });
            }
            else
            {
                verts = ModelExtractor.ReadVertices(instanceMeshReader);
                if (instanceMesh.Type == VertexType.Rigid)
                    verts.ForEach(v => { v.Indices = new byte[] { (byte)instanceMesh.RigidNodeIndex, 0, 0, 0 }; v.Weights = new float[] { 1f, 0, 0, 0 }; });
            }
            ModelExtractor.DecompressVertices(verts, new VertexCompressor(mode.Geometry.Compression[0]));

            for (int ii = 0; ii < mode.InstancePlacements.Count; ii++)
            {
                var placement = mode.InstancePlacements[ii];
                if (ii >= instanceMesh.SubParts.Count) continue;
                var subpart = instanceMesh.SubParts[ii];
                if ((int)subpart.IndexCount <= 0) continue;

                ushort[] subpartIndices;
                try
                {
                    var idxStream = instanceMeshReader.OpenIndexBufferStream(indexBuffer);
                    idxStream.Position = subpart.FirstIndex;
                    subpartIndices = indexBuffer.Format == IndexBufferFormat.TriangleStrip
                        ? idxStream.ReadTriangleStrip(subpart.IndexCount)
                        : idxStream.ReadIndices(subpart.IndexCount);
                }
                catch { continue; }

                if (subpartIndices.Length < 3) continue;

                string shaderName = "default";
                if (subpart.PartIndex >= 0 && subpart.PartIndex < instanceMesh.Parts.Count)
                {
                    var part = instanceMesh.Parts[subpart.PartIndex];
                    shaderName = ResolveRenderMaterialName(mode, part.MaterialIndex);
                }

                string placementName = Cache.StringTable.GetString(placement.Name) ?? $"instance_{ii}";
                int matIdx = materialList.Count;
                materialList.Add(new JmsFormat.JmsMaterial
                {
                    Name = shaderName,
                    MaterialName = placementName,
                });

                float s  = placement.Scale <= 0f ? 1f : placement.Scale;
                float fI = placement.Forward.X, fJ = placement.Forward.Y, fK = placement.Forward.Z;
                float lI = placement.Left.X,    lJ = placement.Left.Y,    lK = placement.Left.Z;
                float uI = placement.Up.X,      uJ = placement.Up.Y,      uK = placement.Up.Z;
                float pX = placement.Position.X, pY = placement.Position.Y, pZ = placement.Position.Z;

                int vertBase = Jms.Vertices.Count;
                var remap = new Dictionary<ushort, int>();

                for (int t = 0; t + 2 < subpartIndices.Length; t += 3)
                {
                    ushort i0 = subpartIndices[t], i1 = subpartIndices[t + 1], i2 = subpartIndices[t + 2];
                    if (i0 >= verts.Count || i1 >= verts.Count || i2 >= verts.Count) continue;

                    int r0 = GetOrAddInstanceVertex(remap, i0, verts, fI, fJ, fK, lI, lJ, lK, uI, uJ, uK, pX, pY, pZ, s, placement.NodeIndex);
                    int r1 = GetOrAddInstanceVertex(remap, i1, verts, fI, fJ, fK, lI, lJ, lK, uI, uJ, uK, pX, pY, pZ, s, placement.NodeIndex);
                    int r2 = GetOrAddInstanceVertex(remap, i2, verts, fI, fJ, fK, lI, lJ, lK, uI, uJ, uK, pX, pY, pZ, s, placement.NodeIndex);

                    Jms.Triangles.Add(new JmsFormat.JmsTriangle
                    {
                        VertexIndices = new List<int> { vertBase + r0, vertBase + r1, vertBase + r2 },
                        MaterialIndex = matIdx,
                    });
                }
            }
        }

        private int GetOrAddInstanceVertex(
            Dictionary<ushort, int> remap, ushort vi,
            List<ModelExtractor.GenericVertex> verts,
            float fI, float fJ, float fK,
            float lI, float lJ, float lK,
            float uI, float uJ, float uK,
            float pX, float pY, float pZ,
            float scale, int boneIndex)
        {
            if (remap.TryGetValue(vi, out int local)) return local;
            local = remap.Count;
            remap[vi] = local;

            var gv = verts[vi];
            float sx = gv.Position.X * scale, sy = gv.Position.Y * scale, sz = gv.Position.Z * scale;

            var cn = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(gv.Normal.X, gv.Normal.Y, gv.Normal.Z));

            var jv = new JmsFormat.JmsVertex
            {
                Position = new RealPoint3d(
                    fI * sx + lI * sy + uI * sz + pX,
                    fJ * sx + lJ * sy + uJ * sz + pY,
                    fK * sx + lK * sy + uK * sz + pZ) * 100.0f,
                Normal = new RealVector3d(
                    fI * cn.I + lI * cn.J + uI * cn.K,
                    fJ * cn.I + lJ * cn.J + uJ * cn.K,
                    fK * cn.I + lK * cn.J + uK * cn.K),
                NodeSets = new List<JmsFormat.JmsVertex.NodeSet>(),
                UvSets = new List<JmsFormat.JmsVertex.UvSet>
                {
                    new JmsFormat.JmsVertex.UvSet
                    {
                        TextureCoordinates = new RealPoint2d(gv.TexCoords.X, 1f - gv.TexCoords.Y)
                    }
                },
            };

            if (boneIndex >= 0)
                jv.NodeSets.Add(new JmsFormat.JmsVertex.NodeSet { NodeIndex = (byte)boneIndex, NodeWeight = 1f });

            Jms.Vertices.Add(jv);
            return local;
        }
    }
}
