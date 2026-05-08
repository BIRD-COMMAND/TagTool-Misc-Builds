using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Tags.Definitions;

namespace TagTool.Bitmaps.Export
{
    /// <summary>
    /// Exporter-neutral bitmap image. Carries fully extracted pixel data ready for DDS writing.
    /// One ExportBitmap = one entry in Bitmap.Images[].
    /// Mirrors donor bitmap/mod.rs BitmapImage.
    /// </summary>
    public class ExportBitmap
    {
        public string TagPath;
        public int    ImageIndex;    // index into Bitmap.Images[]

        // Mip-0 dimensions.
        public int Width;
        public int Height;
        public int Depth;            // > 1 for 3D/volume textures

        public int  MipCount;
        public int  LayerCount;      // 6 for cube maps, N for arrays, 1 for 2D/3D

        public bool IsCubeMap;
        public bool IsArray;

        // BitmapFormat is used directly (it's an enum in TagTool.Bitmaps, not cache-coupled).
        // The DDS writer uses this to determine the DDS PIXELFORMAT / DXGI_FORMAT fields.
        public BitmapFormat Format;

        // One entry per face (cube: +X, -X, +Y, -Y, +Z, -Z in D3D order) /
        // slice (array/volume) / single layer (2D).
        // Each layer holds its own mip chain as raw bytes.
        public List<ExportBitmapLayer> Layers = new List<ExportBitmapLayer>();

        // Diagnostic helpers.
        public string FormatName => Format.ToString();
        public string TypeName   => IsCubeMap ? "CubeMap" : IsArray ? "Array" : Depth > 1 ? "Volume" : "2D";
    }

    /// <summary>
    /// One layer = one face / slice of the texture, containing all mip levels.
    /// Mirrors donor bitmap/mod.rs BitmapImage pixel slice logic.
    /// </summary>
    public class ExportBitmapLayer
    {
        // Raw pixel data for all mips of this layer in chain order (mip 0 first).
        // Extracted via Cache.ResourceCache.GetBitmapTextureInteropResource() /
        // GetBitmapTextureInteropResourceGen4(); no further byte-order swapping is needed
        // because pixel block data (DXT/BC) is layout-only, not multi-byte integer fields.
        public byte[] PixelData;

        public List<ExportBitmapMip> Mips = new List<ExportBitmapMip>();
    }

    /// <summary>
    /// Describes the location and dimensions of one mip level within an ExportBitmapLayer.
    /// </summary>
    public class ExportBitmapMip
    {
        public int Width;
        public int Height;
        public int Depth;

        // Byte offset and length within the parent ExportBitmapLayer.PixelData.
        public int DataOffset;
        public int DataLength;
    }

    /// <summary>
    /// Adapts a Bitmap tag into ExportBitmap DTOs by extracting pixel data via the resource cache.
    ///
    /// Resource loading:
    ///   Gen3 / HaloOnline: Cache.ResourceCache.GetBitmapTextureInteropResource(bitmap.HardwareTextures[i])
    ///   Gen4:              Cache.ResourceCache.GetBitmapTextureInteropResourceGen4(bitmap.HardwareTextures[i])
    ///   Interleaved (Gen3): Cache.ResourceCache.GetBitmapTextureInterleavedInteropResource(bitmap.InterleavedHardwareTextures[i])
    ///
    /// Returns null per image if the resource reference is invalid or cannot be loaded.
    /// </summary>
    public interface IExportBitmapAdapter
    {
        /// <summary>Adapt a single image by index.</summary>
        ExportBitmap AdaptBitmap(Bitmap bitmap, int imageIndex, GameCache cache, string tagPath);

        /// <summary>Adapt all images in the bitmap tag.</summary>
        List<ExportBitmap> AdaptAllBitmapImages(Bitmap bitmap, GameCache cache, string tagPath);
    }
}
