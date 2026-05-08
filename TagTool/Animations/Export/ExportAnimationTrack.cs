using TagTool.Common;

namespace TagTool.Animations.Export
{
    /// <summary>
    /// Static track: one transform for the entire animation duration (node does not animate).
    /// Mirrors donor animation/pose.rs static track resolution.
    /// </summary>
    public class ExportStaticTrack
    {
        // Identity defaults: RealQuaternion(0,0,0,1), RealPoint3d(0,0,0), 1.0f.
        public RealQuaternion Rotation;
        public RealPoint3d    Translation;
        public float          Scale = 1.0f;
    }

    /// <summary>
    /// Animated track: per-frame transforms for one skeleton node.
    /// Arrays are parallel to ExportAnimation.FrameCount.
    /// Null array = the node has no data for that channel (use static or identity).
    /// Mirrors donor animation/pose.rs animated track resolution.
    /// </summary>
    public class ExportAnimatedTrack
    {
        public RealQuaternion[] Rotations;    // length == FrameCount, or null
        public RealPoint3d[]    Translations; // length == FrameCount, or null
        public float[]          Scales;       // length == FrameCount, or null
    }
}
