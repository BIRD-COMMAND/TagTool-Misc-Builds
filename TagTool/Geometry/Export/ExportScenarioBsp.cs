using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Geometry.BspCollisionGeometry;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral scenario structure BSP. Target for ASS export.
    /// Mirrors donor ass.rs AssFile::from_scenario_structure_bsp().
    /// </summary>
    public class ExportScenarioBsp
    {
        public string TagPath;
        public List<ExportMaterial>                      Materials                 = new List<ExportMaterial>();
        public List<ExportBspCluster>                    Clusters                  = new List<ExportBspCluster>();
        public List<ExportBspClusterPortal>              ClusterPortals            = new List<ExportBspClusterPortal>();
        public List<ExportBspInstancedGeometryDef>       InstancedGeometryDefs     = new List<ExportBspInstancedGeometryDef>();
        public List<ExportBspInstancedGeometryPlacement> InstancedGeometryPlacements = new List<ExportBspInstancedGeometryPlacement>();
        public List<ExportBspWeatherPolyhedron>          WeatherPolyhedra          = new List<ExportBspWeatherPolyhedron>();

        // Collision BSP mesh exported with the @CollideOnly / +collision sentinel material.
        public List<RealPoint3d>          CollisionVertices = new List<RealPoint3d>();
        public List<ExportBspCollisionSurface> CollisionSurfaces = new List<ExportBspCollisionSurface>();

        public List<ExportMarker>              Markers            = new List<ExportMarker>();
        public List<ExportBspEnvironmentObject> EnvironmentObjects = new List<ExportBspEnvironmentObject>();
    }

    public class ExportBspCluster
    {
        public int ClusterIndex;
        // World-space meshes for this cluster. Compression bounds = Identity (clusters are world-space).
        public List<ExportMesh> Meshes = new List<ExportMesh>();
    }

    public class ExportBspClusterPortal
    {
        // Vertices in fan order (world space). Fan-triangulate with GeometryUtils.TriangleFanToList
        // when writing the ASS OBJECTS section.
        public List<RealPoint3d> Vertices = new List<RealPoint3d>();
    }

    public class ExportBspInstancedGeometryDef
    {
        public int DefinitionIndex;
        // Per-definition compression bounds (NOT identity; sourced from the instance mesh's compression_info).
        public ExportCompressionBounds Compression;
        // Meshes in definition-local space (NOT world space). Instances are placed via ExportBspInstancedGeometryPlacement.Transform.
        public List<ExportMesh> Meshes = new List<ExportMesh>();
    }

    public class ExportBspInstancedGeometryPlacement
    {
        public int          DefinitionIndex;
        public string       InstanceName;
        public float        Scale;
        // Full 4×3 transform (3×3 rotation + translation) from ScenarioStructureBsp.InstancedGeometryInstances.
        // Already deserialized by TagTool (endian-safe for all platforms including Gen4 BE).
        public RealMatrix4x3 Transform;
    }

    public class ExportBspWeatherPolyhedron
    {
        // Raw plane set from ScenarioStructureBsp.WeatherPolyhedra planes.
        // Triple-intersection to derive convex hull vertices is deferred to Phase 3.
        // TODO Phase3: port donor math.rs RealPlane3d::triple_intersection() (Cramer's rule) to
        //              populate Vertices from every combination of 3 planes in this set.
        public List<RealPlane3d>  Planes   = new List<RealPlane3d>();
        // Populated only after Phase 3 triple-intersection port.
        public List<RealPoint3d>  Vertices = new List<RealPoint3d>();
    }

    public class ExportBspCollisionSurface
    {
        // Vertex indices into ExportScenarioBsp.CollisionVertices, in edge-ring traversal order.
        public List<int> VertexIndices = new List<int>();
        public int SourceBspIndex;
        public int SourceSurfaceIndex;
        public int FirstEdge;
        public int PlaneIndex;
        public short MaterialIndex;
        public SurfaceFlags Flags;
    }

    public class ExportBspEnvironmentObject
    {
        // Resolved tag path of the environment object (xref).
        public string        TagPath;
        public string        ObjectName;
        // Rows 1-3 = rotation matrix (built from placement quaternion), row 4 = position.
        public RealMatrix4x3 Transform;
        // Uniform scale from the environment object placement.
        public float         Scale = 1.0f;
    }

    /// <summary>
    /// Adapts a ScenarioStructureBsp tag into an ExportScenarioBsp DTO.
    ///
    /// Resource loading required:
    ///   Render:    Cache.ResourceCache.GetRenderGeometryApiResourceDefinition(bsp.Geometry.Resource)
    ///   Collision: Cache.ResourceCache.GetStructureBspTagResources(bsp.CollisionBspResource)
    ///   Gen4:      Use GetRenderGeometryApiResourceDefinitionGen4 / GetStructureBspTagResourcesGen4 variants.
    ///
    /// Returns null and logs a warning if critical resources cannot be loaded.
    /// </summary>
    public interface IExportScenarioBspAdapter
    {
        ExportScenarioBsp AdaptScenarioBsp(ScenarioStructureBsp bsp, GameCache cache, string tagPath);
    }
}
