using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TagTool.Bitmaps;
using TagTool.Bitmaps.DDS;
using TagTool.Bitmaps.Export;
using TagTool.Bitmaps.Utils;
using TagTool.Cache;
using TagTool.Commands.Common;
using TagTool.IO;
using TagTool.Tags;
using TagTool.Tags.Definitions;

namespace TagTool.Commands.Bitmaps
{
    class ExtractBitmapCommand : Command
    {
        private GameCache Cache  { get; }
        private CachedTag Tag    { get; }
        private Bitmap    Bitmap { get; }

        private static readonly BitmapExportAdapter Adapter = new BitmapExportAdapter();

        public ExtractBitmapCommand(GameCache cache, CachedTag tag, Bitmap bitmap)
            : base(false,
                  "ExtractBitmap",
                  "Extracts a bitmap to a DDS file.",
                  "ExtractBitmap <output directory>",
                  "Extracts a bitmap to a DDS file.")
        {
            Cache  = cache;
            Tag    = tag;
            Bitmap = bitmap;
        }

        public override object Execute(List<string> args)
        {
            string directory;
            if (args.Count == 1)
                directory = args[0];
            else if (args.Count == 0)
                directory = "Bitmaps";
            else
                return new TagToolError(CommandError.ArgCount);

            Directory.CreateDirectory(directory);

            var    ddsOutDir = directory;
            string name;

            if (Tag.Name != null)
            {
                var split = Tag.Name.Split('\\');
                name = split[split.Length - 1];
            }
            else
            {
                name = Tag.Index.ToString("X8");
            }

            if (Bitmap.Images.Count > 1)
            {
                ddsOutDir = Path.Combine(directory, name);
                Directory.CreateDirectory(ddsOutDir);
            }

            for (int i = 0; i < Bitmap.Images.Count; i++)
            {
                var bitmapName = (Bitmap.Images.Count > 1) ? i.ToString() : name;
                var outPath    = Path.Combine(ddsOutDir, bitmapName);

                if (File.Exists(outPath + ".dds"))
                {
                    var bitmLongName = bitmapName + CreateLongName(Tag);
                    outPath = Path.Combine(ddsOutDir, bitmLongName);
                }
                outPath += ".dds";

                bool written = TryWriteNewPath(i, outPath);
                if (!written)
                    written = TryWriteLegacyPath(i, outPath);

                if (!written)
                    return new TagToolError(CommandError.OperationFailed,
                        $"Could not extract bitmap image {i} from tag {Tag.Name ?? Tag.Index.ToString("X8")}.");
            }

            Console.WriteLine("Done!");
            return true;
        }

        // -----------------------------------------------------------------------
        // New path: BitmapExportAdapter → DdsWriter
        // -----------------------------------------------------------------------

        private bool TryWriteNewPath(int imageIndex, string outPath)
        {
            ExportBitmap eb;
            try
            {
                eb = Adapter.AdaptBitmap(Bitmap, imageIndex, Cache, Tag.Name ?? Tag.Index.ToString("X8"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[BitmapExportAdapter] {ex.Message}");
                return false;
            }

            if (eb == null)
                return false;

            bool dx10 = DdsWriter.RequiresDx10(eb.Format, eb.IsArray && !eb.IsCubeMap);
            PrintDiagnostics(eb, outPath, dx10);

            try
            {
                using var fs = File.Open(outPath, FileMode.Create, FileAccess.Write);
                DdsWriter.Write(eb, fs);
                return true;
            }
            catch (NotSupportedException ex)
            {
                Console.Error.WriteLine(
                    $"[DdsWriter] Format {eb.Format} not supported in new writer; " +
                    $"falling back to legacy. ({ex.Message})");
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DdsWriter] Write failed: {ex.Message}; falling back to legacy.");
                return false;
            }
        }

        // -----------------------------------------------------------------------
        // Legacy fallback: BitmapExtractor → DDSFile
        // -----------------------------------------------------------------------

        private bool TryWriteLegacyPath(int imageIndex, string outPath)
        {
            DDSFile ddsFile;
            try
            {
                ddsFile = BitmapExtractor.ExtractBitmap(Cache, Bitmap, imageIndex, Tag.Name);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[BitmapExtractor] {ex.Message}");
                return false;
            }

            if (ddsFile == null || ddsFile.BitmapData == null)
                return false;

            using var fileStream = File.Open(outPath, FileMode.Create, FileAccess.Write);
            using var writer     = new EndianWriter(fileStream, EndianFormat.LittleEndian);
            ddsFile.Write(writer);
            return true;
        }

        // -----------------------------------------------------------------------
        // Diagnostics
        // -----------------------------------------------------------------------

        private static void PrintDiagnostics(ExportBitmap eb, string outPath, bool dx10)
        {
            Console.WriteLine($"  Tag:      {eb.TagPath}[{eb.ImageIndex}]");
            Console.WriteLine($"  Format:   {eb.FormatName}");
            Console.WriteLine($"  Type:     {eb.TypeName}");
            Console.WriteLine($"  Size:     {eb.Width}×{eb.Height}" +
                              (eb.Depth > 1 ? $"×{eb.Depth}" : ""));
            Console.WriteLine($"  Mips:     {eb.MipCount}");
            Console.WriteLine($"  Layers:   {eb.LayerCount}" +
                              (eb.IsCubeMap ? " (cube map)" : eb.IsArray ? " (array)" : ""));

            long rawBytes = 0;
            foreach (var layer in eb.Layers)
                if (layer?.PixelData != null)
                    rawBytes += layer.PixelData.Length;
            Console.WriteLine($"  Raw data: {rawBytes:N0} bytes");
            Console.WriteLine($"  Header:   {(dx10 ? "DX10 extension" : "legacy DDS")}");
            Console.WriteLine($"  Output:   {outPath}");
        }

        // -----------------------------------------------------------------------
        // Filename helpers (unchanged from original)
        // -----------------------------------------------------------------------

        public string CreateLongName(CachedTag tag)
        {
            string concatenation = " (";
            List<string> split = tag.Name.Split('\\').ToList();
            string[] cutKeywords = { "objects", "levels", "multi", "dlc", "solo", "characters" };

            split.RemoveAt(split.Count - 1);
            foreach (string s in cutKeywords)
            {
                if (split.Contains(s))
                    split.RemoveAt(split.IndexOf(s));
            }

            concatenation += string.Join("_", split.ToArray()) + ")";
            return concatenation;
        }
    }
}
