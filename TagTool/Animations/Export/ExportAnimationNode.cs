namespace TagTool.Animations.Export
{
    /// <summary>
    /// Exporter-neutral skeleton node for animation export.
    /// Mirrors donor animation/pose.rs SkeletonNode.
    /// </summary>
    public class ExportAnimationNode
    {
        public string Name;
        public int ParentIndex;      // -1 = no parent (root)
        public int FirstChildIndex;  // -1 = no children
        public int NextSiblingIndex; // -1 = no next sibling
    }
}
