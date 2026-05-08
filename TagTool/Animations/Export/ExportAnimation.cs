namespace TagTool.Animations.Export
{
    /// <summary>
    /// Animation type classification matching donor animation/jma.rs JmaKind logic.
    /// Determines which JMA-family extension (.jmm, .jma, .jmt, .jmz, .jmo, .jmr, .jmw) to emit.
    /// </summary>
    public enum ExportAnimationType
    {
        Base,
        Overlay,
        Replacement,
        WorldRelative,
    }

    /// <summary>
    /// Frame info type — controls movement data embedding in the JMA family.
    /// Maps to ModelAnimationGraph.Animation's frame_info_type field.
    /// </summary>
    public enum ExportAnimationFrameInfoType
    {
        None,
        Dx,
        DxDy,
        DxDyDyaw,
        DxDyDzDyaw,
        DxDyDzDangleAxis,
    }

    /// <summary>
    /// Exporter-neutral animation. Carries decoded or decodable per-node transforms.
    /// Mirrors donor animation/mod.rs AnimationClip and jma.rs Pose.
    /// </summary>
    public class ExportAnimation
    {
        public string Name;
        public ExportAnimationType          Type;
        public ExportAnimationFrameInfoType FrameInfoType;
        public bool  WorldRelative;
        public int   FrameCount;
        public float FrameRate = 30.0f; // Halo default; 30 fps if not stored in the tag

        // Node flags indexed parallel to ExportAnimationGraph.Nodes.
        // true = the corresponding track carries actual data for this animation.
        // Populated from the bit-packed node flag arrays in the codec stream.
        // TODO Phase3: parse node flags from codec stream header (donor animation/codec.rs read_node_flags).
        public bool[] HasStaticRotation;
        public bool[] HasStaticTranslation;
        public bool[] HasStaticScale;
        public bool[] HasAnimatedRotation;
        public bool[] HasAnimatedTranslation;
        public bool[] HasAnimatedScale;

        // Decoded per-node tracks. Null = "use identity/default for this node."
        // TODO Phase3: populated by codec decoders (all 12 codec variants in donor animation/codec.rs).
        //              Left null until codec porting is complete.
        public ExportStaticTrack[]   StaticTracks;
        public ExportAnimatedTrack[] AnimatedTracks;

        public ExportMovementData Movement;

        // Raw codec blob preserved for Phase 3 decoding.
        //
        // IMPORTANT — BYTE ORDER UNVERIFIED:
        // TODO: Before porting donor animation/codec.rs, verify codec stream byte order per generation:
        //   - HaloOnline / MCC PC: expected little-endian (matches donor assumptions)
        //   - Halo 3 / ODST / Reach packaged cache: byte order of animation resource data must be
        //     confirmed by inspecting ResourceCacheGen3 extraction vs. donor codec field reads.
        //   If Gen3 resources are big-endian, all u16/u32 reads in the codec decoders need BE variants.
        public byte[] RawCodecData;

        // Codec type index (donor animation/codec.rs Codec enum).
        // 0=NoCompression, 1=UncompressedStatic, 2=UncompressedAnimated,
        // 3=EightByteQuantizedRotationOnly, 4-7=Keyframe variants, 8=BlendScreen,
        // 9-11=Reach curve codecs.
        public int CodecType;
    }
}
