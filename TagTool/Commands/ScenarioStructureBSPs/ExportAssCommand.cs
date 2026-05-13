using System;
using System.Collections.Generic;
using System.IO;
using TagTool.Cache;
using TagTool.Commands.Common;
using TagTool.Geometry.Ass;
using TagTool.Tags.Definitions;

namespace TagTool.Commands.ScenarioStructureBSPs
{
    public class ExportAssCommand : Command
    {
        private GameCache          Cache  { get; }
        private ScenarioStructureBsp Bsp  { get; }
        private CachedTag          Tag    { get; }

        public ExportAssCommand(GameCache cache, CachedTag tag, ScenarioStructureBsp bsp)
            : base(true,

                "ExportASS",
                "Exports the scenario_structure_bsp to an ASS (Amalgam) file.",

                "ExportASS <filename>",

                "Exports cluster meshes, cluster portals, instanced geometry defs/placements,\n" +
                "markers, environment object references, and the structure collision BSP\n" +
                "into an ASS v7 file that can be re-imported by Tool.exe.\n\n" +
                "Weather polyhedra are skipped (TagTool reads them as NullBlock).\n" +
                "Output file extension .ass is appended if not already present.")
        {
            Cache = cache;
            Tag   = tag;
            Bsp   = bsp;
        }

        public override object Execute(List<string> args)
        {
            if (args.Count != 1)
                return new TagToolError(CommandError.ArgCount);

            string filePath = args[0];
            if (!filePath.EndsWith(".ass", StringComparison.OrdinalIgnoreCase))
                filePath += ".ass";

            var file = new FileInfo(filePath);
            if (!file.Directory.Exists)
                file.Directory.Create();

            string tagPath = Tag.Name ?? Tag.Index.ToString("X8");

            Console.WriteLine($"Exporting ASS for {tagPath} ...");

            ScenarioBspAssExportReport report;
            try
            {
                report = ScenarioBspAssExportService.ExportScenarioBspToAss(Cache, Tag, Bsp, file.FullName);
            }
            catch (Exception ex)
            {
                return new TagToolError(CommandError.OperationFailed,
                    $"ASS export failed: {ex.Message}");
            }

            Console.WriteLine($"  Output:               \"{report.OutputPath}\"");

            return true;
        }
    }
}
