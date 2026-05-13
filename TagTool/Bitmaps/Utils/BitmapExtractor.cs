using System;
using System.IO;
using TagTool.Cache;
using TagTool.IO;
using TagTool.Tags.Definitions;
using TagTool.Bitmaps.DDS;
using TagTool.Bitmaps.Utils;

namespace TagTool.Bitmaps
{
    public static class BitmapExtractor
    {
        private static bool UsePrototypeGen4BitmapPath(GameCache cache)
        {
            return cache.Version == CacheVersion.Halo4220811 ||
                   cache.Version == CacheVersion.Halo4280911;
        }

        private static byte[] ExtractPrototypeGen4BitmapData(GameCache cache, Bitmap bitmap, int imageIndex, ref Bitmap.Image image)
        {
            var resourceReference = bitmap.HardwareTextures[imageIndex];
            var resourceDefinition = cache.ResourceCache.GetBitmapTextureInteropResourceGen4(resourceReference);
            if (resourceDefinition?.TextureInterop?.Definition == null)
            {
                Console.Error.WriteLine("No Gen4 resource associated to this bitmap.");
                return null;
            }

            var textureInterop = resourceDefinition.TextureInterop.Definition;
            image.Width = textureInterop.Width;
            image.Height = textureInterop.Height;
            image.Depth = textureInterop.Depth;
            image.Type = (BitmapType)textureInterop.Type;
            image.MipmapCount = (sbyte)Math.Max(0, textureInterop.TotalMipmapCount - 1);

            var primary = textureInterop.PixelData?.Data ?? Array.Empty<byte>();
            var secondary = textureInterop.MediumResData?.Data ?? Array.Empty<byte>();

            if (secondary.Length == 0)
                return primary;

            var result = new byte[secondary.Length + primary.Length];
            Array.Copy(secondary, 0, result, 0, secondary.Length);
            Array.Copy(primary, 0, result, secondary.Length, primary.Length);
            return result;
        }

        public static byte[] ExtractBitmapData(GameCache cache, Bitmap bitmap, int imageIndex, ref Bitmap.Image image)
        {
            if (UsePrototypeGen4BitmapPath(cache))
                return ExtractPrototypeGen4BitmapData(cache, bitmap, imageIndex, ref image);

            var resourceReference = bitmap.HardwareTextures[imageIndex];
            var resourceDefinition = cache.ResourceCache.GetBitmapTextureInteropResource(resourceReference);
            if (cache is GameCacheHaloOnlineBase)
            {
                if(resourceDefinition != null)
                {
                    image.Width = resourceDefinition.Texture.Definition.Bitmap.Width;
                    image.Height = resourceDefinition.Texture.Definition.Bitmap.Height;
                    return resourceDefinition.Texture.Definition.PrimaryResourceData.Data;
                }
                else
                {
                    Console.Error.WriteLine("No resource associated to this bitmap.");
                    return null;
                }
            }
            else if(cache.GetType() == typeof(GameCacheGen3))
            {
                if (resourceDefinition != null)
                {
                    var bitmapTextureInteropDefinition = resourceDefinition.Texture.Definition.Bitmap;

                    if(bitmapTextureInteropDefinition.HighResInSecondaryResource == 1)
                    {
                        var result = new byte[resourceDefinition.Texture.Definition.PrimaryResourceData.Data.Length + resourceDefinition.Texture.Definition.SecondaryResourceData.Data.Length];
                        Array.Copy(resourceDefinition.Texture.Definition.PrimaryResourceData.Data, 0, result, 0, resourceDefinition.Texture.Definition.PrimaryResourceData.Data.Length);
                        Array.Copy(resourceDefinition.Texture.Definition.SecondaryResourceData.Data, 0, result, resourceDefinition.Texture.Definition.PrimaryResourceData.Data.Length, resourceDefinition.Texture.Definition.SecondaryResourceData.Data.Length);
                        return result;
                    }
                    else
                    {
                        return resourceDefinition.Texture.Definition.PrimaryResourceData.Data;
                    }
                }
                else
                {
                    Console.Error.WriteLine("No resource associated to this bitmap.");
                    return null;
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public static BaseBitmap ExtractBitmap(GameCache cache, Bitmap bitmap, int imageIndex, string tagName, bool forDDS = true)
        {
            if (cache is GameCacheHaloOnlineBase)
            {
                var image = bitmap.Images[imageIndex].DeepCloneV2();
                return new BaseBitmap(image, ExtractBitmapData(cache, bitmap, imageIndex, ref image));
            }
            else if (cache.Version == CacheVersion.Halo4280911)
            {
                // Sep27 prototype: MediumResData = mip0 tiled surface, PixelData = mip1+.
                // Route through the proper Xbox 360 untiling path.
                return new BitmapConverterGen4(cache).ConvertBitmap(bitmap, imageIndex, tagName, forDDS);
            }
            else if (UsePrototypeGen4BitmapPath(cache)) // Halo4220811: raw fallback unchanged
            {
                var image = bitmap.Images[imageIndex].DeepCloneV2();
                return new BaseBitmap(image, ExtractBitmapData(cache, bitmap, imageIndex, ref image));
            }
            else if (CacheVersionDetection.GetGeneration(cache.Version) ==  CacheGeneration.Third)
            {
                return new BitmapConverterGen3(cache).ConvertBitmap(bitmap, imageIndex, tagName, forDDS);
            }

            return null;
        }

        public static DDSFile ExtractBitmap(GameCache cache, Bitmap bitmap, int imageIndex, string tagName)
        {
            var baseBitmap = ExtractBitmap(cache, bitmap, imageIndex, tagName, true);
            if (baseBitmap == null)
                return null;

            return new DDSFile(baseBitmap);
        }

        public static byte[] ExtractBitmapToDDSArray(GameCache cache, Bitmap bitmap, int imageIndex, string tagName)
        {
            var ddsFile = ExtractBitmap(cache, bitmap, imageIndex, tagName);
            var stream = new MemoryStream();
            using(var writer = new EndianWriter(stream))
            {
                ddsFile.Write(writer);
            }
            var result = stream.ToArray();
            stream.Dispose();
            return result;
        }
    }
}
