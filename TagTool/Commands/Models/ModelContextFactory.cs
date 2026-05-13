using TagTool.Cache;
using TagTool.Commands.Gen4.Models;
using TagTool.Tags.Definitions;

namespace TagTool.Commands.Models
{
    static class ModelContextFactory
    {
        public static CommandContext Create(CommandContext parent, GameCache cache, CachedTag tag, Model model)
        {
            var groupName = tag.Group.ToString();

            var context = new CommandContext(parent,
                string.Format("{0:X8}.{1}", tag.Index, groupName));

            Populate(context, cache, tag, model);

            return context;
        }

        public static void Populate(CommandContext context, GameCache cache, CachedTag tag, Model model)
        {
            context.AddCommand(new ListVariantsCommand(cache, model));
            context.AddCommand(new ExtractModelCommand(cache, tag, model));
            context.AddCommand(new ExtractBitmapsCommand(cache, tag, model));
            context.AddCommand(new ExportJMSCommand(cache, model));
            if (cache.Version == CacheVersion.Halo4280911)
                context.AddCommand(new DiagnoseSep27RenderModelCommand(cache, model));
            context.AddCommand(new UpdateModelRegionsCommand(cache, model, tag));
            context.AddCommand(new UpdateModelNodesCommand(cache, model, tag));
        }
    }
}
