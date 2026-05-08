using System.Collections.Generic;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral triangle mesh. Indices are always triangle-list (strips are converted
    /// upstream by GeometryUtils.StripToTriangleList before this DTO is populated).
    /// </summary>
    public class ExportMesh
    {
        public List<ExportVertex> Vertices = new List<ExportVertex>();

        // Triangle list; every 3 elements form one triangle.
        public List<int> Indices = new List<int>();

        // Index into the parent model/BSP material list.
        public int MaterialIndex;

        // Human-readable label for diagnostic logging (e.g. "region:permutation", "cluster 3").
        public string PartLabel;
    }
}
