using System;
using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Commands.Common;
using TagTool.Geometry.Jms;
using SharedDefs = TagTool.Tags.Definitions;

namespace TagTool.Commands.Gen4.Models
{
    public class ExportJMSCommand : Command
    {
        private GameCache Cache { get; }
        private SharedDefs.Model Definition { get; }

        public ExportJMSCommand(GameCache cache, SharedDefs.Model definition) :
            base(true,
                "ExportJMS",
                "",
                "ExportJMS <coll/mode/phmo> <File>",
                "")
        {
            Cache = cache;
            Definition = definition;
        }

        public override object Execute(List<string> args)
        {
            if (args.Count != 2)
                return new TagToolError(CommandError.ArgCount);

            ModelJmsExportKind kind;
            switch (args[0])
            {
                case "coll":
                    kind = ModelJmsExportKind.Collision;
                    break;
                case "mode":
                    kind = ModelJmsExportKind.Render;
                    break;
                case "phmo":
                    kind = ModelJmsExportKind.Physics;
                    break;
                default:
                    return new TagToolError(CommandError.ArgInvalid);
            }

            var report = ModelJmsExportService.ExportModelJms(
                Cache,
                modelTag: null,
                model: Definition,
                kind: kind,
                outputPath: args[1],
                skipMissingReference: false);

            if (!report.Exported)
            {
                return new TagToolError(
                    CommandError.OperationFailed,
                    report.Exception?.Message ?? report.Message ?? "Failed to export JMS.");
            }

            if (!string.IsNullOrWhiteSpace(report.Message))
                Console.WriteLine(report.Message);

            Console.WriteLine($"Exported to \"{report.OutputPath}\".");
            return true;
        }
    }
}
