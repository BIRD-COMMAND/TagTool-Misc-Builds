using TagTool.Common;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral skeleton node. Used by render model, collision model, and physics model exporters.
    /// Mirrors donor jms.rs JmsNode.
    /// </summary>
    public class ExportNode
    {
        public string Name;
        public int ParentIndex;      // -1 = root
        public int FirstChildIndex;  // -1 = no children
        public int NextSiblingIndex; // -1 = no next sibling

        // Default pose in parent-local space. Sourced from RenderModel.Node fields directly
        // (TagTool deserialization handles endian). Not yet multiplied by scale × 100.
        public RealQuaternion DefaultRotation;
        public RealPoint3d    DefaultTranslation;
        public float          DefaultScale;
    }
}
