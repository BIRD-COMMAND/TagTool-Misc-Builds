using System.Collections.Generic;

namespace TagTool.Animations.Export
{
    /// <summary>
    /// Root-motion type classification matching donor animation/jma.rs MovementKind.
    /// </summary>
    public enum ExportMovementKind
    {
        None,
        DxDy,
        DxDyDyaw,
        DxDyDzDyaw,
        DxDyDzDangleAxis,
    }

    /// <summary>
    /// Per-frame root motion delta. Values are in world units (NOT pre-scaled by 100).
    /// Mirrors donor animation/mod.rs MovementFrame.
    /// </summary>
    public class ExportMovementFrame
    {
        public float Dx, Dy, Dz; // position deltas in world units
        public float Dyaw;       // yaw delta in radians
    }

    /// <summary>
    /// Root motion data for a single animation.
    /// Mirrors donor animation/mod.rs MovementData.
    /// </summary>
    public class ExportMovementData
    {
        public ExportMovementKind      Kind;
        public List<ExportMovementFrame> Frames = new List<ExportMovementFrame>();
    }
}
