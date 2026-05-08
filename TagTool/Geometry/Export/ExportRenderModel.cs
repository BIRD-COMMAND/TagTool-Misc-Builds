using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral render model. Target for JMS mode export.
    /// Populated by IExportRenderModelAdapter from TagTool's deserialized RenderModel tag
    /// and its associated RenderGeometryApiResourceDefinition resource.
    /// </summary>
    public class ExportRenderModel
    {
        public string TagPath;
        public List<ExportNode>             Nodes               = new List<ExportNode>();
        public List<ExportMaterial>         Materials           = new List<ExportMaterial>();
        public List<ExportMarker>           Markers             = new List<ExportMarker>();
        public List<ExportRegion>           Regions             = new List<ExportRegion>();
        public List<ExportInstancePlacement> InstancePlacements = new List<ExportInstancePlacement>();

        // Global compression bounds read from RenderGeometry.Compression[0].
        // Per-instance bounds live on each ExportInstancePlacement.
        public ExportCompressionBounds GlobalCompression;
    }

    public class ExportRegion
    {
        public string Name;
        public List<ExportPermutation> Permutations = new List<ExportPermutation>();
    }

    public class ExportPermutation
    {
        public string Name;
        public int MeshIndex;
        public int MeshCount;
        // Decoded meshes (triangle-list, decompressed vertices). Populated by the adapter.
        public List<ExportMesh> Meshes = new List<ExportMesh>();
    }

    /// <summary>
    /// An instance of shared mesh geometry in the render model.
    /// In donor jms.rs this is handled by append_instance_geometry().
    /// </summary>
    public class ExportInstancePlacement
    {
        public string Name;
        public int    NodeIndex;
        public float  Scale;

        // Row vectors from RenderModel.InstancePlacement (already deserialized by TagTool).
        public RealPoint3d Forward;
        public RealPoint3d Left;
        public RealPoint3d Up;
        public RealPoint3d Position;

        // Per-instance compression bounds. Sourced from the instance mesh's own compression_info
        // rather than the global RenderGeometry.Compression[0].
        public ExportCompressionBounds Compression;
        public List<ExportMesh> Meshes = new List<ExportMesh>();
    }

    /// <summary>
    /// Adapts a TagTool RenderModel tag into an ExportRenderModel DTO.
    /// Implementation must call Cache.ResourceCache.GetRenderGeometryApiResourceDefinition()
    /// and VertexCompressor.DecompressPosition() / DecompressUv() for vertex data.
    /// </summary>
    public interface IExportRenderModelAdapter
    {
        /// <summary>
        /// Returns null and logs a warning if the render geometry resource cannot be loaded.
        /// tagPath is the human-readable tag path used for diagnostics (typically CachedTag.Name).
        /// </summary>
        ExportRenderModel AdaptRenderModel(RenderModel renderModel, GameCache cache, string tagPath);
    }
}
