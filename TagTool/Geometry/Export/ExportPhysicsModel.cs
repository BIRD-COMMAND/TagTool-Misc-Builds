using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral physics model. Target for JMS phmo export.
    /// Mirrors donor jms.rs JmsFile::from_physics_model() / from_physics_model_with_skeleton().
    /// </summary>
    public class ExportPhysicsModel
    {
        public string TagPath;
        public List<ExportNode>          Nodes      = new List<ExportNode>();
        public List<ExportMaterial>      Materials  = new List<ExportMaterial>();
        public List<ExportRigidBody>     RigidBodies = new List<ExportRigidBody>();
        public List<ExportPhysSphere>    Spheres    = new List<ExportPhysSphere>();
        public List<ExportPhysBox>       Boxes      = new List<ExportPhysBox>();
        public List<ExportPhysCapsule>   Capsules   = new List<ExportPhysCapsule>();
        public List<ExportPhysPolyhedron> Polyhedra = new List<ExportPhysPolyhedron>();
        public List<ExportPhysRagdoll>   Ragdolls   = new List<ExportPhysRagdoll>();
        public List<ExportPhysHinge>     Hinges     = new List<ExportPhysHinge>();
    }

    public class ExportRigidBody
    {
        public int        NodeIndex;
        public int        MaterialIndex;
        public float      Mass;
        public RealPoint3d CenterOfMass;
    }

    /// <summary>Base data shared by all physics shape types.</summary>
    public abstract class ExportPhysShape
    {
        public string Name;
        public int    MaterialIndex;
        public int    NodeIndex; // rigid body's node index
    }

    public class ExportPhysSphere : ExportPhysShape
    {
        public RealPoint3d Translation;
        public float       Radius;
    }

    public class ExportPhysBox : ExportPhysShape
    {
        public RealPoint3d    Translation;
        public RealVector3d   HalfExtents;

        // Rotation extracted from Havok 3×3 row-vector basis (RotationI/J/K) by the adapter.
        // Computed via Matrix4x4(rows=RotationI/J/K) + Quaternion.CreateFromRotationMatrix.
        public RealQuaternion Rotation;
    }

    public class ExportPhysCapsule : ExportPhysShape
    {
        public RealPoint3d Bottom;
        public RealPoint3d Top;
        public float       Radius;
    }

    public class ExportPhysPolyhedron : ExportPhysShape
    {
        // Vertices extracted from PhysicsModel.PolyhedronFourVectors.
        // Four-vector packing: each block holds 4 vertices; Phase 3 unpacks them.
        // TODO Phase3: unpack four-vector blocks (PhysicsModel.PolyhedronFourVectors) into vertices.
        public List<RealPoint3d>  Vertices       = new List<RealPoint3d>();
        public List<RealPlane3d>  PlaneEquations = new List<RealPlane3d>();
    }

    /// <summary>Base for Havok constraint types. Full frame data is deferred to Phase 3.</summary>
    public abstract class ExportPhysConstraint
    {
        public string Name;
        public int    NodeA;
        public int    NodeB;

        // Pivot and rotation extracted from Havok constraint frame (AForward/ALeft/AUp/APosition).
        // Adapter builds quaternion from matrix rows, negates for ragdolls (TagTool convention).
        public RealPoint3d    PivotInA;
        public RealPoint3d    PivotInB;
        public RealQuaternion RotationInA;
        public RealQuaternion RotationInB;
    }

    public class ExportPhysRagdoll : ExportPhysConstraint
    {
        public float MinTwist;
        public float MaxTwist;
        public float MinCone;
        public float MaxCone;
        public float MinPlane;
        public float MaxPlane;
        public float FrictionLimit;
    }

    public class ExportPhysHinge : ExportPhysConstraint
    {
        public int   IsLimited;    // 0 = non-limited, 1 = limited
        public float MinAngle;
        public float MaxAngle;
        public float FrictionLimit;
    }

    /// <summary>
    /// Adapts a PhysicsModel tag into an ExportPhysicsModel DTO.
    /// No pageable resources required — physics data is inline in the deserialized tag.
    /// Note: MOPP Havok data is not exported (only convex primitives and constraints).
    /// </summary>
    public interface IExportPhysicsModelAdapter
    {
        ExportPhysicsModel AdaptPhysicsModel(PhysicsModel physicsModel, GameCache cache, string tagPath);
    }
}
