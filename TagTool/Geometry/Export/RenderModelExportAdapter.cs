using System;
using System.Collections.Generic;
using System.Linq;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Export
{
    public class RenderModelExportAdapter : IExportRenderModelAdapter
    {
        public ExportRenderModel AdaptRenderModel(RenderModel mode, GameCache cache, string tagPath)
        {
            if (mode  == null) throw new ArgumentNullException(nameof(mode));
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            var em = new ExportRenderModel { TagPath = tagPath };

            // Global compression bounds from RenderGeometry.Compression[0].
            if (mode.Geometry.Compression != null && mode.Geometry.Compression.Count > 0)
            {
                var c = mode.Geometry.Compression[0];
                em.GlobalCompression = ExportCompressionBounds.FromRenderGeometryCompression(c.X, c.Y, c.Z, c.U, c.V);
            }
            else
            {
                Console.Error.WriteLine($"[RenderModelExportAdapter] No compression info on {tagPath}; using identity bounds.");
                em.GlobalCompression = ExportCompressionBounds.Identity;
            }

            BuildNodes(em, mode, cache);

            var partMaterialMap = new Dictionary<(int meshIndex, int partIndex), int>(64);
            BuildMaterials(em, mode, cache, partMaterialMap);

            BuildMarkers(em, mode, cache);
            BuildGeometry(em, mode, cache, partMaterialMap);
            BuildInstancePlacements(em, mode, cache);

            return em;
        }

        // -----------------------------------------------------------------------
        // Nodes: raw local-space data only — world-space chaining is done
        // by ExportJMSCommand.BuildNodesFromMode (unchanged).
        // -----------------------------------------------------------------------

        private static void BuildNodes(ExportRenderModel em, RenderModel mode, GameCache cache)
        {
            if (mode.Nodes == null) return;
            foreach (var node in mode.Nodes)
            {
                var name = cache.StringTable.GetString(node.Name) ?? string.Empty;
                if (!name.StartsWith("b_", StringComparison.Ordinal))
                    name = "b_" + name;

                em.Nodes.Add(new ExportNode
                {
                    Name              = name,
                    ParentIndex       = node.ParentNode,
                    FirstChildIndex   = node.FirstChildNode,
                    NextSiblingIndex  = node.NextSiblingNode,
                    DefaultRotation   = node.DefaultRotation,
                    DefaultTranslation = new RealPoint3d(node.DefaultTranslation.X, node.DefaultTranslation.Y, node.DefaultTranslation.Z),
                    DefaultScale      = 1.0f,
                });
            }
        }

        // -----------------------------------------------------------------------
        // Materials: region × permutation × part walk.
        // ShaderPath  = shader basename (JMS Name field).
        // LightmapPath = "permName regionName" cell label (JMS MaterialName suffix).
        // -----------------------------------------------------------------------

        private static void BuildMaterials(ExportRenderModel em, RenderModel mode, GameCache cache,
            Dictionary<(int, int), int> partMaterialMap)
        {
            if (mode.Regions == null) return;

            var seen = new Dictionary<(string shaderBasename, string cellLabel), int>(32);

            foreach (var region in mode.Regions)
            {
                var regionName = cache.StringTable.GetString(region.Name) ?? string.Empty;
                if (region.Permutations == null) continue;

                foreach (var perm in region.Permutations)
                {
                    var permName = cache.StringTable.GetString(perm.Name) ?? string.Empty;
                    if (perm.MeshIndex < 0 || perm.MeshCount == 0 || perm.MeshCount == 65535) continue;

                    for (int mi = 0; mi < perm.MeshCount; mi++)
                    {
                        int meshIndex = perm.MeshIndex + mi;
                        if (meshIndex >= mode.Geometry.Meshes.Count) continue;

                        var mesh = mode.Geometry.Meshes[meshIndex];
                        for (int pi = 0; pi < mesh.Parts.Count; pi++)
                        {
                            var part = mesh.Parts[pi];
                            string shaderBasename = ResolveShaderBasename(mode, part.MaterialIndex);
                            string cellLabel      = $"{permName} {regionName}";
                            var   key             = (shaderBasename, cellLabel);

                            if (!seen.TryGetValue(key, out int matIdx))
                            {
                                matIdx     = em.Materials.Count;
                                seen[key]  = matIdx;
                                em.Materials.Add(new ExportMaterial
                                {
                                    ShaderPath   = shaderBasename,
                                    LightmapPath = cellLabel,  // repurposed: holds JMS cell label
                                });
                            }
                            partMaterialMap[(meshIndex, pi)] = matIdx;
                        }
                    }
                }
            }
        }

        private static string ResolveShaderBasename(RenderModel mode, int materialIndex)
        {
            if (materialIndex < 0 || materialIndex >= mode.Materials.Count)
                return "default";
            var renderMethod = mode.Materials[materialIndex]?.RenderMethod;
            if (renderMethod == null)
                return "default";

            var rmName = renderMethod.Name ?? $"{renderMethod.Group.Tag}_0x{renderMethod.Index:X4}";
            string[] delimiters = { "\\shaders\\", "\\materials\\", "\\" };
            foreach (var delim in delimiters)
            {
                var parts = rmName.Split(new[] { delim }, StringSplitOptions.None);
                if (parts.Length > 1)
                    return parts.Last();
            }
            return rmName;
        }

        // -----------------------------------------------------------------------
        // Markers
        // -----------------------------------------------------------------------

        private static void BuildMarkers(ExportRenderModel em, RenderModel mode, GameCache cache)
        {
            if (mode.MarkerGroups == null) return;
            foreach (var group in mode.MarkerGroups)
            {
                var groupName = cache.StringTable.GetString(group.Name) ?? string.Empty;
                if (group.Markers == null) continue;
                foreach (var m in group.Markers)
                {
                    em.Markers.Add(new ExportMarker
                    {
                        GroupName        = groupName,
                        NodeIndex        = m.NodeIndex,
                        RegionIndex      = m.RegionIndex,
                        PermutationIndex = m.PermutationIndex,
                        Translation      = m.Translation,
                        Rotation         = m.Rotation,
                        Scale            = m.Scale,
                    });
                }
            }
        }

        // -----------------------------------------------------------------------
        // Geometry: regions → permutations → meshes → parts.
        // Vertex data sourced from TagTool's MeshReader + VertexCompressor.
        // Strip → triangle-list conversion handled by ModelExtractor.ReadIndices.
        // -----------------------------------------------------------------------

        private static void BuildGeometry(ExportRenderModel em, RenderModel mode, GameCache cache,
            Dictionary<(int, int), int> partMaterialMap)
        {
            if (mode.Regions == null) return;

            // Build compressor once; pass null to DecompressVertices if not available.
            VertexCompressor compressor = null;
            if (mode.Geometry.Compression != null && mode.Geometry.Compression.Count > 0)
                compressor = new VertexCompressor(mode.Geometry.Compression[0]);
            else
                Console.Error.WriteLine("[RenderModelExportAdapter] Compression block empty — vertices will be un-decompressed.");

            bool useReach = cache.Version >= CacheVersion.HaloReach;

            foreach (var region in mode.Regions)
            {
                var regionName   = cache.StringTable.GetString(region.Name) ?? string.Empty;
                var exportRegion = new ExportRegion { Name = regionName };

                if (region.Permutations != null)
                {
                    foreach (var perm in region.Permutations)
                    {
                        var permName = cache.StringTable.GetString(perm.Name) ?? string.Empty;
                        if (perm.MeshIndex < 0 || perm.MeshCount == 0 || perm.MeshCount == 65535) continue;

                        var exportPerm = new ExportPermutation
                        {
                            Name      = permName,
                            MeshIndex = perm.MeshIndex,
                            MeshCount = perm.MeshCount,
                        };

                        for (int mi = 0; mi < perm.MeshCount; mi++)
                        {
                            int meshIndex = perm.MeshIndex + mi;
                            if (meshIndex >= mode.Geometry.Meshes.Count) continue;

                            ExtractMeshParts(exportPerm, mode, cache, meshIndex,
                                compressor, useReach, partMaterialMap, permName, regionName, mi);
                        }

                        exportRegion.Permutations.Add(exportPerm);
                    }
                }
                em.Regions.Add(exportRegion);
            }
        }

        private static void ExtractMeshParts(
            ExportPermutation exportPerm,
            RenderModel       mode,
            GameCache         cache,
            int               meshIndex,
            VertexCompressor  compressor,
            bool              useReach,
            Dictionary<(int, int), int> partMaterialMap,
            string permName,
            string regionName,
            int    meshOffset)
        {
            var mesh       = mode.Geometry.Meshes[meshIndex];
            var meshReader = new MeshReader(cache, mesh);

            List<ModelExtractor.GenericVertex> vertices;
            if (useReach)
            {
                vertices = ModelExtractor.ReadVerticesReach(meshReader);
                if (mesh.ReachType == VertexTypeReach.Rigid || mesh.ReachType == VertexTypeReach.RigidCompressed)
                {
                    vertices.ForEach(v => { v.Indices = new byte[] { (byte)mesh.RigidNodeIndex, 0, 0, 0 }; v.Weights = new float[] { 1, 0, 0, 0 }; });
                }
            }
            else
            {
                vertices = ModelExtractor.ReadVertices(meshReader);
                if (mesh.Type == VertexType.Rigid)
                {
                    vertices.ForEach(v => { v.Indices = new byte[] { (byte)mesh.RigidNodeIndex, 0, 0, 0 }; v.Weights = new float[] { 1, 0, 0, 0 }; });
                }
            }

            ModelExtractor.DecompressVertices(vertices, compressor);

            // Per-mesh node map (remaps local bone index → global skeleton index).
            RenderGeometry.PerMeshNodeMap nodeMap = null;
            if (mode.Geometry.PerMeshNodeMaps != null && mode.Geometry.PerMeshNodeMaps.Count > meshIndex)
                nodeMap = mode.Geometry.PerMeshNodeMaps[meshIndex];

            for (int pi = 0; pi < mesh.Parts.Count; pi++)
            {
                var part   = mesh.Parts[pi];
                int matIdx = partMaterialMap.TryGetValue((meshIndex, pi), out var m) ? m : -1;

                ushort[] rawIndices;
                try   { rawIndices = ModelExtractor.ReadIndices(meshReader, part); }
                catch { continue; }

                if (rawIndices.Length < 3) continue;

                var exportMesh = new ExportMesh
                {
                    MaterialIndex = matIdx,
                    PartLabel     = $"{permName}:{regionName}:mesh{meshOffset}:part{pi}",
                };

                // Share vertices: remap original vertex buffer index → ExportMesh-local index.
                // The tag's vertex buffer encodes smooth/hard edges via split normals —
                // same position with different normals = hard edge. Preserving vertex sharing
                // keeps that encoding intact so importers see correct shading without fixups.
                var vertexRemap = new Dictionary<ushort, int>();
                for (int t = 0; t + 2 < rawIndices.Length; t += 3)
                {
                    ushort i0 = rawIndices[t], i1 = rawIndices[t + 1], i2 = rawIndices[t + 2];
                    if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count)
                        continue;

                    int r0 = GetOrAddVertex(exportMesh, vertices, vertexRemap, i0, nodeMap, mode.Nodes.Count);
                    int r1 = GetOrAddVertex(exportMesh, vertices, vertexRemap, i1, nodeMap, mode.Nodes.Count);
                    int r2 = GetOrAddVertex(exportMesh, vertices, vertexRemap, i2, nodeMap, mode.Nodes.Count);

                    exportMesh.Indices.Add(r0);
                    exportMesh.Indices.Add(r1);
                    exportMesh.Indices.Add(r2);
                }

                if (exportMesh.Indices.Count > 0)
                    exportPerm.Meshes.Add(exportMesh);
            }
        }

        private static int GetOrAddVertex(
            ExportMesh exportMesh,
            List<ModelExtractor.GenericVertex> vertices,
            Dictionary<ushort, int> vertexRemap,
            ushort vi,
            RenderGeometry.PerMeshNodeMap nodeMap,
            int totalNodes)
        {
            if (!vertexRemap.TryGetValue(vi, out int localIdx))
            {
                localIdx = exportMesh.Vertices.Count;
                vertexRemap[vi] = localIdx;
                exportMesh.Vertices.Add(ConvertVertex(vertices[vi], nodeMap, totalNodes));
            }
            return localIdx;
        }

        private static ExportVertex ConvertVertex(
            ModelExtractor.GenericVertex gv,
            RenderGeometry.PerMeshNodeMap nodeMap,
            int totalNodes)
        {
            // Normals (and tangents/binormals) are stored as UShort4N in the Halo 3/Reach MCC
            // cache vertex buffer, giving values in [0, 1] after DenormalizeUnsigned. The signed
            // [-1, 1] normal requires the two's-complement reinterpretation ConvertVectorSpace
            // applies: value ≤ 0.5 → 2v (positive half), value > 0.5 → (v-1)*2 (negative half).
            // DecompressVertices does not touch normals, so we apply the conversion here.
            var ev = new ExportVertex
            {
                Position = new RealPoint3d(gv.Position.X, gv.Position.Y, gv.Position.Z),
                Normal   = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(gv.Normal.X,    gv.Normal.Y,    gv.Normal.Z)),
                Tangent  = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(gv.Tangents.X,  gv.Tangents.Y,  gv.Tangents.Z)),
                Binormal = VertexBufferConverter.ConvertVectorSpace(new RealVector3d(gv.Binormals.X, gv.Binormals.Y, gv.Binormals.Z)),
            };
            ev.TexCoords[0] = new RealVector2d(gv.TexCoords.X, gv.TexCoords.Y);

            if (gv.Indices != null && gv.Weights != null)
            {
                int limit = Math.Min(4, Math.Min(gv.Indices.Length, gv.Weights.Length));
                for (int i = 0; i < limit; i++)
                {
                    int boneIdx = gv.Indices[i];
                    if (nodeMap != null && boneIdx < nodeMap.NodeIndices.Count)
                        boneIdx = nodeMap.NodeIndices[boneIdx].Node;
                    ev.BoneIndices[i] = Math.Min(boneIdx, Math.Max(0, totalNodes - 1));
                    ev.BoneWeights[i] = gv.Weights[i];
                }
            }
            return ev;
        }

        // -----------------------------------------------------------------------
        // Instance placements.
        // Full geometry extraction (reading subpart index ranges from the instance
        // mesh and transforming vertices by the placement basis) is deferred.
        // The placement metadata is populated here so diagnostics and future
        // writers can see it; Meshes[] on each placement remains empty.
        //
        // TODO: implement geometry extraction for non-decorator render_models:
        //   mesh = mode.Geometry.Meshes[mode.InstanceStartingMeshIndex]
        //   for each placement i: read SubParts[i].FirstIndex/IndexCount, extract
        //   vertex slice, apply (Forward, Left, Up, Position, Scale) transform,
        //   override bone weights to single bone (NodeIndex).
        //   See donor jms.rs append_instance_geometry().
        // -----------------------------------------------------------------------

        private static void BuildInstancePlacements(ExportRenderModel em, RenderModel mode, GameCache cache)
        {
            if (mode.InstancePlacements == null || mode.InstancePlacements.Count == 0) return;
            if (mode.InstanceStartingMeshIndex < 0) return;

            Console.Error.WriteLine(
                $"[RenderModelExportAdapter] {mode.InstancePlacements.Count} instance placements found; " +
                $"geometry extraction not yet implemented — placements recorded as metadata only.");

            foreach (var ip in mode.InstancePlacements)
            {
                em.InstancePlacements.Add(new ExportInstancePlacement
                {
                    Name        = cache.StringTable.GetString(ip.Name) ?? string.Empty,
                    NodeIndex   = ip.NodeIndex,
                    Scale       = ip.Scale,
                    Forward     = ip.Forward,
                    Left        = ip.Left,
                    Up          = ip.Up,
                    Position    = ip.Position,
                    Compression = em.GlobalCompression,
                });
            }
        }
    }
}
