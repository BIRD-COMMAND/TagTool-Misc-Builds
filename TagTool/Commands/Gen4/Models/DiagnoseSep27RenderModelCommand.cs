using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TagTool.Cache;
using TagTool.Commands.Common;
using TagTool.Commands.Porting.Gen4;
using TagTool.Geometry.Jms;
using SharedDefs = TagTool.Tags.Definitions;

namespace TagTool.Commands.Gen4.Models
{
    /// <summary>
    /// Diagnostic command that dumps the full region → permutation → mesh hierarchy
    /// for a Sep27 render_model tag. Used to investigate mesh duplication / missing
    /// mesh issues in the JMS export pipeline.
    /// </summary>
    public class DiagnoseSep27RenderModelCommand : Command
    {
        private GameCache Cache { get; }
        private SharedDefs.Model Definition { get; }

        public DiagnoseSep27RenderModelCommand(GameCache cache, SharedDefs.Model definition) :
            base(true,
                "DiagnoseRenderModel",
                "Dumps render_model region/permutation/mesh hierarchy for debugging",
                "DiagnoseRenderModel",
                "Dumps the full region → permutation → mesh structure of the render_model\n" +
                "referenced by this hlmt, including Sep27 variant selectors and orphan mesh analysis.")
        {
            Cache = cache;
            Definition = definition;
        }

        public override object Execute(List<string> args)
        {
            if (Definition.RenderModel == null)
                return new TagToolError(CommandError.OperationFailed, "Model has no render_model reference.");

            bool isSep27 = Cache.Version == CacheVersion.Halo4280911;
            Console.WriteLine($"Cache version: {Cache.Version} (isSep27: {isSep27})");
            Console.WriteLine($"Render model tag: {Definition.RenderModel}");
            Console.WriteLine();

            using (var cacheStream = Cache.OpenCacheRead())
            {
                if (isSep27)
                    DiagnoseSep27(cacheStream);
                else
                    DiagnoseGeneric(cacheStream);
            }

            return true;
        }

        private void DiagnoseSep27(Stream cacheStream)
        {
            var sharedMode = Cache.Deserialize<SharedDefs.RenderModel>(cacheStream, Definition.RenderModel);
            var convertedMode = RenderModelConverter.ConvertSep27(Cache, sharedMode);
            var analysis = Sep27RenderModelAnalyzer.Analyze(Cache, Definition, sharedMode, convertedMode);

            Console.WriteLine("=== RAW Sep27 DESERIALIZATION ===");
            Console.WriteLine();

            var regions = sharedMode.RegionsHalo4280911;
            var geometry = sharedMode.GeometryHalo4280911;

            Console.WriteLine($"RegionsHalo4280911 count: {regions?.Count ?? 0}");
            Console.WriteLine($"Regions (gen3 field) count: {sharedMode.Regions?.Count ?? 0}");
            Console.WriteLine($"NodesHalo4280911 count: {sharedMode.NodesHalo4280911?.Count ?? 0}");
            Console.WriteLine($"Nodes (gen3 field) count: {sharedMode.Nodes?.Count ?? 0}");
            Console.WriteLine($"Materials count: {sharedMode.Materials?.Count ?? 0}");
            Console.WriteLine($"MarkerGroupsHalo4280911 count: {sharedMode.MarkerGroupsHalo4280911?.Count ?? 0}");
            Console.WriteLine($"InstanceStartingMeshIndex (raw): {sharedMode.InstanceStartingMeshIndex}");
            Console.WriteLine($"InstancePlacements count (raw): {sharedMode.InstancePlacements?.Count ?? 0}");
            Console.WriteLine();

            if (geometry == null)
            {
                Console.WriteLine("ERROR: GeometryHalo4280911 is NULL");
                return;
            }

            Console.WriteLine($"GeometryHalo4280911.Meshes count: {geometry.Meshes?.Count ?? 0}");
            Console.WriteLine($"GeometryHalo4280911.CompressionInfo count: {geometry.CompressionInfo?.Count ?? 0}");
            Console.WriteLine($"GeometryHalo4280911.PerMeshNodeMap count: {geometry.PerMeshNodeMap?.Count ?? 0}");
            Console.WriteLine($"GeometryHalo4280911.ApiResource: {(geometry.ApiResource != null ? "present" : "NULL")}");
            Console.WriteLine();

            PrintRawMeshDetails(sharedMode, geometry, analysis);
            PrintRawRegionPermutationHierarchy(regions, geometry);

            Console.WriteLine("--- Direct Mesh References ---");
            Console.WriteLine($"Direct mesh indices: {FormatMeshIndices(analysis.DirectMeshIndices)}");
            Console.WriteLine($"Unreferenced mesh indices: {FormatMeshIndices(analysis.OrphanMeshes.Select(mesh => mesh.MeshIndex))}");
            foreach (var orphanMesh in analysis.OrphanMeshes)
            {
                Console.WriteLine(
                    $"  Orphan Mesh[{orphanMesh.MeshIndex}] => dominantMaterial=\"{orphanMesh.DominantMaterialName}\", " +
                    $"syntheticPermutation=\"{orphanMesh.SyntheticPermutationName}\"");
            }
            Console.WriteLine();

            Console.WriteLine("--- Zero-Mesh Permutations ---");
            if (analysis.ZeroMeshPermutations.Count == 0)
            {
                Console.WriteLine("  (none)");
            }
            else
            {
                foreach (var permutation in analysis.ZeroMeshPermutations)
                {
                    Console.WriteLine(
                        $"  {permutation.RegionName}\\{permutation.PermutationName} " +
                        $"CloneName=\"{permutation.CloneName}\" " +
                        $"Masks=[{permutation.InstanceMask031} | {permutation.InstanceMask3263} | " +
                        $"{permutation.InstanceMask6495} | {permutation.InstanceMask96127}]");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== POST-CONVERSION (ConvertSep27 -> gen3 RenderModel) ===");
            Console.WriteLine($"Converted Regions: {convertedMode.Regions?.Count ?? 0}");
            Console.WriteLine($"Converted Meshes: {convertedMode.Geometry?.Meshes?.Count ?? 0}");
            Console.WriteLine($"Converted Materials: {convertedMode.Materials?.Count ?? 0}");
            Console.WriteLine($"Converted PerMeshNodeMaps: {convertedMode.Geometry?.PerMeshNodeMaps?.Count ?? 0}");
            Console.WriteLine($"Converted InstanceStartingMeshIndex: {convertedMode.InstanceStartingMeshIndex}");
            Console.WriteLine($"Converted InstancePlacements: {convertedMode.InstancePlacements?.Count ?? 0}");
            Console.WriteLine();

            if (convertedMode.Regions != null)
            {
                for (int regionIndex = 0; regionIndex < convertedMode.Regions.Count; regionIndex++)
                {
                    var region = convertedMode.Regions[regionIndex];
                    string regionName = Cache.StringTable.GetString(region.Name) ?? $"<{region.Name}>";
                    Console.WriteLine($"  Region[{regionIndex}] \"{regionName}\":");
                    if (region.Permutations == null)
                        continue;

                    for (int permutationIndex = 0; permutationIndex < region.Permutations.Count; permutationIndex++)
                    {
                        var permutation = region.Permutations[permutationIndex];
                        string permutationName = Cache.StringTable.GetString(permutation.Name) ?? $"<{permutation.Name}>";
                        Console.WriteLine(
                            $"    Perm[{permutationIndex}] \"{permutationName}\": " +
                            $"MeshIndex={permutation.MeshIndex}, MeshCount={permutation.MeshCount}");
                    }
                }
            }

            Console.WriteLine();
            PrintSep27VariantRuntimeInfo();
            Console.WriteLine();
            Console.WriteLine("=== DONE ===");
        }

        private void PrintRawMeshDetails(
            SharedDefs.RenderModel sharedMode,
            SharedDefs.RenderModel.RenderGeometryHalo4280911 geometry,
            Sep27RenderModelAnalysis analysis)
        {
            if (geometry.Meshes == null)
                return;

            var orphanMaterials = analysis.OrphanMeshes.ToDictionary(mesh => mesh.MeshIndex, mesh => mesh.DominantMaterialName);

            Console.WriteLine("--- Per-Mesh Detail (raw Sep27 GlobalMeshBlockSep27) ---");
            for (int meshIndex = 0; meshIndex < geometry.Meshes.Count; meshIndex++)
            {
                var mesh = geometry.Meshes[meshIndex];
                int partCount = mesh.Parts?.Count ?? 0;
                int subpartCount = mesh.Subparts?.Count ?? 0;
                int totalPartVertices = 0;
                int totalPartIndices = 0;
                if (mesh.Parts != null)
                {
                    foreach (var part in mesh.Parts)
                    {
                        totalPartVertices += part.BudgetVertexCount;
                        totalPartIndices += part.IndexCount;
                    }
                }

                string orphanMaterialSuffix = orphanMaterials.TryGetValue(meshIndex, out string dominantMaterialName)
                    ? $", OrphanDominantMaterial=\"{dominantMaterialName}\""
                    : string.Empty;

                Console.WriteLine(
                    $"  Mesh[{meshIndex}]: Parts={partCount}, Subparts={subpartCount}, VertexType={mesh.VertexType}, " +
                    $"RigidNode={mesh.RigidNodeIndex}, IndexBuffer={mesh.IndexBufferIndex}, IndexType={mesh.IndexBufferType}, " +
                    $"TotalPartVerts={totalPartVertices}, TotalPartIndices={totalPartIndices}{orphanMaterialSuffix}");
            }
            Console.WriteLine();
        }

        private void PrintRawRegionPermutationHierarchy(
            List<SharedDefs.Gen4.RenderModel.RenderModelRegionBlock> regions,
            SharedDefs.RenderModel.RenderGeometryHalo4280911 geometry)
        {
            if (regions == null)
                return;

            Console.WriteLine("--- Region -> Permutation Hierarchy ---");
            int totalPermutations = 0;
            var meshUsage = new Dictionary<int, List<string>>();

            for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
            {
                var region = regions[regionIndex];
                string regionName = Cache.StringTable.GetString(region.Name) ?? $"<{region.Name}>";
                Console.WriteLine($"  Region[{regionIndex}] \"{regionName}\":");

                if (region.Permutations == null || region.Permutations.Count == 0)
                {
                    Console.WriteLine("    (no permutations)");
                    continue;
                }

                for (int permutationIndex = 0; permutationIndex < region.Permutations.Count; permutationIndex++)
                {
                    var permutation = region.Permutations[permutationIndex];
                    totalPermutations++;

                    string permutationName = Cache.StringTable.GetString(permutation.Name) ?? $"<{permutation.Name}>";
                    string cloneName = Cache.StringTable.GetString(permutation.CloneName) ?? string.Empty;

                    Console.WriteLine(
                        $"    Perm[{permutationIndex}] \"{permutationName}\": " +
                        $"MeshIndex={permutation.MeshIndex}, MeshCount={permutation.MeshCount}");

                    if (permutation.MeshCount == 0)
                    {
                        Console.WriteLine(
                            $"      CloneName=\"{cloneName}\", Masks=[{permutation.InstanceMask031} | " +
                            $"{permutation.InstanceMask3263} | {permutation.InstanceMask6495} | {permutation.InstanceMask96127}]");
                    }

                    for (int meshOffset = 0; meshOffset < permutation.MeshCount; meshOffset++)
                    {
                        int meshIndex = permutation.MeshIndex + meshOffset;
                        if (!meshUsage.TryGetValue(meshIndex, out var users))
                        {
                            users = new List<string>();
                            meshUsage[meshIndex] = users;
                        }

                        users.Add($"{regionName}:{permutationName}");
                    }

                    if (permutation.MeshIndex >= 0 && permutation.MeshCount > 0)
                    {
                        int lastMesh = permutation.MeshIndex + permutation.MeshCount - 1;
                        if (lastMesh >= (geometry.Meshes?.Count ?? 0))
                        {
                            Console.WriteLine(
                                $"      *** ERROR: MeshIndex({permutation.MeshIndex}) + MeshCount({permutation.MeshCount}) - 1 = {lastMesh} " +
                                $">= Geometry.Meshes.Count({geometry.Meshes?.Count ?? 0})");
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Total regions: {regions.Count}");
            Console.WriteLine($"Total permutations: {totalPermutations}");
            Console.WriteLine();
            Console.WriteLine("--- Mesh Usage Summary ---");

            int meshCount = geometry.Meshes?.Count ?? 0;
            int referencedMeshCount = 0;
            int sharedMeshCount = 0;
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (meshUsage.TryGetValue(meshIndex, out var users))
                {
                    referencedMeshCount++;
                    string prefix = users.Count > 1 ? "SHARED" : "      ";
                    if (users.Count > 1)
                        sharedMeshCount++;
                    Console.WriteLine($"  {prefix} Mesh[{meshIndex}] used by: {string.Join(", ", users)}");
                }
                else
                {
                    Console.WriteLine($"  UNUSED Mesh[{meshIndex}] — not referenced by any positive MeshCount permutation");
                }
            }

            Console.WriteLine();
            Console.WriteLine(
                $"Total meshes: {meshCount}, Referenced: {referencedMeshCount}, " +
                $"Unreferenced: {meshCount - referencedMeshCount}, Shared by multiple perms: {sharedMeshCount}");
            Console.WriteLine();
        }

        private void PrintSep27VariantRuntimeInfo()
        {
            Console.WriteLine("=== HLMT VARIANTS (Sep27) ===");
            var variants = Definition.VariantsHalo4280911;
            if (variants == null || variants.Count == 0)
            {
                Console.WriteLine("No Sep27 variant block data is present.");
                return;
            }

            bool allRuntimeSelectorsZero = true;

            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                var variant = variants[variantIndex];
                string variantName = Cache.StringTable.GetString(variant.Name) ?? $"<{variant.Name}>";
                string runtimeRegionArray = variant.RuntimeVariantRegionIndices == null
                    ? "(null)"
                    : string.Join(", ", variant.RuntimeVariantRegionIndices.Select(entry => entry.RuntimeRegionIndex));

                Console.WriteLine(
                    $"  Variant[{variantIndex}] \"{variantName}\": " +
                    $"InstanceGroup={variant.InstanceGroup}, RuntimeVariantRegionIndices=[{runtimeRegionArray}]");

                if (variant.RuntimeVariantRegionIndices != null &&
                    variant.RuntimeVariantRegionIndices.Any(entry => entry.RuntimeRegionIndex != 0))
                {
                    allRuntimeSelectorsZero = false;
                }

                if (variant.Regions == null)
                    continue;

                for (int regionIndex = 0; regionIndex < variant.Regions.Count; regionIndex++)
                {
                    var region = variant.Regions[regionIndex];
                    string regionName = Cache.StringTable.GetString(region.RegionName) ?? $"<{region.RegionName}>";
                    Console.WriteLine(
                        $"    Region[{regionIndex}] \"{regionName}\": " +
                        $"RuntimeRegionIndex={region.RuntimeRegionIndex}, ParentVariant={region.ParentVariant}");

                    if (region.RuntimeRegionIndex != 0)
                        allRuntimeSelectorsZero = false;

                    if (region.Permutations == null)
                        continue;

                    for (int permutationIndex = 0; permutationIndex < region.Permutations.Count; permutationIndex++)
                    {
                        var permutation = region.Permutations[permutationIndex];
                        string permutationName = Cache.StringTable.GetString(permutation.PermutationName) ?? $"<{permutation.PermutationName}>";
                        Console.WriteLine(
                            $"      Perm[{permutationIndex}] \"{permutationName}\": " +
                            $"RuntimePermutationIndex={permutation.RuntimePermutationIndex}");

                        if (permutation.RuntimePermutationIndex != 0)
                            allRuntimeSelectorsZero = false;
                    }
                }
            }

            if (allRuntimeSelectorsZero)
            {
                Console.WriteLine();
                Console.WriteLine("WARNING: All Sep27 hlmt runtime variant selectors are still 0. This likely indicates a remaining hlmt layout mismatch.");
            }
        }

        private static string FormatMeshIndices(IEnumerable<int> meshIndices)
        {
            var ordered = meshIndices?.OrderBy(index => index).ToList() ?? new List<int>();
            return ordered.Count == 0 ? "(none)" : string.Join(", ", ordered);
        }

        private void DiagnoseGeneric(Stream cacheStream)
        {
            var mode = Cache.Deserialize<SharedDefs.RenderModel>(cacheStream, Definition.RenderModel);

            Console.WriteLine("=== Generic RenderModel Diagnosis ===");
            Console.WriteLine($"Regions: {mode.Regions?.Count ?? 0}");
            Console.WriteLine($"Nodes: {mode.Nodes?.Count ?? 0}");
            Console.WriteLine($"Materials: {mode.Materials?.Count ?? 0}");
            Console.WriteLine($"Meshes: {mode.Geometry?.Meshes?.Count ?? 0}");
            Console.WriteLine();

            if (mode.Regions == null)
                return;

            for (int regionIndex = 0; regionIndex < mode.Regions.Count; regionIndex++)
            {
                var region = mode.Regions[regionIndex];
                string regionName = Cache.StringTable.GetString(region.Name) ?? $"<{region.Name}>";
                Console.WriteLine($"  Region[{regionIndex}] \"{regionName}\":");
                if (region.Permutations == null)
                    continue;

                for (int permutationIndex = 0; permutationIndex < region.Permutations.Count; permutationIndex++)
                {
                    var permutation = region.Permutations[permutationIndex];
                    string permutationName = Cache.StringTable.GetString(permutation.Name) ?? $"<{permutation.Name}>";
                    Console.WriteLine(
                        $"    Perm[{permutationIndex}] \"{permutationName}\": " +
                        $"MeshIndex={permutation.MeshIndex}, MeshCount={permutation.MeshCount}");
                }
            }
        }
    }
}
