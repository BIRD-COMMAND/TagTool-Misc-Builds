using System;
using System.IO;
using TagTool.Cache;
using TagTool.Tags.Definitions;

namespace TagTool.Bitmaps.Utils
{
    // Converts Halo 4 Sep27 (CacheVersion.Halo4280911) bitmaps to BaseBitmap.
    // Mirrors BitmapConverterGen3 in structure: iterates (layer, mip) surfaces,
    // untiles each via BitmapExtractorGen4, and returns a flat BaseBitmap blob.
    // No format conversion is performed — Gen4 Sep27 uses the same Xbox 360 DXT
    // block formats that the DDS export path already handles.
    public class BitmapConverterGen4
    {
        public GameCache Cache { get; }

        public BitmapConverterGen4(GameCache cache)
        {
            Cache = cache;
        }

        public BaseBitmap ConvertBitmap(Bitmap bitmap, int imageIndex, string tagName, bool forDDS = false)
        {
            var extractor = new BitmapExtractorGen4(Cache, bitmap, imageIndex);
            if (extractor.HasInvalidResource)
                return null;

            // Clone and correct image metadata from the resource definition.
            // Derive Format from the GPU format in the resource rather than trusting the
            // bitmap tag's Format field, which may be stale or incorrect in Sep27 prototypes.
            var image = bitmap.Images[imageIndex].DeepCloneV2();
            image.Width       = extractor.Width;
            image.Height      = extractor.Height;
            image.Depth       = extractor.Depth;
            image.Type        = extractor.Type;
            image.MipmapCount = (sbyte)extractor.MipmapCount;
            image.Format      = extractor.ResourceFormat;

            int mipCount   = image.MipmapCount + 1;
            int layerCount = image.Type == BitmapType.CubeMap ? 6 : Math.Max(1, (int)image.Depth);

            var result = new MemoryStream();
            foreach (var (layerIndex, mipLevel) in BitmapUtils.GetBitmapSurfacesEnumerable(layerCount, mipCount, forDDS))
            {
                byte[] surface = extractor.ExtractSurface(layerIndex, mipLevel, out _, out _);
                if (surface == null)
                    surface = Array.Empty<byte>();

                result.Write(surface, 0, surface.Length);
            }

            var resultBitmap    = new BaseBitmap(image);
            resultBitmap.Data        = result.ToArray();
            resultBitmap.MipMapCount = mipCount;
            return resultBitmap;
        }
    }
}
