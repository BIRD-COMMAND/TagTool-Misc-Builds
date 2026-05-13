using System;
using System.Diagnostics;
using System.IO;
using TagTool.Cache;
using TagTool.Geometry.Export;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Ass
{
    public sealed class ScenarioBspAssExportReport
    {
        public string TagPath { get; init; } = string.Empty;
        public string OutputPath { get; init; } = string.Empty;
        public int ClusterCount { get; init; }
        public int PortalCount { get; init; }
        public int CollisionSurfaceCount { get; init; }
        public int InstanceCount { get; init; }
        public int MaterialCount { get; init; }
        public int ObjectCount { get; init; }
        public int SourceWeatherPolyhedraCount { get; init; }
        public int ExportedWeatherPolyhedraCount { get; init; }
        public TimeSpan Duration { get; init; }
        public bool WeatherPolyhedraSkipped => SourceWeatherPolyhedraCount > 0 && ExportedWeatherPolyhedraCount == 0;
    }

    public static class ScenarioBspAssExportService
    {
        public static ScenarioBspAssExportReport ExportScenarioBspToAss(
            GameCache cache,
            CachedTag tag,
            ScenarioStructureBsp bsp,
            string outputPath)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));
            if (bsp == null)
                throw new ArgumentNullException(nameof(bsp));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            string fullPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string tagPath = tag.Name ?? tag.Index.ToString("X8");
            var sw = Stopwatch.StartNew();

            ExportScenarioBsp dto = new ScenarioBspExportAdapter().AdaptScenarioBsp(bsp, cache, tagPath);

            var ass = new AssFormat();
            var exporter = new AssExporter(cache, ass);
            exporter.Export(dto);

            ass.Write(new FileInfo(fullPath));

            sw.Stop();

            return new ScenarioBspAssExportReport
            {
                TagPath = tagPath,
                OutputPath = fullPath,
                ClusterCount = dto.Clusters.Count,
                PortalCount = dto.ClusterPortals.Count,
                CollisionSurfaceCount = dto.CollisionSurfaces.Count,
                InstanceCount = dto.InstancedGeometryPlacements.Count,
                MaterialCount = ass.Materials.Count,
                ObjectCount = ass.Objects.Count,
                SourceWeatherPolyhedraCount = bsp.WeatherPolyhedra?.Count ?? 0,
                ExportedWeatherPolyhedraCount = dto.WeatherPolyhedra.Count,
                Duration = sw.Elapsed,
            };
        }
    }
}
