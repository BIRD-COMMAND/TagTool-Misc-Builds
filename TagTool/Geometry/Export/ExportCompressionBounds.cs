using TagTool.Common;

namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Axis-aligned compression bounds for dequantizing normalized vertex positions and UVs.
    /// Mirrors donor geometry.rs CompressionBounds; populated from RenderGeometryCompression.
    /// </summary>
    public class ExportCompressionBounds
    {
        public float XMin, XMax;
        public float YMin, YMax;
        public float ZMin, ZMax;
        public float UMin, UMax;
        public float VMin, VMax;

        /// <summary>
        /// Bounds that map [-1, 1] position space — used when geometry is not compressed.
        /// </summary>
        public static ExportCompressionBounds Identity => new ExportCompressionBounds
        {
            XMin = -1f, XMax = 1f,
            YMin = -1f, YMax = 1f,
            ZMin = -1f, ZMax = 1f,
            UMin = 0f,  UMax = 1f,
            VMin = 0f,  VMax = 1f,
        };

        /// <summary>
        /// Passthrough bounds that map [0, 1] to [0, 1] — for world-space cluster geometry.
        /// </summary>
        public static ExportCompressionBounds Passthrough => new ExportCompressionBounds
        {
            XMin = 0f, XMax = 1f,
            YMin = 0f, YMax = 1f,
            ZMin = 0f, ZMax = 1f,
            UMin = 0f, UMax = 1f,
            VMin = 0f, VMax = 1f,
        };

        // All field reads on RenderGeometryCompression come from TagTool deserialization
        // and are already in the host's native byte order — no manual endian handling needed here.
        public static ExportCompressionBounds FromRenderGeometryCompression(
            Bounds<float> x, Bounds<float> y, Bounds<float> z,
            Bounds<float> u, Bounds<float> v)
        {
            return new ExportCompressionBounds
            {
                XMin = x.Lower, XMax = x.Upper,
                YMin = y.Lower, YMax = y.Upper,
                ZMin = z.Lower, ZMax = z.Upper,
                UMin = u.Lower, UMax = u.Upper,
                VMin = v.Lower, VMax = v.Upper,
            };
        }

        public RealPoint3d DecompressPosition(float nx, float ny, float nz)
            => new RealPoint3d(
                nx * (XMax - XMin) + XMin,
                ny * (YMax - YMin) + YMin,
                nz * (ZMax - ZMin) + ZMin);

        public RealVector2d DecompressUv(float nu, float nv)
            => new RealVector2d(
                nu * (UMax - UMin) + UMin,
                nv * (VMax - VMin) + VMin);
    }
}
