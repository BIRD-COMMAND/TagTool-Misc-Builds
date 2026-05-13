using System;
using TagTool.Bitmaps;
using TagTool.Cache;
using TagTool.Direct3D.D3D9x;
using TagTool.Tags.Definitions;
using TagTool.Tags.Resources;
using TagTool.Tags.Resources.Gen4;

namespace TagTool.Bitmaps.Utils
{
    // Extracts per-surface pixel data for Halo 4 Sep27 (CacheVersion.Halo4280911) bitmaps.
    //
    // Sep27 resource layout vs Gen3 "HighResInSecondaryResource" layout:
    //
    //   Gen4 MediumResData  ←→  Gen3 SecondaryResourceData  (mip0 full physical tiled surface)
    //   Gen4 PixelData      ←→  Gen3 PrimaryResourceData    (mip1+ tiled chain)
    //   IsMediumResBitmapHalo4280911 == True  ←→  HighResInSecondaryResource == 1
    //
    // The existing XboxBitmapUtils.GetXboxBitmapLevelData already handles this split
    // correctly for Gen3; we just synthesise the right BitmapTextureInteropDefinition.
    public class BitmapExtractorGen4
    {
        private readonly BitmapTextureInteropDefinition SyntheticDef;
        private readonly byte[] PixelData;
        private readonly byte[] MediumResData;

        public bool HasInvalidResource => SyntheticDef == null;

        public BitmapExtractorGen4(GameCache cache, Bitmap bitmap, int imageIndex)
        {
            var resourceRef = bitmap.HardwareTextures[imageIndex];
            var resourceDef = cache.ResourceCache.GetBitmapTextureInteropResourceGen4(resourceRef);
            if (resourceDef?.TextureInterop?.Definition == null)
                return;

            var tex = resourceDef.TextureInterop.Definition;

            PixelData     = tex.PixelData?.Data     ?? Array.Empty<byte>();
            MediumResData = tex.MediumResData?.Data ?? Array.Empty<byte>();

            bool hasMediumRes = MediumResData.Length > 0 &&
                tex.IsMediumResBitmapHalo4280911 == RenderTextureInteropDefinition.BooleanEnum.True;

            SyntheticDef = new BitmapTextureInteropDefinition
            {
                Width                      = tex.Width,
                Height                     = tex.Height,
                Depth                      = (byte)Math.Max(1, (int)tex.Depth),
                MipmapCount                = (sbyte)Math.Max(0, tex.TotalMipmapCount - 1),
                D3DFormat                  = tex.XenonD3dFormat,
                BitmapType                 = (BitmapType)tex.Type,
                HighResInSecondaryResource = (byte)(hasMediumRes ? 1 : 0),
            };

            Console.Error.WriteLine($"[Gen4Diag] {tex.Width}x{tex.Height} mips={tex.TotalMipmapCount} type={tex.Type}" +
                $" d3dFmt=0x{tex.XenonD3dFormat:X8} gpuFmt={(tex.XenonD3dFormat & 0x3F)} endian={(tex.XenonD3dFormat >> 6) & 0x3} tiled={(tex.XenonD3dFormat >> 8) & 0x1}" +
                $" medResLen={MediumResData.Length} pixLen={PixelData.Length} hasMedRes={hasMediumRes}");
        }

        // Exposes corrected dimensions so the caller can patch Bitmap.Image metadata.
        // Only valid when HasInvalidResource == false.
        public short Width      => SyntheticDef != null ? SyntheticDef.Width                : (short)0;
        public short Height     => SyntheticDef != null ? SyntheticDef.Height               : (short)0;
        public sbyte Depth      => SyntheticDef != null ? (sbyte)SyntheticDef.Depth         : (sbyte)1;
        public BitmapType Type  => SyntheticDef != null ? SyntheticDef.BitmapType           : BitmapType.Texture2D;
        public int MipmapCount  => SyntheticDef != null ? SyntheticDef.MipmapCount          : 0;

        // Derives the high-level BitmapFormat from the GPU texture format stored in the resource.
        // Sep27 bitmap tags may not have reliable Format fields, so we always derive from the resource.
        public BitmapFormat ResourceFormat
        {
            get
            {
                if (SyntheticDef == null) return global::TagTool.Bitmaps.BitmapFormat.A8R8G8B8;
                var gpuFmt = XboxGraphics.XGGetGpuFormat(SyntheticDef.D3DFormat);
                switch (gpuFmt)
                {
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT1:
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT1_AS_16_16_16_16:
                        return global::TagTool.Bitmaps.BitmapFormat.Dxt1;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT2_3:
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT2_3_AS_16_16_16_16:
                        return global::TagTool.Bitmaps.BitmapFormat.Dxt3;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT4_5:
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT4_5_AS_16_16_16_16:
                        return global::TagTool.Bitmaps.BitmapFormat.Dxt5;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXN:
                        return global::TagTool.Bitmaps.BitmapFormat.Dxn;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT3A:
                        return global::TagTool.Bitmaps.BitmapFormat.Dxt3a;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_DXT5A:
                        return global::TagTool.Bitmaps.BitmapFormat.Dxt5a;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_CTX1:
                        return global::TagTool.Bitmaps.BitmapFormat.Ctx1;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_8_8_8_8:
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_8_8_8_8_AS_16_16_16_16:
                        return global::TagTool.Bitmaps.BitmapFormat.A8R8G8B8;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_5_6_5:
                        return global::TagTool.Bitmaps.BitmapFormat.R5G6B5;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_1_5_5_5:
                        return global::TagTool.Bitmaps.BitmapFormat.A1R5G5B5;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_4_4_4_4:
                        return global::TagTool.Bitmaps.BitmapFormat.A4R4G4B4;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_8:
                        return global::TagTool.Bitmaps.BitmapFormat.A8;
                    case D3D9xGPU.GPUTEXTUREFORMAT.GPUTEXTUREFORMAT_8_8:
                        return global::TagTool.Bitmaps.BitmapFormat.A8Y8;
                    default:
                        return global::TagTool.Bitmaps.BitmapFormat.A8R8G8B8;
                }
            }
        }

        public byte[] ExtractSurface(int layerIndex, int mipIndex, out int width, out int height)
        {
            if (HasInvalidResource)
            {
                width = height = 0;
                return null;
            }

            width  = Math.Max(1, SyntheticDef.Width  >> mipIndex);
            height = Math.Max(1, SyntheticDef.Height >> mipIndex);

            // Diagnostic: inverse-map untile with pixel-width (1024) — Sep27 data appears
            // to be tiled with XGAddress2DTiledOffset(x, y, pixelWidth, texelPitch).
            // Forward iteration leaves most blocks out-of-range, so use the inverse:
            // iterate physical block indices and scatter to logical (x,y) positions.
            if (mipIndex == 0 && layerIndex == 0 && SyntheticDef.HighResInSecondaryResource > 0 && SyntheticDef.Width == 1024 && SyntheticDef.Height == 1023)
            {
                var gpuFmt = XboxGraphics.XGGetGpuFormat(SyntheticDef.D3DFormat);
                XboxGraphics.XGGetBlockDimensions(gpuFmt, out uint blkW, out uint blkH);
                uint bpp = XboxGraphics.XGBitsPerPixelFromGpuFormat(gpuFmt);
                uint texelPitch = blkW * blkH * bpp / 8;  // 16 for DXT5

                uint logicalBlockW = 256;  // 1024px / 4px-per-block
                uint logicalBlockH = 256;
                uint tiledWidth = (uint)SyntheticDef.Width;  // 1024 — pixel-width tiling
                uint totalBlocks = (uint)(MediumResData.Length / texelPitch);
                byte[] src = MediumResData;

                // Variant H: byte-level un-tiling (texelPitch=1, width=pixelW=1024).
                // Hypothesis: Sep27 tiles the DXT bytes as 1-byte "texels" rather than 16-byte blocks.
                {
                    uint byteW  = tiledWidth;          // 1024 "byte-texels" wide
                    uint byteH  = (uint)src.Length / byteW; // 1024 rows
                    byte[] dst  = new byte[src.Length];
                    for (uint py = 0; py < byteH; py++)
                        for (uint px = 0; px < byteW; px++)
                        {
                            uint phys   = XboxGraphics.XGAddress2DTiledOffset(px, py, byteW, 1);
                            uint dstOff = py * byteW + px;
                            if (phys < src.Length && dstOff < dst.Length)
                                dst[dstOff] = src[phys];
                        }
                    XboxGraphics.XGEndianSwapSurface(SyntheticDef.D3DFormat, dst);
                    System.IO.File.WriteAllBytes(@"C:\Temp\sep27_varH_bytelevel_swap.bin", dst);
                    Console.Error.WriteLine($"[Gen4Diag] varH (byte-level+swap): {dst.Length} bytes");
                }

                // Variant I: byte-level un-tiling, no endian swap
                {
                    uint byteW = tiledWidth;
                    uint byteH = (uint)src.Length / byteW;
                    byte[] dst = new byte[src.Length];
                    for (uint py = 0; py < byteH; py++)
                        for (uint px = 0; px < byteW; px++)
                        {
                            uint phys   = XboxGraphics.XGAddress2DTiledOffset(px, py, byteW, 1);
                            uint dstOff = py * byteW + px;
                            if (phys < src.Length && dstOff < dst.Length)
                                dst[dstOff] = src[phys];
                        }
                    System.IO.File.WriteAllBytes(@"C:\Temp\sep27_varI_bytelevel_noswap.bin", dst);
                    Console.Error.WriteLine($"[Gen4Diag] varI (byte-level, no swap): {dst.Length} bytes");
                }
            }

            // GetXboxBitmapLevelData reads from MediumResData when level==0 and
            // HighResInSecondaryResource==1, and from PixelData for all other levels —
            // exactly the Sep27 physical layout.
            return XboxBitmapUtils.GetXboxBitmapLevelData(
                PixelData, MediumResData, SyntheticDef,
                mipIndex, layerIndex,
                isPaired: false, pairIndex: 0, otherDefinition: null);
        }
    }
}
