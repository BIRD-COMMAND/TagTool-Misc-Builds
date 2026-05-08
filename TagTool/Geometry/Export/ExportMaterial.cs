namespace TagTool.Geometry.Export
{
    /// <summary>
    /// Exporter-neutral material reference. Carries shader path and optional lightmap override.
    /// Mirrors donor jms.rs JmsMaterial / ass.rs AssMaterial.
    /// </summary>
    public class ExportMaterial
    {
        /// <summary>Path of the shader/render_method tag (e.g. "levels\shared\shaders\fog").</summary>
        public string ShaderPath;

        /// <summary>Lightmap material override; empty for non-BSP contexts.</summary>
        public string LightmapPath;
    }
}
