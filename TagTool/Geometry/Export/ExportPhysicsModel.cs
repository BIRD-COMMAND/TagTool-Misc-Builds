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

        // Rotation derived from the Havok 3×3 row-vector basis stored in PhysicsModel.Box.
        // TODO Phase3: port donor jms.rs rotation_from_basis() to extract this quaternion.
        //              Input is PhysicsModel.Box.RotationI/J/K as row vectors.
        //              Current adapter leaves this as identity until Phase 3.
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

        // Pivot and rotation extracted from Havok constraint frame data.
        // TODO Phase3: port donor jms.rs constraint_frame() + rotation_from_basis() to populate
        //              PivotInA/B and RotationInA/B from the raw Havok row-vector basis fields.
        public RealPoint3d    PivotInA;
        public RealPoint3d    PivotInB;
        public RealQuaternion RotationInA;
        public RealQuaternion RotationInB;
    }

    public class ExportPhysRagdoll : ExportPhysConstraint { }
    public class ExportPhysHinge   : ExportPhysConstraint { }

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
