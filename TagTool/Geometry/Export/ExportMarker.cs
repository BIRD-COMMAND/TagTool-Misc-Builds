using TagTool.Common;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral marker. Belongs to a named marker group.
    /// Mirrors donor jms.rs JmsMarker.
    /// </summary>
    public class ExportMarker
    {
        public string GroupName;
        public int    RegionIndex;
        public int    PermutationIndex;
        public int    NodeIndex;
        public RealPoint3d    Translation;
        public RealQuaternion Rotation;
        public float          Scale;
    }
}
