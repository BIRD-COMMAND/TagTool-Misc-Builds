using System;
using System.Collections.Generic;
using System.Numerics;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Havok;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Export
{
    public class PhysicsModelExportAdapter : IExportPhysicsModelAdapter
    {
        public ExportPhysicsModel AdaptPhysicsModel(PhysicsModel phmo, GameCache cache, string tagPath)
        {
            if (phmo  == null) throw new ArgumentNullException(nameof(phmo));
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            var em      = new ExportPhysicsModel { TagPath = tagPath };
            bool reach  = cache.Version >= CacheVersion.HaloReach;

            // Nodes: name + tree links only; no transforms (phmo node block carries none).
            // Matches donor read_phmo_nodes() identity-rotation behaviour.
            if (phmo.Nodes != null)
            {
                foreach (var node in phmo.Nodes)
                {
                    em.Nodes.Add(new ExportNode
                    {
                        Name             = cache.StringTable.GetString(node.Name) ?? string.Empty,
                        ParentIndex      = node.Parent,
                        FirstChildIndex  = node.Child,
                        NextSiblingIndex = node.Sibling,
                    });
                }
            }

            // Materials: Name for ShaderPath, MaterialName for LightmapPath.
            // Donor uses the same string for both; existing C# uses separate fields.
            // We match existing C# (Name + MaterialName) so output is unchanged.
            if (phmo.Materials != null)
            {
                foreach (var mat in phmo.Materials)
                {
                    em.Materials.Add(new ExportMaterial
                    {
                        ShaderPath   = cache.StringTable.GetString(mat.Name)         ?? string.Empty,
                        LightmapPath = cache.StringTable.GetString(mat.MaterialName) ?? string.Empty,
                    });
                }
            }

            // Rigid body → node lookup keyed by (shapeType, shapeIndex).
            // Handles H3/HO (ShapeType/ShapeIndex) and Reach (ShapeType_Reach/ShapeIndex_Reach).
            // Mirrors donor build_phmo_parent_lookup().
            var parentLookup = BuildParentLookup(phmo, reach);

            if (phmo.RigidBodies != null)
            {
                foreach (var rb in phmo.RigidBodies)
                    em.RigidBodies.Add(new ExportRigidBody { NodeIndex = rb.Node });
            }

            // Spheres — unscaled (scale ×100 applied by JMS writer at emit time).
            // Sphere has no per-shape orientation (donor comment confirmed).
            if (phmo.Spheres != null)
            {
                for (int i = 0; i < phmo.Spheres.Count; i++)
                {
                    var s = phmo.Spheres[i];
                    em.Spheres.Add(new ExportPhysSphere
                    {
                        Name          = cache.StringTable.GetString(s.Name) ?? string.Empty,
                        MaterialIndex = reach ? s.MaterialIndexReach : s.MaterialIndex,
                        NodeIndex     = LookupNode(parentLookup, BlamShapeType.Sphere, i),
                        Translation   = new RealPoint3d(s.Translation.I, s.Translation.J, s.Translation.K),
                        Radius        = s.ShapeBase.Radius,
                    });
                }
            }

            // Boxes — rotation extracted from Havok row-vector basis.
            // Matches existing JmsPhmoExporter: Matrix4x4(rows=RotationI/J/K) + CreateFromRotationMatrix.
            // Donor equivalent: rotation_from_basis() via from_basis_columns(transpose).
            // HalfExtents stored without convex radius to match existing C# output dimensions.
            if (phmo.Boxes != null)
            {
                for (int i = 0; i < phmo.Boxes.Count; i++)
                {
                    var b = phmo.Boxes[i];

                    // Row vectors of the Havok 3×3 rotation placed as Matrix rows.
                    // CreateFromRotationMatrix only reads the upper-left 3×3, so M14/M24/M34 are 0.
                    var boxMatrix = new Matrix4x4(
                        b.RotationI.I, b.RotationI.J, b.RotationI.K, 0f,
                        b.RotationJ.I, b.RotationJ.J, b.RotationJ.K, 0f,
                        b.RotationK.I, b.RotationK.J, b.RotationK.K, 0f,
                        0f, 0f, 0f, 1f);
                    Quaternion q = Quaternion.CreateFromRotationMatrix(boxMatrix);

                    em.Boxes.Add(new ExportPhysBox
                    {
                        Name          = cache.StringTable.GetString(b.Name) ?? string.Empty,
                        MaterialIndex = reach ? b.MaterialIndexReach : b.MaterialIndex,
                        NodeIndex     = LookupNode(parentLookup, BlamShapeType.Box, i),
                        Translation   = new RealPoint3d(b.Translation.I, b.Translation.J, b.Translation.K),
                        HalfExtents   = new RealVector3d(b.HalfExtents.I, b.HalfExtents.J, b.HalfExtents.K),
                        Rotation      = new RealQuaternion(q.X, q.Y, q.Z, q.W),
                    });
                }
            }

            // Pills / capsules — Bottom/Top/Radius stored unscaled.
            // JMS writer computes anchor position, height, and orientation quaternion.
            if (phmo.Pills != null)
            {
                for (int i = 0; i < phmo.Pills.Count; i++)
                {
                    var p = phmo.Pills[i];
                    em.Capsules.Add(new ExportPhysCapsule
                    {
                        Name          = cache.StringTable.GetString(p.Name) ?? string.Empty,
                        MaterialIndex = reach ? p.MaterialIndexReach : p.MaterialIndex,
                        NodeIndex     = LookupNode(parentLookup, BlamShapeType.Pill, i),
                        Bottom        = new RealPoint3d(p.Bottom.I, p.Bottom.J, p.Bottom.K),
                        Top           = new RealPoint3d(p.Top.I,    p.Top.J,    p.Top.K),
                        Radius        = p.ShapeBase.Radius,
                    });
                }
            }

            // Polyhedra — unpack four-vector blocks, deduplicate by bit-identical position.
            // Matches donor read_phmo_polyhedra() including the padding-vertex dedup step.
            // Vertices stored unscaled; writer applies ×100.
            if (phmo.Polyhedra != null && phmo.PolyhedronFourVectors != null)
            {
                int fvOffset = 0;
                for (int i = 0; i < phmo.Polyhedra.Count; i++)
                {
                    var poly = phmo.Polyhedra[i];
                    var ep   = new ExportPhysPolyhedron
                    {
                        Name          = cache.StringTable.GetString(poly.Name) ?? string.Empty,
                        MaterialIndex = reach ? poly.MaterialIndexReach : poly.MaterialIndex,
                        NodeIndex     = LookupNode(parentLookup, BlamShapeType.Polyhedron, i),
                    };

                    // Each four-vector block packs 4 vertices: (x.i,y.i,z.i), (x.j,y.j,z.j),
                    // (x.k,y.k,z.k), (x_w,y_w,z_w). Deduplicate using bit-pattern keys.
                    var seen = new HashSet<(int, int, int)>();
                    for (int k = fvOffset; k < fvOffset + poly.FourVectorsSize && k < phmo.PolyhedronFourVectors.Count; k++)
                    {
                        var fv = phmo.PolyhedronFourVectors[k];
                        TryAddVertex(ep.Vertices, seen, fv.FourVectorsX.I, fv.FourVectorsY.I, fv.FourVectorsZ.I);
                        TryAddVertex(ep.Vertices, seen, fv.FourVectorsX.J, fv.FourVectorsY.J, fv.FourVectorsZ.J);
                        TryAddVertex(ep.Vertices, seen, fv.FourVectorsX.K, fv.FourVectorsY.K, fv.FourVectorsZ.K);
                        TryAddVertex(ep.Vertices, seen, fv.FourVectorsXW,  fv.FourVectorsYW,  fv.FourVectorsZW);
                    }
                    fvOffset += poly.FourVectorsSize;

                    em.Polyhedra.Add(ep);
                }
            }

            // Ragdoll constraints.
            // Orientation built from (AForward, ALeft, AUp) as matrix rows + CreateFromRotationMatrix,
            // then negated — matching existing JmsPhmoExporter convention and donor -a_rot.
            // Pivot stored unscaled; writer applies ×100.
            if (phmo.RagdollConstraints != null)
            {
                foreach (var r in phmo.RagdollConstraints)
                {
                    Quaternion qA = Quaternion.Negate(Quaternion.CreateFromRotationMatrix(
                        ConstraintMatrix(r.AForward, r.ALeft, r.AUp)));
                    Quaternion qB = Quaternion.Negate(Quaternion.CreateFromRotationMatrix(
                        ConstraintMatrix(r.BForward, r.BLeft, r.BUp)));

                    em.Ragdolls.Add(new ExportPhysRagdoll
                    {
                        Name          = cache.StringTable.GetString(r.Name) ?? string.Empty,
                        NodeA         = r.NodeA,
                        NodeB         = r.NodeB,
                        PivotInA      = new RealPoint3d(r.APosition.X, r.APosition.Y, r.APosition.Z),
                        PivotInB      = new RealPoint3d(r.BPosition.X, r.BPosition.Y, r.BPosition.Z),
                        RotationInA   = new RealQuaternion(qA.X, qA.Y, qA.Z, qA.W),
                        RotationInB   = new RealQuaternion(qB.X, qB.Y, qB.Z, qB.W),
                        MinTwist      = r.TwistRange.Lower,
                        MaxTwist      = r.TwistRange.Upper,
                        MinCone       = r.ConeRange.Lower,
                        MaxCone       = r.ConeRange.Upper,
                        MinPlane      = r.PlaneRange.Lower,
                        MaxPlane      = r.PlaneRange.Upper,
                        FrictionLimit = r.MaxFrictionTorque,
                    });
                }
            }

            // Non-limited hinge constraints (NOT negated — only ragdolls are negated).
            // Mirrors donor: hinges use rotation as-is, ragdolls negate.
            if (phmo.HingeConstraints != null)
            {
                foreach (var h in phmo.HingeConstraints)
                {
                    Quaternion qA = Quaternion.CreateFromRotationMatrix(ConstraintMatrix(h.AForward, h.ALeft, h.AUp));
                    Quaternion qB = Quaternion.CreateFromRotationMatrix(ConstraintMatrix(h.BForward, h.BLeft, h.BUp));

                    em.Hinges.Add(new ExportPhysHinge
                    {
                        Name        = cache.StringTable.GetString(h.Name) ?? string.Empty,
                        NodeA       = h.NodeA,
                        NodeB       = h.NodeB,
                        PivotInA    = new RealPoint3d(h.APosition.X, h.APosition.Y, h.APosition.Z),
                        PivotInB    = new RealPoint3d(h.BPosition.X, h.BPosition.Y, h.BPosition.Z),
                        RotationInA = new RealQuaternion(qA.X, qA.Y, qA.Z, qA.W),
                        RotationInB = new RealQuaternion(qB.X, qB.Y, qB.Z, qB.W),
                        IsLimited   = 0,
                    });
                }
            }

            // Limited hinge constraints.
            if (phmo.LimitedHingeConstraints != null)
            {
                foreach (var h in phmo.LimitedHingeConstraints)
                {
                    Quaternion qA = Quaternion.CreateFromRotationMatrix(ConstraintMatrix(h.AForward, h.ALeft, h.AUp));
                    Quaternion qB = Quaternion.CreateFromRotationMatrix(ConstraintMatrix(h.BForward, h.BLeft, h.BUp));

                    em.Hinges.Add(new ExportPhysHinge
                    {
                        Name          = cache.StringTable.GetString(h.Name) ?? string.Empty,
                        NodeA         = h.NodeA,
                        NodeB         = h.NodeB,
                        PivotInA      = new RealPoint3d(h.APosition.X, h.APosition.Y, h.APosition.Z),
                        PivotInB      = new RealPoint3d(h.BPosition.X, h.BPosition.Y, h.BPosition.Z),
                        RotationInA   = new RealQuaternion(qA.X, qA.Y, qA.Z, qA.W),
                        RotationInB   = new RealQuaternion(qB.X, qB.Y, qB.Z, qB.W),
                        IsLimited     = 1,
                        FrictionLimit = h.LimitFriction,
                        MinAngle      = h.LimitAngleBounds.Lower,
                        MaxAngle      = h.LimitAngleBounds.Upper,
                    });
                }
            }

            return em;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// Build (shapeType, shapeIndex) → nodeIndex from rigid bodies.
        /// Handles H3/HO (ShapeType/ShapeIndex) and Reach (ShapeType_Reach/ShapeIndex_Reach).
        private static Dictionary<(BlamShapeType, int), int> BuildParentLookup(PhysicsModel phmo, bool reach)
        {
            var lookup = new Dictionary<(BlamShapeType, int), int>();
            if (phmo.RigidBodies == null) return lookup;

            for (int i = 0; i < phmo.RigidBodies.Count; i++)
            {
                var rb         = phmo.RigidBodies[i];
                var shapeType  = reach ? rb.ShapeType_Reach  : rb.ShapeType;
                var shapeIndex = reach ? rb.ShapeIndex_Reach : rb.ShapeIndex;
                var key        = (shapeType, (int)shapeIndex);
                if (!lookup.ContainsKey(key))
                    lookup[key] = rb.Node;
            }
            return lookup;
        }

        private static int LookupNode(Dictionary<(BlamShapeType, int), int> lookup, BlamShapeType shapeType, int index)
            => lookup.TryGetValue((shapeType, index), out int node) ? node : -1;

        /// Build a 4×4 matrix with (forward, left, up) as row vectors, matching the convention
        /// used by the existing JmsPhmoExporter constraint matrix construction.
        private static Matrix4x4 ConstraintMatrix(RealVector3d forward, RealVector3d left, RealVector3d up)
        {
            return new Matrix4x4(
                forward.I, forward.J, forward.K, 0f,
                left.I,    left.J,    left.K,    0f,
                up.I,      up.J,      up.K,      0f,
                0f,        0f,        0f,        1f);
        }

        /// Add vertex to list if not already present (bit-equality dedup, matching donor).
        private static void TryAddVertex(List<RealPoint3d> list, HashSet<(int, int, int)> seen, float x, float y, float z)
        {
            var key = (BitConverter.SingleToInt32Bits(x), BitConverter.SingleToInt32Bits(y), BitConverter.SingleToInt32Bits(z));
            if (seen.Add(key))
                list.Add(new RealPoint3d(x, y, z));
        }
    }
}
