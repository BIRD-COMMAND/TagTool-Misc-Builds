using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RenderModelGen4 = TagTool.Tags.Definitions.Gen4.RenderModel;
using ScenarioLightmapBspGen4 = TagTool.Tags.Definitions.Gen4.ScenarioLightmapBspData;
using TagTool.Tags.Definitions;
using TagTool.Cache;
using TagTool.Geometry;
using TagTool.Common;
using static TagTool.Tags.Definitions.Gen4.StructureDesign.GlobalRenderGeometryStruct.GlobalMeshBlock;
using TagTool.Cache.Resources;
using TagTool.Tags;
using TagTool.Tags.Resources;

namespace TagTool.Commands.Porting.Gen4
{
    public static class RenderModelConverter
    {
        public static RenderModel Convert(GameCache Cache, RenderModelGen4 gen4RenderModel)
        {
            RenderModel result = new RenderModel();

            //convert regions
            result.Regions = new List<RenderModel.Region>();
            if (gen4RenderModel.Regions != null)
            {
                foreach(var reg in gen4RenderModel.Regions)
                {
                    RenderModel.Region newRegion = new RenderModel.Region
                    {
                        Name = reg.Name,
                        Permutations = new List<RenderModel.Region.Permutation>()
                    };
                    if (reg.Permutations != null)
                    {
                        foreach(var perm in reg.Permutations)
                        {
                            newRegion.Permutations.Add(new RenderModel.Region.Permutation
                            {
                                Name = perm.Name,
                                MeshCount = (ushort)perm.MeshCount,
                                MeshIndex = perm.MeshIndex
                            });
                        }
                    }
                    result.Regions.Add(newRegion);
                }
            }

            //convert materials
            result.Materials = new List<RenderMaterial>();
            if (gen4RenderModel.Materials != null)
            {
                foreach(var mat in gen4RenderModel.Materials)
                {
                    result.Materials.Add(new RenderMaterial
                    {
                        RenderMethod = mat.RenderMethod
                    });
                }
            }

            //convert markers
            result.MarkerGroups = new List<RenderModel.MarkerGroup>();
            if (gen4RenderModel.MarkerGroups != null)
            {
                foreach(var mark in gen4RenderModel.MarkerGroups)
                {
                    var newMark = new RenderModel.MarkerGroup
                    {
                        Name = mark.Name,
                        Markers = new List<RenderModel.MarkerGroup.Marker>()
                    };
                    if (mark.Markers != null)
                    {
                        foreach(var marker in mark.Markers)
                        {
                            newMark.Markers.Add(new RenderModel.MarkerGroup.Marker
                            {
                                NodeIndex = (sbyte)marker.NodeIndex,
                                RegionIndex = marker.RegionIndex,
                                PermutationIndex = marker.PermutationIndex,
                                Translation = marker.Translation,
                                Rotation = marker.Rotation,
                                Scale = marker.Scale,
                            });
                        }
                    }
                    result.MarkerGroups.Add(newMark);
                }
            }

            result.Geometry = ConvertGeometry(gen4RenderModel.RenderGeometry);
            result.LightgenLights = new List<RenderModel.SkygenLight>();

            return result;
        }

        // Convert a Sep27 (Halo4280911) shared RenderModel into a gen3-compatible RenderModel
        // ready for JMS export. Sep27 stores its data in *Halo4280911 fields instead of the
        // standard gen3 fields, so those are mapped here.
        public static RenderModel ConvertSep27(GameCache Cache, TagTool.Tags.Definitions.RenderModel sep27Mode)
        {
            RenderModel result = new RenderModel();

            // regions
            result.Regions = new List<RenderModel.Region>();
            if (sep27Mode.RegionsHalo4280911 != null)
            {
                foreach (var reg in sep27Mode.RegionsHalo4280911)
                {
                    var newRegion = new RenderModel.Region
                    {
                        Name = reg.Name,
                        Permutations = new List<RenderModel.Region.Permutation>()
                    };
                    if (reg.Permutations != null)
                    {
                        foreach (var perm in reg.Permutations)
                        {
                            newRegion.Permutations.Add(new RenderModel.Region.Permutation
                            {
                                Name = perm.Name,
                                MeshCount = (ushort)perm.MeshCount,
                                MeshIndex = perm.MeshIndex
                            });
                        }
                    }
                    result.Regions.Add(newRegion);
                }
            }

            // materials (same field name as gen3)
            result.Materials = new List<RenderMaterial>();
            if (sep27Mode.Materials != null)
            {
                foreach (var mat in sep27Mode.Materials)
                {
                    result.Materials.Add(new RenderMaterial
                    {
                        RenderMethod = mat.RenderMethod
                    });
                }
            }

            // markers
            result.MarkerGroups = new List<RenderModel.MarkerGroup>();
            if (sep27Mode.MarkerGroupsHalo4280911 != null)
            {
                foreach (var markGroup in sep27Mode.MarkerGroupsHalo4280911)
                {
                    var newMark = new RenderModel.MarkerGroup
                    {
                        Name = markGroup.Name,
                        Markers = new List<RenderModel.MarkerGroup.Marker>()
                    };
                    if (markGroup.Markers != null)
                    {
                        foreach (var marker in markGroup.Markers)
                        {
                            newMark.Markers.Add(new RenderModel.MarkerGroup.Marker
                            {
                                NodeIndex = (sbyte)marker.NodeIndex,
                                RegionIndex = marker.RegionIndex,
                                PermutationIndex = marker.PermutationIndex,
                                Translation = marker.Translation,
                                Rotation = marker.Rotation,
                                Scale = marker.Scale,
                            });
                        }
                    }
                    result.MarkerGroups.Add(newMark);
                }
            }

            result.Geometry = ConvertGeometryFromSep27(sep27Mode.GeometryHalo4280911);
            result.LightgenLights = new List<RenderModel.SkygenLight>();

            result.InstanceStartingMeshIndex = sep27Mode.InstanceStartingMeshIndex;
            if (sep27Mode.InstancePlacements != null)
                result.InstancePlacements = sep27Mode.InstancePlacements;

            return result;
        }

        // Convert RenderGeometryHalo4280911 (Sep27 inline geometry struct) to a gen3 RenderGeometry.
        // The block element types (GlobalMeshBlock, CompressionInfoBlock) are shared with the
        // regular Gen4 path, so the conversion logic mirrors ConvertGeometry(GlobalRenderGeometryStruct).
        public static RenderGeometry ConvertGeometryFromSep27(TagTool.Tags.Definitions.RenderModel.RenderGeometryHalo4280911 geo)
        {
            RenderGeometry newGeo = new RenderGeometry();

            // compression
            newGeo.Compression = new List<RenderGeometryCompression>();
            if (geo?.CompressionInfo != null)
            {
                foreach (var compress in geo.CompressionInfo)
                {
                    newGeo.Compression.Add(new RenderGeometryCompression
                    {
                        Flags = (RenderGeometryCompressionFlags)compress.CompressionFlags1,
                        X = compress.X,
                        Y = compress.Y,
                        Z = compress.Z,
                        U = compress.U,
                        V = compress.V,
                    });
                }
            }

            // meshes
            newGeo.Meshes = new List<Mesh>();
            if (geo?.Meshes != null)
            {
                foreach (var mesh in geo.Meshes)
                {
                    var newMesh = new Mesh
                    {
                        Parts = new List<Part>(),
                        SubParts = new List<SubPart>(),
                        IndexBufferIndices = new short[2] { mesh.IndexBufferIndex, -1 },
                        VertexBufferIndices = ConvertVertexBufferIndices(mesh.VertexBufferIndices),
                        RigidNodeIndex = mesh.RigidNodeIndex,
                        IndexBufferType = (PrimitiveType)mesh.IndexBufferType,
                        ReachType = (VertexTypeReach)mesh.VertexType
                    };

                    if (mesh.Parts != null)
                    {
                        foreach (var part in mesh.Parts)
                        {
                            newMesh.Parts.Add(new Part
                            {
                                MaterialIndex = part.RenderMethodIndex,
                                TransparentSortingIndex = part.TransparentSortingIndex,
                                FirstIndex = part.IndexStart,
                                IndexCount = part.IndexCount,
                                FirstSubPartIndex = part.SubpartStart,
                                SubPartCount = part.SubpartCount,
                                TypeNew = (Part.PartTypeNew)part.PartType,
                                VertexCount = part.BudgetVertexCount
                            });
                        }
                    }

                    if (mesh.Subparts != null)
                    {
                        foreach (var subpart in mesh.Subparts)
                        {
                            newMesh.SubParts.Add(new SubPart
                            {
                                FirstIndex = subpart.IndexStart,
                                IndexCount = subpart.IndexCount,
                                PartIndex = subpart.PartIndex,
                                VertexCount = subpart.BudgetVertexCount
                            });
                        }
                    }

                    newGeo.Meshes.Add(newMesh);
                }
            }

            newGeo.InstancedGeometryPerPixelLighting = new List<RenderGeometry.StaticPerPixelLighting>();

            // per-mesh node remapping tables (local vertex bone index → global model bone index)
            newGeo.PerMeshNodeMaps = new List<RenderGeometry.PerMeshNodeMap>();
            if (geo?.PerMeshNodeMap != null)
            {
                foreach (var map in geo.PerMeshNodeMap)
                {
                    var newMap = new RenderGeometry.PerMeshNodeMap
                    {
                        NodeIndices = new List<RenderGeometry.PerMeshNodeMap.NodeIndex>()
                    };
                    if (map.NodeMap != null)
                    {
                        foreach (var entry in map.NodeMap)
                            newMap.NodeIndices.Add(new RenderGeometry.PerMeshNodeMap.NodeIndex { Node = (byte)entry.NodeIndex });
                    }
                    newGeo.PerMeshNodeMaps.Add(newMap);
                }
            }

            return newGeo;
        }

        public static RenderGeometry ConvertGeometry(RenderModelGen4.GlobalRenderGeometryStruct gen4Geometry)
        {
            RenderGeometry newGeo = new RenderGeometry();

            //compression
            newGeo.Compression = new List<RenderGeometryCompression>();
            foreach(var compress in gen4Geometry.CompressionInfo)
            {
                newGeo.Compression.Add(new RenderGeometryCompression
                {
                    Flags = (RenderGeometryCompressionFlags)compress.CompressionFlags1,
                    X = compress.X,
                    Y = compress.Y,
                    Z = compress.Z,
                    U = compress.U,
                    V = compress.V,
                });
            }

            //meshes
            newGeo.Meshes = new List<Mesh>();
            foreach(var mesh in gen4Geometry.Meshes)
            {
                var newMesh = new Mesh
                {
                    Parts = new List<Part>(),
                    SubParts = new List<SubPart>(),
                    IndexBufferIndices = new short[2] {mesh.IndexBufferIndex,-1},
                    VertexBufferIndices = ConvertVertexBufferIndices(mesh.VertexBufferIndices),
                    RigidNodeIndex = mesh.RigidNodeIndex,
                    IndexBufferType = (PrimitiveType)mesh.IndexBufferType,
                    ReachType = (VertexTypeReach)mesh.VertexType
                };

                //parts
                foreach(var part in mesh.Parts)
                {
                    newMesh.Parts.Add(new Part
                    {
                        MaterialIndex = part.RenderMethodIndex,
                        TransparentSortingIndex = part.TransparentSortingIndex,
                        FirstIndex = part.IndexStart,
                        IndexCount = part.IndexCount,
                        FirstSubPartIndex = part.SubpartStart,
                        SubPartCount = part.SubpartCount,
                        TypeNew = (Part.PartTypeNew)part.PartType,
                        VertexCount = part.BudgetVertexCount
                    });
                }

                //subparts
                foreach(var subpart in mesh.Subparts)
                {
                    newMesh.SubParts.Add(new SubPart
                    {
                        FirstIndex = subpart.IndexStart,
                        IndexCount = subpart.IndexCount,
                        PartIndex = subpart.PartIndex,
                        VertexCount = subpart.BudgetVertexCount
                    });
                }
                newGeo.Meshes.Add(newMesh);
            }

            newGeo.InstancedGeometryPerPixelLighting = new List<RenderGeometry.StaticPerPixelLighting>();

            return newGeo;
        }

        public static RenderGeometry ConvertGeometry(ScenarioLightmapBspGen4.GlobalRenderGeometryStruct gen4Geometry)
        {
            RenderGeometry newGeo = new RenderGeometry();

            //compression
            newGeo.Compression = new List<RenderGeometryCompression>();
            foreach (var compress in gen4Geometry.CompressionInfo)
            {
                newGeo.Compression.Add(new RenderGeometryCompression
                {
                    Flags = (RenderGeometryCompressionFlags)compress.CompressionFlags1,
                    X = compress.X,
                    Y = compress.Y,
                    Z = compress.Z,
                    U = compress.U,
                    V = compress.V,
                });
            }

            //meshes
            newGeo.Meshes = new List<Mesh>();
            foreach (var mesh in gen4Geometry.Meshes)
            {
                var newMesh = new Mesh
                {
                    Parts = new List<Part>(),
                    SubParts = new List<SubPart>(),
                    IndexBufferIndices = new short[2] { mesh.IndexBufferIndex, -1 },
                    VertexBufferIndices = ConvertVertexBufferIndices(mesh.VertexBufferIndices),
                    RigidNodeIndex = mesh.RigidNodeIndex,
                    IndexBufferType = (PrimitiveType)mesh.IndexBufferType,
                    ReachType = (VertexTypeReach)mesh.VertexType
                };

                //parts
                foreach (var part in mesh.Parts)
                {
                    newMesh.Parts.Add(new Part
                    {
                        MaterialIndex = part.RenderMethodIndex,
                        TransparentSortingIndex = part.TransparentSortingIndex,
                        FirstIndex = part.IndexStart,
                        IndexCount = part.IndexCount,
                        FirstSubPartIndex = part.SubpartStart,
                        SubPartCount = part.SubpartCount,
                        TypeNew = (Part.PartTypeNew)part.PartType,
                        VertexCount = part.BudgetVertexCount
                    });
                }

                //subparts
                foreach (var subpart in mesh.Subparts)
                {
                    newMesh.SubParts.Add(new SubPart
                    {
                        FirstIndex = subpart.IndexStart,
                        IndexCount = subpart.IndexCount,
                        PartIndex = subpart.PartIndex,
                        VertexCount = subpart.BudgetVertexCount
                    });
                }
                newGeo.Meshes.Add(newMesh);
            }

            newGeo.InstancedGeometryPerPixelLighting = new List<RenderGeometry.StaticPerPixelLighting>();

            return newGeo;
        }

        public static RenderGeometryApiResourceDefinition ConvertResource(TagTool.Tags.Resources.Gen4.RenderGeometryApiResourceDefinition gen4Resource)
        {
            RenderGeometryApiResourceDefinition newResource = new RenderGeometryApiResourceDefinition
            {
                VertexBuffers = new TagBlock<D3DStructure<VertexBufferDefinition>>(),
                IndexBuffers = new TagBlock<D3DStructure<IndexBufferDefinition>>()
            };
            foreach (var buffer in gen4Resource.XenonVertexBuffers)
            {
                newResource.VertexBuffers.Add(new D3DStructure<VertexBufferDefinition> { Definition = buffer.Definition });
            }
            foreach (var buffer in gen4Resource.XenonIndexBuffers)
            {
                newResource.IndexBuffers.Add(new D3DStructure<IndexBufferDefinition> { Definition = buffer.Definition });
            }
            return newResource;
        }

        private static short[] ConvertVertexBufferIndices(RenderModelGen4.GlobalRenderGeometryStruct.GlobalMeshBlock.VertexBufferIndicesWordArray[] indicesArray)
        {
            List<short> VertexBufferIndices = new List<short>();
            for (var i = 0; i < indicesArray.Length; i++)
                VertexBufferIndices.Add((short)indicesArray[i].VertexBufferIndex);
            return VertexBufferIndices.ToArray();
        }

        private static short[] ConvertVertexBufferIndices(ScenarioLightmapBspGen4.GlobalRenderGeometryStruct.GlobalMeshBlock.VertexBufferIndicesWordArray[] indicesArray)
        {
            List<short> VertexBufferIndices = new List<short>();
            for (var i = 0; i < indicesArray.Length; i++)
                VertexBufferIndices.Add((short)indicesArray[i].VertexBufferIndex);
            return VertexBufferIndices.ToArray();
        }
    }
}
