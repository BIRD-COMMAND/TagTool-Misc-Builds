using TagTool.Cache;
using TagTool.Common;
using TagTool.Geometry;
using System;
using System.Collections.Generic;
using static TagTool.Tags.TagFieldFlags;
using Gen4Defs = TagTool.Tags.Definitions.Gen4;

namespace TagTool.Tags.Definitions
{
    [TagStructure(Name = "render_model", Tag = "mode", Size = 0x1B4, MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo3Beta)]
    [TagStructure(Name = "render_model", Tag = "mode", Size = 0x1CC, MinVersion = CacheVersion.Halo3Retail, MaxVersion = CacheVersion.HaloOnline700123)]
    [TagStructure(Name = "render_model", Tag = "mode", Size = 0x350, MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911, Platform = CachePlatform.Original)]
    [TagStructure(Name = "render_model", Tag = "mode", Size = 0x258, MinVersion = CacheVersion.HaloReach, Platform = CachePlatform.Original)]
    [TagStructure(Name = "render_model", Tag = "mode", Size = 0x264, MinVersion = CacheVersion.HaloReach, Platform = CachePlatform.MCC)]
    public class RenderModel : TagStructure
    {
        public StringId Name;
        public FlagsValue Flags;
        public short Version;
        public int Checksum;

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public List<CharacterLightingLod> CharacterLightingLods;

        [TagField(MaxVersion = CacheVersion.Halo4220811)]
        public List<Region> Regions;

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public List<Gen4Defs.RenderModel.RenderModelRegionBlock> RegionsHalo4280911;

        [TagField(MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo4220811)]
        public int Unknown18;

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public sbyte L1SectionGroupIndex;
        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public sbyte L2SectionGroupIndex;
        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911, Length = 2, Flags = Padding)]
        public byte[] Halo4280911Padding0;

        [TagField(MinVersion = CacheVersion.Halo3Beta)]
        public int InstanceStartingMeshIndex;

        [TagField(MinVersion = CacheVersion.Halo3Beta)]
        public List<InstancePlacement> InstancePlacements;

        public int NodeListChecksum;
        [TagField(MaxVersion = CacheVersion.Halo4220811)]
        public List<Node> Nodes;

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public List<Gen4Defs.RenderModel.RenderModelNodeBlock> NodesHalo4280911;

        [TagField(MaxVersion = CacheVersion.Halo4220811)]
        public List<MarkerGroup> MarkerGroups;

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public List<Gen4Defs.RenderModel.RenderModelMarkerGroupBlock> MarkerGroupsHalo4280911;
        public List<RenderMaterial> Materials;

        [TagField(Flags = Padding, Length = 12, MaxVersion = CacheVersion.Halo4220811)]
        public byte[] Unused; // "Errors" block

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public List<Gen4Defs.RenderModel.GlobalErrorReportCategoriesBlock> ErrorsHalo4280911;

        public float DontDrawOverCameraCosineAngle;

        [TagField(MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo4220811)]
        [TagField(MinVersion = CacheVersion.Halo4E3)]
        public RenderGeometry Geometry = new RenderGeometry();

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public RenderGeometryHalo4280911 GeometryHalo4280911;

        [TagField(MinVersion = CacheVersion.HaloReach)]
        public List<short> NodeMapMapping;
        [TagField(MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo4280911)]
        public List<SkygenLight> LightgenLights; // first index is sun

        [TagField(Length = 16, MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo4280911)]
        public float[] SHRed = new float[SphericalHarmonics.Order3Count];
        [TagField(Length = 16, MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo4280911)]
        public float[] SHGreen = new float[SphericalHarmonics.Order3Count];
        [TagField(Length = 16, MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo4280911)]
        public float[] SHBlue = new float[SphericalHarmonics.Order3Count];
        [TagField(Length = 16, MinVersion = CacheVersion.HaloReach, MaxVersion = CacheVersion.Halo4280911)]
        public float[] VmfLightProbe;
        [TagField(MinVersion = CacheVersion.HaloReach, MaxVersion = CacheVersion.Halo4280911)]
        public RealVector3d AnalyticalLightDirection;
        [TagField(MinVersion = CacheVersion.HaloReach, MaxVersion = CacheVersion.Halo4280911)]
        public RealRgbColor AnalyticalLightColor;
        [TagField(MinVersion = CacheVersion.HaloReach, MaxVersion = CacheVersion.Halo4280911)]
        public float DirectSunLightMultiplier;
        [TagField(MinVersion = CacheVersion.HaloReach, MaxVersion = CacheVersion.Halo4280911)]
        public float SkyDomeAndAllBounceLightMultiplier;
        [TagField(MinVersion = CacheVersion.HaloReach, MaxVersion = CacheVersion.Halo4280911)]
        public float Sun1stBounceScaler;
        [TagField(MinVersion = CacheVersion.HaloReach, MaxVersion = CacheVersion.Halo4280911)]
        public float SkyLight1stBounceScaler;


        [TagField(MinVersion = CacheVersion.Halo3Retail)]
        public List<VolumeSamplesBlock> VolumeSamples;

        [TagField(MinVersion = CacheVersion.Halo3Retail)]
        public List<RuntimeNodeOrientation> RuntimeNodeOrientations;

        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
        public List<Gen4Defs.RenderModel.RenderModelBoneGroupBlock> BoneGroupsHalo4280911;

        [TagField(MinVersion = CacheVersion.Halo4E3, ValidTags = new[] { "smet" })]
        public CachedTag StructureMetaData;
        [TagField(MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911, ValidTags = new[] { "Lbsp" })]
        public CachedTag LightmapBspDataReference;
        [TagField(MinVersion = CacheVersion.Halo4E3, ValidTags = new[] { "rmla" })]
        public CachedTag ForgeLightmapAtlases;

        [TagStructure(Size = 0x94)]
        public class CharacterLightingLod : TagStructure
        {
            [TagField(Flags = Padding, Length = 0x94)]
            public byte[] Data;
        }

        [Flags]
        public enum FlagsValue : ushort
        {
            None = 0,
            ForceThirdPerson = 1 << 0,
            ForceCarmackReverse = 1 << 1,
            ForceNodeMaps = 1 << 2,
            GeometryPostprocessed = 1 << 3,
            Bit4 = 1 << 4,
            Bit5 = 1 << 5,
            Bit6 = 1 << 6,
            Bit7 = 1 << 7,
            Bit8 = 1 << 8,
            Bit9 = 1 << 9,
            Bit10 = 1 << 10,
            Bit11 = 1 << 11,
            Bit12 = 1 << 12,
            Bit13 = 1 << 13,
            Bit14 = 1 << 14,
            Bit15 = 1 << 15
        }

        /// <summary>
        /// A region of a model.
        /// </summary>
        [TagStructure(Size = 0x10)]
        public class Region : TagStructure
		{
            /// <summary>
            /// The name of the region.
            /// </summary>
            [TagField(Flags = Label)]
            public StringId Name;

            /// <summary>
            /// The node map offset of the region.
            /// </summary>
            [TagField(MaxVersion = CacheVersion.Halo2PC)]
            public short NodeMapOffset;

            /// <summary>
            /// The node map size of the region.
            /// </summary>
            [TagField(MaxVersion = CacheVersion.Halo2PC)]
            public short NodeMapSize;

            /// <summary>
            /// The permutations belonging to the region.
            /// </summary>
            public List<Permutation> Permutations;

            /// <summary>
            /// A permutation of a region, associating a specific mesh with it.
            /// </summary>
            [TagStructure(Size = 0x10, MaxVersion = CacheVersion.Halo3Retail, Platform = CachePlatform.Original)]
            [TagStructure(Size = 0x18, MinVersion = CacheVersion.Halo3ODST)]
            [TagStructure(Size = 0x18, MinVersion = CacheVersion.Halo3Retail, Platform = CachePlatform.MCC)]
            public class Permutation : TagStructure
			{
                /// <summary>
                /// The name of the permutation as a string id.
                /// </summary>
                [TagField(Flags = Label)]
                public StringId Name;

                /// <summary>
                /// The level-of-detail section indices of the permutation.
                /// </summary>
                [TagField(Length = 6, MaxVersion = CacheVersion.Halo2PC)]
                public short[] LodSectionIndices = new short[6];

                /// <summary>
                /// The index of the first mesh belonging to the permutation.
                /// </summary>
                [TagField(MinVersion = CacheVersion.Halo3Beta)]
                public short MeshIndex;

                /// <summary>
                /// The number of meshes belonging to the permutation.
                /// </summary>
                [TagField(MinVersion = CacheVersion.Halo3Beta)]
                public ushort MeshCount;

                [TagField(MinVersion = CacheVersion.Halo3Beta)]
                public int Unknown8;

                [TagField(MinVersion = CacheVersion.Halo3Beta)]
                public int UnknownC;

                [TagField(MinVersion = CacheVersion.Halo3ODST)]
                [TagField(MinVersion = CacheVersion.Halo3Retail, Platform = CachePlatform.MCC)]
                public int Unknown10;

                [TagField(MinVersion = CacheVersion.Halo3ODST)]
                [TagField(MinVersion = CacheVersion.Halo3Retail, Platform = CachePlatform.MCC)]
                public int Unknown14;
            }
        }

        [Flags]
        public enum SectionLightingFlags : ushort
        {
            None,
            HasLightmapTexcoords = 1 << 0,
            HasLightmapIncRad = 1 << 1,
            HasLightmapColors = 1 << 2,
            HasLightmapPrt = 1 << 3
        }

        [Flags]
        public enum SectionFlags : ushort
        {
            None,
            GeometryPostprocessed = 1 << 0
        }

        [TagStructure(Size = 0x5C, MaxVersion = CacheVersion.Halo2PC)]
        public class Section : TagStructure
		{
            public RenderGeometryClassification GlobalGeometryClassification;

            [TagField(Flags = Padding, Length = 2)]
            public byte[] Unused = new byte[2];

            public ushort TotalVertexCount;
            public ushort TotalTriangleCount;
            public ushort TotalPartCount;
            public ushort ShadowCastingTriangleCount;
            public ushort ShadowCastingPartCount;
            public ushort OpaquePointCount;
            public ushort OpaqueVertexCount;
            public ushort OpaquePartCount;
            public ushort OpaqueMaxNodesVertex;
            public ushort ShadowCastingRigidTriangleCount;
            public RenderGeometryClassification GeometryClassification;
            public RenderGeometryCompressionFlags GeometryCompressionFlags;
            public List<RenderGeometryCompression> Compression;
            public byte HardwareNodeCount;
            public byte NodeMapSize;
            public ushort SoftwarePlaneSount;
            public ushort TotalSubpartCount;
            public SectionLightingFlags LightingFlags;
            public short RigidNode;
            public SectionFlags Flags;
            public List<Mesh> Meshes;
            public DatumHandle BlockOffset;
            public int BlockSize;
            public uint SectionDataSize;
            public uint ResourceDataSize;
            public List<TagResourceGen2> Resources;

            [TagField(Flags = Short)]
            public CachedTag Original;

            public short OwnerTagSectionOffset;
            public byte RuntimeLinked;
            public byte RuntimeLoaded;

            [TagField(Flags = Short)]
            public CachedTag Runtime;
        }

        [TagStructure(Size = 0xC, MaxVersion = CacheVersion.Halo2PC)]
        public class InvalidSectionPairBit : TagStructure
		{
            public int Bits;
			public int Unknown_0x04;
			public int Unknown_0x08;
		}

		[TagStructure(Size = 0xC, MaxVersion = CacheVersion.Halo2PC)]
        public class SectionGroup : TagStructure
		{
            public DetailLevelFlags DetailLevels;
            public short Unknown;
            public List<CompoundNode> CompoundNodes;

            [Flags]
            public enum DetailLevelFlags : ushort
            {
                None,
                Level1 = 1 << 0,
                Level2 = 1 << 1,
                Level3 = 1 << 2,
                Level4 = 1 << 3,
                Level5 = 1 << 4,
                Level6 = 1 << 5
            }

            [TagStructure(Size = 0x10, MaxVersion = CacheVersion.Halo2PC)]
            public class CompoundNode : TagStructure
			{
                [TagField(Length = 4)]
                public sbyte[] NodeIndices = new sbyte[4];

                [TagField(Length = 3)]
                public float[] NodeWeights = new float[3];
            }
        }

        [Flags]
        public enum InstancePlacementFlags : ushort
        {
            None = 0,
            Bit0 = 1 << 0,
            Bit1 = 1 << 1,
            Bit2 = 1 << 2,
            Bit3 = 1 << 3,
            Bit4 = 1 << 4,
            Bit5 = 1 << 5,
            Bit6 = 1 << 6,
            Bit7 = 1 << 7,
            Bit8 = 1 << 8,
            Bit9 = 1 << 9,
            Bit10 = 1 << 10,
            Bit11 = 1 << 11,
            Bit12 = 1 << 12,
            Bit13 = 1 << 13,
            Bit14 = 1 << 14,
            Bit15 = 1 << 15
        }

        [TagStructure(Size = 0x3C, MinVersion = CacheVersion.Halo3Beta)]
        public class InstancePlacement : TagStructure
		{
            public StringId Name;
            public int NodeIndex;
            public float Scale;
            public RealPoint3d Forward;
            public RealPoint3d Left;
            public RealPoint3d Up;
            public RealPoint3d Position;
        }

        [Flags]
        public enum NodeFlags : ushort
        {
            None = 0,
            ForceDeterministic = 1 << 0,
            ForceRenderThread = 1 << 1
        }

        [TagStructure(Size = 0x60)]
        public class Node : TagStructure
		{
            public StringId Name;
            public short ParentNode;
            public short FirstChildNode;
            public short NextSiblingNode;
            public NodeFlags Flags;
            public RealPoint3d DefaultTranslation;
            public RealQuaternion DefaultRotation;
            public float DefaultScale;
            public RealMatrix4x3 Inverse;
            public float DistanceFromParent;
        }

        [TagStructure(Size = 0x1)]
		public class NodeIndex : TagStructure
		{
            public byte Node;
        }

        [TagStructure(Size = 0xC, MaxVersion = CacheVersion.Halo2PC)]
        [TagStructure(Size = 0x10, MinVersion = CacheVersion.Halo3Beta)]
        public class MarkerGroup : TagStructure
		{
            public StringId Name;
            public List<Marker> Markers;

            [TagStructure(Size = 0x24, MaxVersion = CacheVersion.HaloOnline700123)]
            [TagStructure(Size = 0x30, MinVersion = CacheVersion.HaloReach)]
            public class Marker : TagStructure
			{
                public sbyte RegionIndex;
                public sbyte PermutationIndex;
                public sbyte NodeIndex;

                [TagField(MaxVersion = CacheVersion.Halo3ODST, Length = 0x1, Flags = Padding)]
                public byte[] Padding0;

                [TagField(MinVersion = CacheVersion.HaloOnlineED)]
                public ReachMarkerFlagsDefinition Flags;

                public RealPoint3d Translation;
                public RealQuaternion Rotation;
                public float Scale;
                [TagField(MinVersion = CacheVersion.HaloReach)]
                public RealPoint3d Direction;
            }

            [Flags]
            public enum ReachMarkerFlagsDefinition : byte
            {
                HasNodeRelativeDirection = 1 << 0
            }
        }

        [TagStructure(Size = 0x1C)]
        public class SkygenLight : TagStructure
		{
            public RealVector3d Direction;
            public RealRgbColor RadiantIntensity;
            public float Magnitude;
        }

        [TagStructure(Size = 0x150)]
        public class VolumeSamplesBlock : TagStructure
		{
            public RealPoint3d Position;
            [TagField(Length = 81, MinVersion = CacheVersion.Halo3Beta)]
            public float[] Coefficients = new float[81];
        }

        [TagStructure(Align = 0x10, Size = 0x20)]
        public class RuntimeNodeOrientation : TagStructure
		{
            public RealQuaternion Rotation;
            public RealPoint3d Translation;
            public float Scale;
        }

        [TagStructure(Size = 0x16C)]
        public class RenderGeometryHalo4280911 : TagStructure
        {
            public RenderGeometryFlagsValue RuntimeFlags;

            // Sep27 monolithic runtime layout includes an inline virtual-geometry struct here.
            // Keep it as raw bytes for now until that nested struct is fully named/mapped.
            [TagField(Length = 0xD0, Flags = Padding)]
            public byte[] VirtualGeometry;

            public List<GlobalMeshBlockSep27> Meshes;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.PcameshIndexBlock> PcaMeshIndices;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.CompressionInfoBlock> CompressionInfo;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.SortingPositionBlock> PartSortingPosition;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.UserDataBlock> UserData;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.PerMeshRawDataBlock> PerMeshTemporary;

            [TagField(Length = 0xC, Flags = Padding)]
            public byte[] Padding0;

            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.PerMeshNodeMapBlock> PerMeshNodeMap;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.PerMeshSubpartVisibilityBlock> PerMeshSubpartVisibility;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.PerMeshPrtDataBlock> PerMeshPrtData;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.PerInstanceLightmapTexcoordsBlock> PerInstanceLightmapTexcoords;
            public TagResourceReference ApiResource;
            public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.ShapenameBlock> Shapenames;

            [Flags]
            public enum RenderGeometryFlagsValue : uint
            {
                Processed = 1 << 0,
                Available = 1 << 1,
                HasValidBudgets = 1 << 2,
                ManualResourceCreation = 1 << 3,
                KeepRawGeometry = 1 << 4,
                DontUseCompressedVertexPositions = 1 << 5,
                PcaAnimationTableSorted = 1 << 6,
                NeedsNoLightmapUvs = 1 << 7,
                AlwaysNeedsLightmapUvs = 1 << 8
            }

            // Sep27 global_mesh_block is 0x6C (lacks CloneIndex + CumulativePartCount vs release 0x70).
            [TagStructure(Size = 0x6C)]
            public class GlobalMeshBlockSep27 : TagStructure
            {
                public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.PartBlock> Parts;
                public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.SubpartBlock> Subparts;
                [TagField(Length = 9)]
                public Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.VertexBufferIndicesWordArray[] VertexBufferIndices;
                public short IndexBufferIndex;
                public short IndexBufferTessellation;
                public Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.MeshFlags MeshFlags1;
                public sbyte RigidNodeIndex;
                public Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.MeshVertexType VertexType;
                public Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.MeshTransferVertexType PrtVertexType;
                public Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.MeshLightingPolicyType LightingPolicy;
                public Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.MeshIndexBufferType IndexBufferType;
                [TagField(Length = 0x1, Flags = Padding)]
                public byte[] MeshPadding;
                public short PcaMeshIndex;
                public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.GlobalInstanceBucketBlock> InstanceBuckets;
                public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.IndicesWordBlock> WaterIndicesStart;
                public float RuntimeBoundingRadius;
                public RealPoint3d RuntimeBoundingOffset;
                public List<Gen4Defs.RenderModel.GlobalRenderGeometryStruct.GlobalMeshBlock.VertexkeyBlock> VertexKeys;
            }
        }
    }
}
