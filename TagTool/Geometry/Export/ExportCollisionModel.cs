using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral collision model. Target for JMS coll export.
    /// Collision BSP geometry is inline in the tag (not a pageable resource); no resource
    /// loading is required beyond deserializing the tag itself.
    /// Mirrors donor jms.rs JmsFile::from_collision_model() / from_collision_model_with_skeleton().
    /// </summary>
    public class ExportCollisionModel
    {
        public string TagPath;
        public List<ExportMaterial>        Materials = new List<ExportMaterial>();
        // Skeleton nodes parallel to CollisionModel.Nodes. Used for node-space transforms
        // when exporting with a matching render model skeleton.
        public List<ExportNode>            Nodes     = new List<ExportNode>();
        public List<ExportCollisionRegion> Regions   = new List<ExportCollisionRegion>();
    }

    public class ExportCollisionRegion
    {
        public string Name;
        public List<ExportCollisionPermutation> Permutations = new List<ExportCollisionPermutation>();
    }

    public class ExportCollisionPermutation
    {
        public string Name;
        public List<ExportCollisionBsp> Bsps = new List<ExportCollisionBsp>();
    }

    public class ExportCollisionBsp
    {
        // Skeleton node this BSP is attached to; -1 means world space.
        public int NodeIndex;

        // Vertices in the BSP's local coordinate space.
        // Sourced from CollisionGeometry.Vertices[i].Point (TagTool-deserialized, endian-safe).
        public List<RealPoint3d> Vertices = new List<RealPoint3d>();

        // Surfaces expressed as ordered vertex-index polygons in edge-ring order.
        // The JMS coll exporter fans each polygon into triangles during output.
        public List<ExportCollisionSurface> Surfaces = new List<ExportCollisionSurface>();
    }

    public class ExportCollisionSurface
    {
        public int  MaterialIndex;
        public bool TwoSided;
        public bool Invisible;

        // Vertex indices into ExportCollisionBsp.Vertices, in edge-ring traversal order.
        // Walk: start at Surface.FirstEdge, follow Edge.ForwardEdge / Edge.ReverseEdge
        // until the ring closes. Done by the adapter.
        public List<int> VertexIndices = new List<int>();
    }

    /// <summary>
    /// Adapts a CollisionModel tag into an ExportCollisionModel DTO.
    /// No resource loading required — collision data is inline in the deserialized tag.
    /// </summary>
    public interface IExportCollisionModelAdapter
    {
        ExportCollisionModel AdaptCollisionModel(CollisionModel collisionModel, GameCache cache, string tagPath);
    }
}
