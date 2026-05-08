using System;
using System.Collections.Generic;
using TagTool.Bitmaps.Utils;
using TagTool.Cache;
using TagTool.Tags.Definitions;

namespace TagTool.Bitmaps.Export
{
    /// <summary>
    /// Implements IExportBitmapAdapter by delegating pixel extraction to the existing
    /// BitmapExtractor / BitmapConverterGen3 pipeline, then splitting the resulting flat
    /// blob into per-layer ExportBitmapLayer objects with per-mip offset descriptors.
    ///
    /// What this adapter does NOT do:
    ///   - Does not introduce blay/schema parsing.
    ///   - Does not read loose tag files.
    ///   - Does not assume any particular byte order for pixel block data (DXT/BC data
    ///     is layout-only and not multi-byte integers, so no byte-swap is needed).
    ///   - Does not perform any format conversion; that is left to BitmapConverterGen3.
    ///
    /// Gen4 support: not implemented in the underlying BitmapExtractor; returns null per image.
    /// TODO: add a Gen4 branch calling cache.ResourceCache.GetBitmapTextureInteropResourceGen4().
    /// </summary>
    public class BitmapExportAdapter : IExportBitmapAdapter
    {
        /// <inheritdoc/>
        public ExportBitmap AdaptBitmap(Bitmap bitmap, int imageIndex, GameCache cache, string tagPath)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            if (cache  == null) throw new ArgumentNullException(nameof(cache));
            if (imageIndex < 0 || imageIndex >= bitmap.Images.Count)
                throw new ArgumentOutOfRangeException(nameof(imageIndex));

            var image = bitmap.Images[imageIndex];

            // Delegate to the existing pipeline:
            //   HaloOnline → raw primary resource bytes
            //   Gen3       → BitmapConverterGen3 (Xbox tile deswizzle, format conversion,
            //                primary/secondary split, interleaved handling)
            //   Gen4       → returns null (not yet implemented)
            BaseBitmap baseBitmap;
            try
            {
                baseBitmap = BitmapExtractor.ExtractBitmap(cache, bitmap, imageIndex, tagPath, forDDS: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[BitmapExportAdapter] Extraction failed for {tagPath}[{imageIndex}]: {ex.Message}");
                return null;
            }

            if (baseBitmap == null || baseBitmap.Data == null)
                return null;

            bool isCubeMap = baseBitmap.Type == BitmapType.CubeMap;
            bool isArray   = baseBitmap.Type == BitmapType.Array;
            int  depth     = Math.Max(1, baseBitmap.Depth);

            // Layer count: cube = 6, array = depth slice count, 2D/volume = 1.
            int layerCount = isCubeMap ? 6 : isArray ? depth : 1;

            // MipMapCount in BaseBitmap is image.MipmapCount + 1 (includes the base level).
            int mipCount = Math.Max(1, baseBitmap.MipMapCount);

            var eb = new ExportBitmap
            {
                TagPath    = tagPath,
                ImageIndex = imageIndex,
                Width      = baseBitmap.Width,
                Height     = baseBitmap.Height,
                Depth      = depth,
                MipCount   = mipCount,
                LayerCount = layerCount,
                IsCubeMap  = isCubeMap,
                IsArray    = isArray,
                Format     = baseBitmap.Format,
            };

            // The flat blob (forDDS=true) is laid out layer-major, mip-minor:
            //   Layer 0: [Mip 0][Mip 1]...[Mip N-1]
            //   Layer 1: [Mip 0][Mip 1]...[Mip N-1]
            //   ...
            // Split into per-layer sub-arrays so ExportBitmapLayer.PixelData is self-contained.
            long chainBytes  = DdsWriter.SurfaceBytes(baseBitmap.Format, baseBitmap.Width, baseBitmap.Height, mipCount);
            long blobOffset  = 0;
            byte[] fullBlob  = baseBitmap.Data;

            for (int layer = 0; layer < layerCount; layer++)
            {
                int layerStart = (int)blobOffset;
                int layerLen   = (int)Math.Min(chainBytes, Math.Max(0, fullBlob.Length - layerStart));

                byte[] layerData = new byte[chainBytes];
                if (layerLen > 0)
                    Array.Copy(fullBlob, layerStart, layerData, 0, layerLen);

                var exportLayer = new ExportBitmapLayer { PixelData = layerData };

                long mipOffset = 0;
                for (int mip = 0; mip < mipCount; mip++)
                {
                    int  mw     = Math.Max(1, baseBitmap.Width  >> mip);
                    int  mh     = Math.Max(1, baseBitmap.Height >> mip);
                    int  md     = Math.Max(1, depth >> mip);
                    long mipLen = DdsWriter.LevelBytes(baseBitmap.Format, mw, mh);

                    exportLayer.Mips.Add(new ExportBitmapMip
                    {
                        Width      = mw,
                        Height     = mh,
                        Depth      = md,
                        DataOffset = (int)mipOffset,
                        DataLength = (int)mipLen,
                    });

                    mipOffset += mipLen;
                }

                eb.Layers.Add(exportLayer);
                blobOffset += chainBytes;
            }

            return eb;
        }

        /// <inheritdoc/>
        public List<ExportBitmap> AdaptAllBitmapImages(Bitmap bitmap, GameCache cache, string tagPath)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            var result = new List<ExportBitmap>(bitmap.Images.Count);
            for (int i = 0; i < bitmap.Images.Count; i++)
                result.Add(AdaptBitmap(bitmap, i, cache, tagPath));
            return result;
        }
    }
}
