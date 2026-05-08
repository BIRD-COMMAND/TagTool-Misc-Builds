using TagTool.Common;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral vertex. All values are in model/world-space units (NOT pre-scaled by 100).
    /// The final × 100 scale to centimeters is applied by the individual exporter (JMS, ASS, etc.).
    /// Mirrors donor jms.rs JmsVertex.
    /// </summary>
    public class ExportVertex
    {
        public RealPoint3d  Position;
        public RealVector3d Normal;
        public RealVector3d Tangent;
        public RealVector3d Binormal;

        // UV channels: index 0 = primary (diffuse), index 1 = secondary (lightmap or extra).
        // May be zero-initialised if the source mesh has fewer channels.
        public RealVector2d[] TexCoords = new RealVector2d[2];

        // Bone skinning: up to 4 influences. BoneIndices[i] indexes into ExportRenderModel.Nodes.
        // Unused entries have BoneWeights[i] == 0.
        public int[]   BoneIndices = new int[4];
        public float[] BoneWeights = new float[4];

        // Packed ARGB vertex colour; null when the source mesh has no colour stream.
        public uint? Color;
    }
}
