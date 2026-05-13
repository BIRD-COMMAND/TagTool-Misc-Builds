using System;
using System.Collections.Generic;
using System.Linq;
using TagTool.Cache;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Jms
{
    internal static class Sep27RenderModelAnalyzer
    {
        internal const string SyntheticOrphanRegionName = "__sep27_orphaned__";

        internal static Sep27RenderModelAnalysis Analyze(
            GameCache cache,
            Model hlmt,
            RenderModel rawMode,
            RenderModel convertedMode)
        {
            _ = hlmt;

            var directMeshIndices = new HashSet<int>();
            var zeroMeshPermutations = new List<Sep27ZeroMeshPermutationRecord>();
            int meshCount = convertedMode?.Geometry?.Meshes?.Count ?? 0;

            if (rawMode?.RegionsHalo4280911 != null)
            {
                foreach (var region in rawMode.RegionsHalo4280911)
                {
                    string regionName = cache.StringTable.GetString(region.Name) ?? string.Empty;
                    if (region.Permutations == null)
                        continue;

                    foreach (var permutation in region.Permutations)
                    {
                        string permutationName = cache.StringTable.GetString(permutation.Name) ?? string.Empty;
                        string cloneName = cache.StringTable.GetString(permutation.CloneName) ?? string.Empty;

                        if (permutation.MeshIndex >= 0 && permutation.MeshCount > 0)
                        {
                            for (int meshOffset = 0; meshOffset < permutation.MeshCount; meshOffset++)
                            {
                                int meshIndex = permutation.MeshIndex + meshOffset;
                                if (meshIndex >= 0 && meshIndex < meshCount)
                                    directMeshIndices.Add(meshIndex);
                            }
                        }
                        else if (permutation.MeshIndex >= 0 && permutation.MeshCount == 0)
                        {
                            zeroMeshPermutations.Add(new Sep27ZeroMeshPermutationRecord(
                                regionName,
                                permutationName,
                                cloneName,
                                permutation.InstanceMask031.ToString(),
                                permutation.InstanceMask3263.ToString(),
                                permutation.InstanceMask6495.ToString(),
                                permutation.InstanceMask96127.ToString()));
                        }
                    }
                }
            }

            var orphanMeshes = new List<Sep27OrphanMeshRecord>();
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (directMeshIndices.Contains(meshIndex))
                    continue;
                if (meshIndex == convertedMode.InstanceStartingMeshIndex)
                    continue;

                string dominantMaterialName = ResolveDominantMaterialName(convertedMode, meshIndex);
                orphanMeshes.Add(new Sep27OrphanMeshRecord(
                    meshIndex,
                    dominantMaterialName,
                    BuildSyntheticPermutationName(meshIndex, dominantMaterialName)));
            }

            return new Sep27RenderModelAnalysis(
                directMeshIndices.OrderBy(index => index).ToList(),
                orphanMeshes,
                zeroMeshPermutations);
        }

        private static string ResolveDominantMaterialName(RenderModel mode, int meshIndex)
        {
            if (mode?.Geometry?.Meshes == null || meshIndex < 0 || meshIndex >= mode.Geometry.Meshes.Count)
                return string.Empty;

            var mesh = mode.Geometry.Meshes[meshIndex];
            if (mesh?.Parts == null || mesh.Parts.Count == 0)
                return string.Empty;

            var materialCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int partIndex = 0; partIndex < mesh.Parts.Count; partIndex++)
            {
                var part = mesh.Parts[partIndex];
                string materialName = JmsModeExporter.ResolveRenderMaterialName(mode, part.MaterialIndex);
                if (!materialCounts.ContainsKey(materialName))
                {
                    materialCounts[materialName] = 0;
                    firstSeen[materialName] = partIndex;
                }

                materialCounts[materialName]++;
            }

            return materialCounts
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => firstSeen[entry.Key])
                .Select(entry => entry.Key)
                .FirstOrDefault() ?? string.Empty;
        }

        private static string BuildSyntheticPermutationName(int meshIndex, string dominantMaterialName)
        {
            string suffix = SanitizeNameSegment(dominantMaterialName);
            return string.IsNullOrEmpty(suffix) ? $"mesh_{meshIndex}" : $"mesh_{meshIndex}_{suffix}";
        }

        private static string SanitizeNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = value
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray();

            string sanitized = new string(chars).Trim('_');
            while (sanitized.Contains("__"))
                sanitized = sanitized.Replace("__", "_");

            return sanitized;
        }
    }

    internal sealed class Sep27RenderModelAnalysis
    {
        public IReadOnlyList<int> DirectMeshIndices { get; }
        public IReadOnlyList<Sep27OrphanMeshRecord> OrphanMeshes { get; }
        public IReadOnlyList<Sep27ZeroMeshPermutationRecord> ZeroMeshPermutations { get; }

        public Sep27RenderModelAnalysis(
            IReadOnlyList<int> directMeshIndices,
            IReadOnlyList<Sep27OrphanMeshRecord> orphanMeshes,
            IReadOnlyList<Sep27ZeroMeshPermutationRecord> zeroMeshPermutations)
        {
            DirectMeshIndices = directMeshIndices ?? Array.Empty<int>();
            OrphanMeshes = orphanMeshes ?? Array.Empty<Sep27OrphanMeshRecord>();
            ZeroMeshPermutations = zeroMeshPermutations ?? Array.Empty<Sep27ZeroMeshPermutationRecord>();
        }
    }

    internal sealed class Sep27OrphanMeshRecord
    {
        public int MeshIndex { get; }
        public string DominantMaterialName { get; }
        public string SyntheticPermutationName { get; }

        public Sep27OrphanMeshRecord(int meshIndex, string dominantMaterialName, string syntheticPermutationName)
        {
            MeshIndex = meshIndex;
            DominantMaterialName = dominantMaterialName ?? string.Empty;
            SyntheticPermutationName = syntheticPermutationName ?? $"mesh_{meshIndex}";
        }
    }

    internal sealed class Sep27ZeroMeshPermutationRecord
    {
        public string RegionName { get; }
        public string PermutationName { get; }
        public string CloneName { get; }
        public string InstanceMask031 { get; }
        public string InstanceMask3263 { get; }
        public string InstanceMask6495 { get; }
        public string InstanceMask96127 { get; }

        public Sep27ZeroMeshPermutationRecord(
            string regionName,
            string permutationName,
            string cloneName,
            string instanceMask031,
            string instanceMask3263,
            string instanceMask6495,
            string instanceMask96127)
        {
            RegionName = regionName ?? string.Empty;
            PermutationName = permutationName ?? string.Empty;
            CloneName = cloneName ?? string.Empty;
            InstanceMask031 = instanceMask031 ?? "0";
            InstanceMask3263 = instanceMask3263 ?? "0";
            InstanceMask6495 = instanceMask6495 ?? "0";
            InstanceMask96127 = instanceMask96127 ?? "0";
        }
    }
}
