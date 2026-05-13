using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using TagTool.Cache;
using TagTool.Commands.Porting.Gen4;
using TagTool.Common;
using TagTool.Geometry.Export;
using TagTool.Tags.Definitions;

namespace TagTool.Geometry.Jms
{
    public enum ModelJmsExportKind
    {
        Render,
        Collision,
        Physics
    }

    public sealed class ModelJmsExportItemReport
    {
        public ModelJmsExportKind Kind { get; init; }
        public string SelectedModelTagPath { get; init; } = string.Empty;
        public string ReferencedTagPath { get; init; }
        public string OutputPath { get; init; }
        public bool Attempted { get; init; }
        public bool Exported { get; init; }
        public bool Skipped { get; init; }
        public bool UsedDtoPath { get; init; }
        public bool UsedLegacyPath => Attempted && Exported && !UsedDtoPath;
        public string Message { get; init; } = string.Empty;
        public Exception Exception { get; init; }
    }

    public sealed class ModelJmsExportBatchReport
    {
        public string SelectedModelTagPath { get; init; } = string.Empty;
        public string OutputDirectory { get; init; } = string.Empty;
        public TimeSpan Duration { get; init; }
        public IReadOnlyList<ModelJmsExportItemReport> Items { get; init; } = Array.Empty<ModelJmsExportItemReport>();
    }

    public static class ModelJmsExportService
    {
        public static ModelJmsExportBatchReport ExportAllModelJms(
            GameCache cache,
            CachedTag modelTag,
            Model model,
            string outputDirectory)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (modelTag == null)
                throw new ArgumentNullException(nameof(modelTag));
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            string tagPath = modelTag.Name ?? modelTag.Index.ToString("X8");
            string baseName = GetTagLeafName(modelTag);

            var sw = Stopwatch.StartNew();

            var items = new List<ModelJmsExportItemReport>
            {
                ExportModelJms(cache, modelTag, model, ModelJmsExportKind.Render, Path.Combine(fullOutputDirectory, $"{baseName}_render.jms"), skipMissingReference: true),
                ExportModelJms(cache, modelTag, model, ModelJmsExportKind.Collision, Path.Combine(fullOutputDirectory, $"{baseName}_collision.jms"), skipMissingReference: true),
                ExportModelJms(cache, modelTag, model, ModelJmsExportKind.Physics, Path.Combine(fullOutputDirectory, $"{baseName}_physics.jms"), skipMissingReference: true)
            };

            sw.Stop();

            return new ModelJmsExportBatchReport
            {
                SelectedModelTagPath = tagPath,
                OutputDirectory = fullOutputDirectory,
                Duration = sw.Elapsed,
                Items = items,
            };
        }

        public static ModelJmsExportItemReport ExportModelJms(
            GameCache cache,
            CachedTag modelTag,
            Model model,
            ModelJmsExportKind kind,
            string outputPath,
            bool skipMissingReference = false)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            string tagPath = modelTag?.Name ?? modelTag?.Index.ToString("X8") ?? "model";
            CachedTag reference = GetReference(model, kind);
            string referencePath = reference == null ? null : (reference.Name ?? reference.Index.ToString("X8"));
            string fullOutputPath = NormalizeJmsOutputPath(outputPath);

            if (reference == null && skipMissingReference)
            {
                return new ModelJmsExportItemReport
                {
                    Kind = kind,
                    SelectedModelTagPath = tagPath,
                    ReferencedTagPath = null,
                    OutputPath = fullOutputPath,
                    Attempted = false,
                    Exported = false,
                    Skipped = true,
                    UsedDtoPath = false,
                    Message = $"No {GetKindTagName(kind)} reference.",
                };
            }

            string directory = Path.GetDirectoryName(fullOutputPath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            try
            {
                var jms = new JmsFormat();
                bool usedDtoPath = false;
                bool attempted = false;
                string message = string.Empty;

                using (var cacheStream = cache.OpenCacheRead())
                {
                    BuildNodes(jms, cache, model, cacheStream);

                    switch (kind)
                    {
                        case ModelJmsExportKind.Render:
                            if (reference != null)
                            {
                                attempted = true;
                                usedDtoPath = ExportRender(cache, cacheStream, jms, reference, model);
                            }
                            else
                            {
                                message = "No render_model reference; wrote nodes-only JMS.";
                            }
                            break;
                        case ModelJmsExportKind.Collision:
                            if (reference != null)
                            {
                                attempted = true;
                                usedDtoPath = ExportCollision(cache, cacheStream, jms, reference);
                            }
                            else
                            {
                                message = "No collision_model reference; wrote nodes-only JMS.";
                            }
                            break;
                        case ModelJmsExportKind.Physics:
                            if (reference != null)
                            {
                                attempted = true;
                                usedDtoPath = ExportPhysics(cache, cacheStream, jms, reference);
                            }
                            else
                            {
                                message = "No physics_model reference; wrote nodes-only JMS.";
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
                    }
                }

                jms.Write(new FileInfo(fullOutputPath));

                return new ModelJmsExportItemReport
                {
                    Kind = kind,
                    SelectedModelTagPath = tagPath,
                    ReferencedTagPath = referencePath,
                    OutputPath = fullOutputPath,
                    Attempted = attempted,
                    Exported = true,
                    Skipped = false,
                    UsedDtoPath = usedDtoPath,
                    Message = message,
                };
            }
            catch (Exception ex)
            {
                return new ModelJmsExportItemReport
                {
                    Kind = kind,
                    SelectedModelTagPath = tagPath,
                    ReferencedTagPath = referencePath,
                    OutputPath = fullOutputPath,
                    Attempted = true,
                    Exported = false,
                    Skipped = false,
                    UsedDtoPath = false,
                    Message = ex.Message,
                    Exception = ex,
                };
            }
        }

        private static bool ExportRender(GameCache cache, Stream cacheStream, JmsFormat jms, CachedTag renderTag, Model model)
        {
            if (cache.Version == CacheVersion.Halo4280911)
            {
                var sharedMode = cache.Deserialize<RenderModel>(cacheStream, renderTag);
                if (sharedMode.GeometryHalo4280911?.ApiResource == null)
                    throw new InvalidDataException("Sep27 render model has no geometry resource.");
                var modeGen3 = RenderModelConverter.ConvertSep27(cache, sharedMode);
                var resourceDef = cache.ResourceCache.GetRenderGeometryApiResourceDefinitionGen4(sharedMode.GeometryHalo4280911.ApiResource);
                if (resourceDef == null)
                    throw new InvalidDataException("Could not load Sep27 render geometry API resource.");
                var resource = RenderModelConverter.ConvertResource(resourceDef);
                modeGen3.Geometry.SetResourceBuffers(resource, true);
                var analysis = Sep27RenderModelAnalyzer.Analyze(cache, model, sharedMode, modeGen3);
                new JmsModeExporter(cache, jms).Export(modeGen3, analysis);
                return true;
            }

            var mode = cache.Deserialize<RenderModel>(cacheStream, renderTag);
            var renderResource = cache.ResourceCache.GetRenderGeometryApiResourceDefinition(mode.Geometry.Resource);
            mode.Geometry.SetResourceBuffers(renderResource, true);

            var exporter = new JmsModeExporter(cache, jms);
            string tagName = renderTag.Name ?? renderTag.Index.ToString("X8");

            var dto = TryAdaptRenderModel(mode, cache, tagName);
            if (dto != null)
            {
                exporter.Export(dto);
                return true;
            }

            exporter.Export(mode);
            return false;
        }

        private static bool ExportCollision(GameCache cache, Stream cacheStream, JmsFormat jms, CachedTag collisionTag)
        {
            var coll = cache.Deserialize<CollisionModel>(cacheStream, collisionTag);
            var exporter = new JmsCollExporter(cache, jms);
            string tagName = collisionTag.Name ?? collisionTag.Index.ToString("X8");

            var dto = TryAdaptCollisionModel(coll, cache, tagName);
            if (dto != null)
            {
                exporter.Export(dto);
                return true;
            }

            exporter.Export(coll);
            return false;
        }

        private static bool ExportPhysics(GameCache cache, Stream cacheStream, JmsFormat jms, CachedTag physicsTag)
        {
            var phmo = cache.Deserialize<PhysicsModel>(cacheStream, physicsTag);
            var exporter = new JmsPhmoExporter(cache, jms);
            string tagName = physicsTag.Name ?? physicsTag.Index.ToString("X8");

            var dto = TryAdaptPhysicsModel(phmo, cache, tagName);
            if (dto != null)
            {
                exporter.Export(dto);
                return true;
            }

            exporter.Export(phmo);
            return false;
        }

        private static void BuildNodes(JmsFormat jms, GameCache cache, Model model, Stream cacheStream)
        {
            bool isSep27 = cache.Version == CacheVersion.Halo4280911;

            if (isSep27 && model.RenderModel != null)
            {
                var mode = cache.Deserialize<RenderModel>(cacheStream, model.RenderModel);
                BuildNodesFromModeSep27(jms, cache, mode);
            }
            else if (model.Nodes != null && model.Nodes.Count > 0)
            {
                BuildNodesFromHlmt(jms, cache, model);
            }
            else if (model.RenderModel != null)
            {
                var mode = cache.Deserialize<RenderModel>(cacheStream, model.RenderModel);
                BuildNodesFromMode(jms, cache, mode);
            }
            else
            {
                throw new InvalidDataException("Model has no nodes, couldn't build JMS.");
            }
        }

        private static void BuildNodesFromModeSep27(JmsFormat jms, GameCache cache, RenderModel mode)
        {
            var nodes = mode.NodesHalo4280911;
            if (nodes == null || nodes.Count == 0)
                return;

            foreach (var node in nodes)
            {
                var newnode = new JmsFormat.JmsNode
                {
                    Name = cache.StringTable.GetString(node.Name) ?? node.Name.ToString(),
                    ParentNodeIndex = node.ParentNode,
                    Rotation = node.DefaultRotation,
                    Position = new RealVector3d(node.DefaultTranslation.X, node.DefaultTranslation.Y, node.DefaultTranslation.Z) * 100.0f
                };
                if (!newnode.Name.StartsWith("b_"))
                    newnode.Name = "b_" + newnode.Name;
                if (newnode.ParentNodeIndex != -1)
                {
                    Matrix4x4 transform = MatrixFromNode(newnode.Rotation, newnode.Position);
                    Matrix4x4 parentTransform = MatrixFromNode(jms.Nodes[newnode.ParentNodeIndex].Rotation,
                        jms.Nodes[newnode.ParentNodeIndex].Position);
                    Matrix4x4 result = Matrix4x4.Multiply(transform, parentTransform);

                    Vector3 outScale = new Vector3();
                    Vector3 outTranslate = new Vector3();
                    Quaternion outRotate = new Quaternion();
                    Matrix4x4.Decompose(result, out outScale, out outRotate, out outTranslate);
                    newnode.Position = new RealVector3d(outTranslate.X * outScale.X, outTranslate.Y * outScale.Y, outTranslate.Z * outScale.Z);
                    newnode.Rotation = new RealQuaternion(outRotate.X, outRotate.Y, outRotate.Z, outRotate.W);
                }
                jms.Nodes.Add(newnode);
            }
        }

        private static Matrix4x4 MatrixFromNode(RealQuaternion rotation, RealVector3d position)
        {
            var quat = new Quaternion(rotation.I, rotation.J, rotation.K, rotation.W);

            Matrix4x4 rot = Matrix4x4.CreateFromQuaternion(quat);
            rot.Translation = new Vector3(position.I, position.J, position.K);

            return rot;
        }

        private static void BuildNodesFromMode(JmsFormat jms, GameCache cache, RenderModel mode)
        {
            foreach (var node in mode.Nodes)
            {
                var newnode = new JmsFormat.JmsNode
                {
                    Name = cache.StringTable.GetString(node.Name) ?? node.Name.ToString(),
                    ParentNodeIndex = node.ParentNode,
                    Rotation = node.DefaultRotation,
                    Position = new RealVector3d(node.DefaultTranslation.X, node.DefaultTranslation.Y, node.DefaultTranslation.Z) * 100.0f
                };
                if (!newnode.Name.StartsWith("b_"))
                    newnode.Name = "b_" + newnode.Name;
                if (newnode.ParentNodeIndex != -1)
                {
                    Matrix4x4 transform = MatrixFromNode(newnode.Rotation, newnode.Position);
                    Matrix4x4 parentTransform = MatrixFromNode(jms.Nodes[newnode.ParentNodeIndex].Rotation,
                        jms.Nodes[newnode.ParentNodeIndex].Position);
                    Matrix4x4 result = Matrix4x4.Multiply(transform, parentTransform);

                    Vector3 outScale = new Vector3();
                    Vector3 outTranslate = new Vector3();
                    Quaternion outRotate = new Quaternion();
                    Matrix4x4.Decompose(result, out outScale, out outRotate, out outTranslate);
                    newnode.Position = new RealVector3d(outTranslate.X * outScale.X, outTranslate.Y * outScale.Y, outTranslate.Z * outScale.Z);
                    newnode.Rotation = new RealQuaternion(outRotate.X, outRotate.Y, outRotate.Z, outRotate.W);
                }

                jms.Nodes.Add(newnode);
            }
        }

        private static void BuildNodesFromHlmt(JmsFormat jms, GameCache cache, Model hlmt)
        {
            foreach (var node in hlmt.Nodes)
            {
                var newnode = new JmsFormat.JmsNode
                {
                    Name = cache.StringTable.GetString(node.Name) ?? node.Name.ToString(),
                    ParentNodeIndex = node.ParentNode,
                    Rotation = node.DefaultRotation,
                    Position = new RealVector3d(node.DefaultTranslation.X, node.DefaultTranslation.Y, node.DefaultTranslation.Z) * 100.0f
                };
                if (!newnode.Name.StartsWith("b_"))
                    newnode.Name = "b_" + newnode.Name;
                if (newnode.ParentNodeIndex != -1)
                {
                    Matrix4x4 transform = MatrixFromNode(newnode.Rotation, newnode.Position);
                    Matrix4x4 parentTransform = MatrixFromNode(jms.Nodes[newnode.ParentNodeIndex].Rotation,
                        jms.Nodes[newnode.ParentNodeIndex].Position);
                    Matrix4x4 result = Matrix4x4.Multiply(transform, parentTransform);

                    Vector3 outScale = new Vector3();
                    Vector3 outTranslate = new Vector3();
                    Quaternion outRotate = new Quaternion();
                    Matrix4x4.Decompose(result, out outScale, out outRotate, out outTranslate);
                    newnode.Position = new RealVector3d(outTranslate.X * outScale.X, outTranslate.Y * outScale.Y, outTranslate.Z * outScale.Z);
                    newnode.Rotation = new RealQuaternion(outRotate.X, outRotate.Y, outRotate.Z, outRotate.W);
                }

                jms.Nodes.Add(newnode);
            }
        }

        private static ExportRenderModel TryAdaptRenderModel(RenderModel mode, GameCache cache, string tagName)
        {
            try
            {
                return new RenderModelExportAdapter().AdaptRenderModel(mode, cache, tagName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RenderModelExportAdapter] {ex.Message}; falling back to legacy path.");
                return null;
            }
        }

        private static ExportCollisionModel TryAdaptCollisionModel(CollisionModel coll, GameCache cache, string tagName)
        {
            try
            {
                return new CollisionModelExportAdapter().AdaptCollisionModel(coll, cache, tagName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CollisionModelExportAdapter] {ex.Message}; falling back to legacy path.");
                return null;
            }
        }

        private static ExportPhysicsModel TryAdaptPhysicsModel(PhysicsModel phmo, GameCache cache, string tagName)
        {
            try
            {
                return new PhysicsModelExportAdapter().AdaptPhysicsModel(phmo, cache, tagName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PhysicsModelExportAdapter] {ex.Message}; falling back to legacy path.");
                return null;
            }
        }

        private static CachedTag GetReference(Model model, ModelJmsExportKind kind)
        {
            return kind switch
            {
                ModelJmsExportKind.Render => model.RenderModel,
                ModelJmsExportKind.Collision => model.CollisionModel,
                ModelJmsExportKind.Physics => model.PhysicsModel,
                _ => null
            };
        }

        private static string GetKindTagName(ModelJmsExportKind kind)
        {
            return kind switch
            {
                ModelJmsExportKind.Render => "render_model",
                ModelJmsExportKind.Collision => "collision_model",
                ModelJmsExportKind.Physics => "physics_model",
                _ => "model"
            };
        }

        private static string GetTagLeafName(CachedTag tag)
        {
            if (tag.Name == null)
                return tag.Index.ToString("X8");

            string leaf = Path.GetFileName(tag.Name);
            return string.IsNullOrWhiteSpace(leaf) ? tag.Index.ToString("X8") : leaf;
        }

        private static string NormalizeJmsOutputPath(string outputPath)
        {
            string pathWithExtension = outputPath.EndsWith(".jms", StringComparison.OrdinalIgnoreCase)
                ? outputPath
                : outputPath + ".jms";

            return Path.GetFullPath(pathWithExtension);
        }
    }
}
