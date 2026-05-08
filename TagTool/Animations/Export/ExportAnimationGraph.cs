using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Tags.Definitions;

namespace TagTool.Animations.Export
{
    /// <summary>
    /// Exporter-neutral animation graph. Carries skeleton and all animation metadata.
    /// Target for JMA-family export (JMM/JMA/JMT/JMZ/JMO/JMR/JMW).
    /// Mirrors donor animation/mod.rs Animation + animation/pose.rs Skeleton.
    /// </summary>
    public class ExportAnimationGraph
    {
        public string TagPath;
        public List<ExportAnimationNode> Nodes      = new List<ExportAnimationNode>();
        public List<ExportAnimation>     Animations = new List<ExportAnimation>();
    }

    /// <summary>
    /// Adapts a ModelAnimationGraph tag into an ExportAnimationGraph DTO.
    ///
    /// Skeleton: sourced from ModelAnimationGraph.SkeletonNodes[] via cache.StringTable.GetString().
    ///
    /// Animation codec data loading:
    ///   Gen3 / HaloOnline: Cache.ResourceCache.GetModelAnimationTagResource(jmad.ResourceGroups[i].Resource)
    ///   Gen4:              Cache.ResourceCache.GetModelAnimationTagResourceGen4(jmad.ResourceGroups[i].Resource)
    ///
    /// The raw codec bytes are stored in ExportAnimation.RawCodecData without interpretation.
    /// Codec decoding is deferred to Phase 3.
    ///
    /// Known limitation: Gen4 cross-jmad shared animation references (SharedAnimationIndex != -1)
    /// are not supported. Affected ExportAnimation entries will have RawCodecData = null and
    /// FrameCount = 0. A diagnostic warning must be logged for each skipped animation.
    /// </summary>
    public interface IExportAnimationGraphAdapter
    {
        ExportAnimationGraph AdaptAnimationGraph(ModelAnimationGraph jmad, GameCache cache, string tagPath);
    }
}
