using System;
using System.IO;
using TagTool.Bitmaps.Export;

namespace TagTool.Bitmaps.Utils
{
    /// <summary>
    /// Writes DDS files from ExportBitmap DTOs.
    ///
    /// Ports donor blam-tags/src/bitmap/dds.rs:
    ///   write_dds()         → WriteLegacy()
    ///   write_dds_dx10()    → WriteDx10()
    ///   pixel_format()      → WritePixelFormat()
    ///   dxgi_format()       → DxgiFormat()
    ///   decode_dxn_mono_alpha() → DecodeDxnMonoAlpha() + UnpackBc4Block()
    ///
    /// DDS header fields are written little-endian (BinaryWriter guarantees LE on all platforms).
    /// </summary>
    public static class DdsWriter
    {
        // DDS magic
        private const uint DDS_MAGIC = 0x20534444; // "DDS "

        // dwFlags bits
        private const uint DDSD_CAPS        = 0x00000001;
        private const uint DDSD_HEIGHT      = 0x00000002;
        private const uint DDSD_WIDTH       = 0x00000004;
        private const uint DDSD_PITCH       = 0x00000008;
        private const uint DDSD_PIXELFORMAT = 0x00001000;
        private const uint DDSD_MIPMAPCOUNT = 0x00020000;
        private const uint DDSD_LINEARSIZE  = 0x00080000;

        // ddpfPixelFormat.dwFlags bits
        private const uint DDPF_ALPHAPIXELS = 0x00000001;
        private const uint DDPF_ALPHA       = 0x00000002;
        private const uint DDPF_FOURCC      = 0x00000004;
        private const uint DDPF_RGB         = 0x00000040;
        private const uint DDPF_LUMINANCE   = 0x00020000;
        private const uint DDPF_BUMPDUDV    = 0x00080000;

        // dwCaps bits
        private const uint DDSCAPS_COMPLEX = 0x00000008;
        private const uint DDSCAPS_MIPMAP  = 0x00400000;
        private const uint DDSCAPS_TEXTURE = 0x00001000;

        // dwCaps2 bits
        private const uint DDSCAPS2_CUBEMAP           = 0x00000200;
        private const uint DDSCAPS2_CUBEMAP_ALL_FACES = 0x0000FC00;

        // D3D10 resource dimension for 2-D textures
        private const uint D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3;

        // DX10 marker FourCC ("DX10" in little-endian)
        private const uint FOURCC_DX10 = 0x30305844;

        // -----------------------------------------------------------------------
        // Public size helpers — used by BitmapExportAdapter to build mip offsets
        // -----------------------------------------------------------------------

        /// <summary>
        /// Bytes per 4×4 block for BC-family formats; 0 for uncompressed.
        /// Matches TagTool BitmapFormatUtils.GetBlockSize() and donor format.rs block_bytes().
        /// </summary>
        public static int BlockBytes(BitmapFormat format)
        {
            switch (format)
            {
                // 8-byte blocks (BC1 / BC4 equivalents)
                case BitmapFormat.Dxt1:
                case BitmapFormat.Dxt3a:
                case BitmapFormat.Dxt3aMono:
                case BitmapFormat.Dxt3aAlpha:
                case BitmapFormat.Dxt3A1111:
                case BitmapFormat.Dxt5a:
                case BitmapFormat.Dxt5aMono:
                case BitmapFormat.Dxt5aAlpha:
                case BitmapFormat.Ctx1:
                    return 8;

                // 16-byte blocks (BC2 / BC3 / BC5 equivalents)
                case BitmapFormat.Dxt3:
                case BitmapFormat.Dxt5:
                case BitmapFormat.Dxt5nm:
                case BitmapFormat.Dxn:
                case BitmapFormat.DxnMonoAlpha:
                    return 16;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Total bytes for one mip level.
        /// Mirrors donor format.rs level_bytes(): compressed rounds up to 4×4 grid (min 1 block).
        /// </summary>
        public static long LevelBytes(BitmapFormat format, int width, int height)
        {
            int blockBytes = BlockBytes(format);
            if (blockBytes > 0)
            {
                long bw = Math.Max(1, (width  + 3) / 4);
                long bh = Math.Max(1, (height + 3) / 4);
                return bw * bh * blockBytes;
            }
            int bpp = BitmapFormatUtils.GetBitsPerPixel(format);
            return ((long)width * height * bpp + 7) / 8;
        }

        /// <summary>
        /// Total bytes for a full mip chain of <paramref name="mipCount"/> levels.
        /// Mirrors donor format.rs surface_bytes().
        /// </summary>
        public static long SurfaceBytes(BitmapFormat format, int width, int height, int mipCount)
        {
            long total = 0;
            for (int i = 0; i < mipCount; i++)
                total += LevelBytes(format, Math.Max(1, width >> i), Math.Max(1, height >> i));
            return total;
        }

        /// <summary>
        /// True when a DX10 extension header is required.
        /// Arrays always need DX10; SignedR16G16B16A16 has no legacy D3D9 FourCC.
        /// Mirrors donor dds.rs needs_dx10 logic.
        /// </summary>
        public static bool RequiresDx10(BitmapFormat format, bool isArray)
            => isArray || format == BitmapFormat.SignedR16G16B16A16;

        // -----------------------------------------------------------------------
        // Public write entry point
        // -----------------------------------------------------------------------

        /// <summary>
        /// Writes a complete .dds file (magic + header + pixel data) to <paramref name="output"/>.
        /// Dispatches to the correct path based on format and texture type.
        /// </summary>
        public static void Write(ExportBitmap eb, Stream output)
        {
            if (eb == null)   throw new ArgumentNullException(nameof(eb));
            if (output == null) throw new ArgumentNullException(nameof(output));

            bool isCube  = eb.IsCubeMap;
            bool isArray = eb.IsArray && !isCube; // cube arrays unsupported

            // DxnMonoAlpha is decoded to A8R8G8B8 before writing.
            if (eb.Format == BitmapFormat.DxnMonoAlpha)
            {
                WriteDecodedDxnMonoAlpha(eb, output);
                return;
            }

            if (RequiresDx10(eb.Format, isArray))
            {
                if (isCube)
                    throw new NotSupportedException(
                        $"Cube-map arrays with format {eb.Format} require DX10 cube+array support, which is not implemented.");
                WriteDx10(eb, output);
            }
            else
            {
                WriteLegacy(eb, output);
            }
        }

        // -----------------------------------------------------------------------
        // Private: header writers
        // -----------------------------------------------------------------------

        private static void WriteLegacy(ExportBitmap eb, Stream output)
        {
            using var bw = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

            bool isCube  = eb.IsCubeMap;
            int  mips    = eb.MipCount;

            uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
            if (mips > 1) flags |= DDSD_MIPMAPCOUNT;
            flags |= BitmapUtils.IsCompressedFormat(eb.Format) ? DDSD_LINEARSIZE : DDSD_PITCH;

            uint pitchOrLinear = PitchOrLinearSize(eb.Format, eb.Width, eb.Height);

            uint caps  = DDSCAPS_TEXTURE;
            if (mips > 1) caps |= DDSCAPS_MIPMAP | DDSCAPS_COMPLEX;
            if (isCube)   caps |= DDSCAPS_COMPLEX;

            uint caps2 = isCube ? (DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_ALL_FACES) : 0u;

            // 4-byte magic
            bw.Write(DDS_MAGIC);

            // DDS_HEADER (124 bytes: size field + 30 uint fields)
            bw.Write(124u);                 // dwSize
            bw.Write(flags);               // dwFlags
            bw.Write((uint)eb.Height);
            bw.Write((uint)eb.Width);
            bw.Write(pitchOrLinear);
            bw.Write(0u);                  // dwDepth (not used for 2D/cube)
            bw.Write((uint)mips);          // dwMipMapCount
            for (int i = 0; i < 11; i++) bw.Write(0u); // dwReserved1[11]

            WritePixelFormat(bw, eb.Format);

            bw.Write(caps);
            bw.Write(caps2);
            bw.Write(0u); // dwCaps3
            bw.Write(0u); // dwCaps4
            bw.Write(0u); // dwReserved2

            WritePixelData(bw, eb);
        }

        private static void WriteDx10(ExportBitmap eb, Stream output)
        {
            using var bw = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

            int mips   = eb.MipCount;
            int layers = eb.LayerCount;

            uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
            if (mips > 1) flags |= DDSD_MIPMAPCOUNT;
            flags |= BitmapUtils.IsCompressedFormat(eb.Format) ? DDSD_LINEARSIZE : DDSD_PITCH;

            uint pitchOrLinear = PitchOrLinearSize(eb.Format, eb.Width, eb.Height);

            uint caps = DDSCAPS_TEXTURE;
            if (mips > 1) caps |= DDSCAPS_MIPMAP | DDSCAPS_COMPLEX;

            bw.Write(DDS_MAGIC);

            // DDS_HEADER (124 bytes)
            bw.Write(124u);
            bw.Write(flags);
            bw.Write((uint)eb.Height);
            bw.Write((uint)eb.Width);
            bw.Write(pitchOrLinear);
            bw.Write(0u); // dwDepth
            bw.Write((uint)mips);
            for (int i = 0; i < 11; i++) bw.Write(0u);

            // DDS_PIXELFORMAT with "DX10" marker (32 bytes)
            bw.Write(32u);          // dwSize
            bw.Write(DDPF_FOURCC); // dwFlags
            bw.Write(FOURCC_DX10); // dwFourCC = "DX10"
            bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u); // masks

            bw.Write(caps);
            bw.Write(0u); // dwCaps2 (no cube bits in DX10 path)
            bw.Write(0u); bw.Write(0u); bw.Write(0u);

            // DDS_HEADER_DXT10 (20 bytes)
            bw.Write(DxgiFormat(eb.Format));              // dxgiFormat
            bw.Write(D3D10_RESOURCE_DIMENSION_TEXTURE2D); // resourceDimension
            bw.Write(0u);                                 // miscFlag
            bw.Write((uint)layers);                       // arraySize
            bw.Write(0u);                                 // miscFlags2 (alpha mode unknown)

            WritePixelData(bw, eb);
        }

        // -----------------------------------------------------------------------
        // Private: DxnMonoAlpha → A8R8G8B8 decode path
        // -----------------------------------------------------------------------

        private static void WriteDecodedDxnMonoAlpha(ExportBitmap eb, Stream output)
        {
            // Decode all layers/mips from DxnMonoAlpha to A8R8G8B8, then recurse
            // through Write() so arrays still get the DX10 header.
            // Mirrors donor dds.rs DxnMonoAlpha dispatch in write_dds().
            byte[] decoded = DecodeDxnMonoAlpha(eb);

            var synthetic = new ExportBitmap
            {
                TagPath    = eb.TagPath,
                ImageIndex = eb.ImageIndex,
                Width      = eb.Width,
                Height     = eb.Height,
                Depth      = eb.Depth,
                MipCount   = eb.MipCount,
                LayerCount = eb.LayerCount,
                IsCubeMap  = eb.IsCubeMap,
                IsArray    = eb.IsArray,
                Format     = BitmapFormat.A8R8G8B8,
            };

            int offset = 0;
            int mipCount = eb.MipCount;
            foreach (var origLayer in eb.Layers)
            {
                var layer = new ExportBitmapLayer { PixelData = decoded };
                for (int m = 0; m < mipCount; m++)
                {
                    int mw  = Math.Max(1, eb.Width  >> m);
                    int mh  = Math.Max(1, eb.Height >> m);
                    int len = mw * mh * 4; // A8R8G8B8: 4 bytes/pixel
                    int mipDepth = origLayer.Mips.Count > m ? origLayer.Mips[m].Depth : 1;
                    layer.Mips.Add(new ExportBitmapMip
                    {
                        Width      = mw,
                        Height     = mh,
                        Depth      = mipDepth,
                        DataOffset = offset,
                        DataLength = len,
                    });
                    offset += len;
                }
                synthetic.Layers.Add(layer);
            }

            Write(synthetic, output); // recurse; A8R8G8B8 won't re-enter DxnMonoAlpha branch
        }

        // -----------------------------------------------------------------------
        // Private: pixel data flush
        // -----------------------------------------------------------------------

        private static void WritePixelData(BinaryWriter bw, ExportBitmap eb)
        {
            foreach (var layer in eb.Layers)
            {
                if (layer?.PixelData == null) continue;
                foreach (var mip in layer.Mips)
                {
                    int safeLen = Math.Min(mip.DataLength,
                        layer.PixelData.Length - mip.DataOffset);
                    if (safeLen > 0)
                        bw.Write(layer.PixelData, mip.DataOffset, safeLen);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Private: DDS_PIXELFORMAT block (32 bytes)
        // -----------------------------------------------------------------------

        private static void WritePixelFormat(BinaryWriter bw, BitmapFormat format)
        {
            bw.Write(32u); // dwSize always 32

            switch (format)
            {
                // BC / FourCC compressed formats
                // Mirrors donor dds.rs pixel_format().
                case BitmapFormat.Dxt1:
                    WriteFourCC(bw, 0x31545844u); break; // "DXT1"
                case BitmapFormat.Dxt3:
                    WriteFourCC(bw, 0x33545844u); break; // "DXT3"
                case BitmapFormat.Dxt5:
                case BitmapFormat.Dxt5nm:
                    WriteFourCC(bw, 0x35545844u); break; // "DXT5"

                // BC4-shaped 8-byte blocks → ATI1
                case BitmapFormat.Dxt5a:
                case BitmapFormat.Dxt5aMono:
                case BitmapFormat.Dxt5aAlpha:
                case BitmapFormat.Dxt3a:
                case BitmapFormat.Dxt3aMono:
                case BitmapFormat.Dxt3aAlpha:
                case BitmapFormat.Dxt3A1111:
                    WriteFourCC(bw, 0x31495441u); break; // "ATI1"

                case BitmapFormat.Dxn:
                    WriteFourCC(bw, 0x32495441u); break; // "ATI2"

                case BitmapFormat.Ctx1:
                    // Halo-specific BC1 variant; no universally recognised FourCC.
                    // Writing DXT1 gives correct block structure for size calculations.
                    WriteFourCC(bw, 0x31545844u); break;

                // D3D9 enum passthrough (numeric D3DFMT values used as FourCC).
                // Mirrors donor dds.rs pixel_format() D3DFMT cases.
                case BitmapFormat.Q8W8V8U8:
                    WriteFourCC(bw, 63u); break;   // D3DFMT_Q8W8V8U8
                case BitmapFormat.Abgrfp16:
                    WriteFourCC(bw, 113u); break;  // D3DFMT_A16B16G16R16F
                case BitmapFormat.Abgrfp32:
                    WriteFourCC(bw, 116u); break;  // D3DFMT_A32B32G32R32F
                case BitmapFormat.A16B16G16R16:
                    WriteFourCC(bw, 36u); break;   // D3DFMT_A16B16G16R16

                // Uncompressed with explicit channel bit-masks.
                // Mirrors donor dds.rs pixel_format() uncompressed arms.
                case BitmapFormat.A8:
                    WriteUncompressed(bw, DDPF_ALPHA, 8, 0, 0, 0, 0xFFu); break;
                case BitmapFormat.Y8:
                case BitmapFormat.R8:
                    WriteUncompressed(bw, DDPF_LUMINANCE, 8, 0xFFu, 0, 0, 0); break;
                case BitmapFormat.AY8:
                    WriteUncompressed(bw, DDPF_ALPHA, 8, 0, 0, 0, 0xFFu); break;
                case BitmapFormat.A8Y8:
                    WriteUncompressed(bw, DDPF_LUMINANCE | DDPF_ALPHAPIXELS, 16,
                        0x00FFu, 0, 0, 0xFF00u); break;
                case BitmapFormat.A4R4G4B4:
                case BitmapFormat.A4R4G4B4Font:
                    WriteUncompressed(bw, DDPF_RGB | DDPF_ALPHAPIXELS, 16,
                        0x0F00u, 0x00F0u, 0x000Fu, 0xF000u); break;
                case BitmapFormat.X8R8G8B8:
                    WriteUncompressed(bw, DDPF_RGB, 32,
                        0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0); break;
                case BitmapFormat.A8R8G8B8:
                    WriteUncompressed(bw, DDPF_RGB | DDPF_ALPHAPIXELS, 32,
                        0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0xFF000000u); break;
                case BitmapFormat.V8U8:
                    WriteUncompressed(bw, DDPF_BUMPDUDV, 16, 0xFF00u, 0x00FFu, 0, 0); break;
                case BitmapFormat.R5G6B5:
                    WriteUncompressed(bw, DDPF_RGB, 16, 0xF800u, 0x07E0u, 0x001Fu, 0); break;
                case BitmapFormat.A1R5G5B5:
                    WriteUncompressed(bw, DDPF_RGB | DDPF_ALPHAPIXELS, 16,
                        0x7C00u, 0x03E0u, 0x001Fu, 0x8000u); break;
                case BitmapFormat.A2R10G10B10:
                    WriteUncompressed(bw, DDPF_RGB | DDPF_ALPHAPIXELS, 32,
                        0x000003FFu, 0x000FFC00u, 0x3FF00000u, 0xC0000000u); break;
                case BitmapFormat.G8B8:
                    WriteUncompressed(bw, DDPF_RGB, 16, 0xFF00u, 0x00FFu, 0, 0); break;

                default:
                    throw new NotSupportedException(
                        $"BitmapFormat.{format} has no legacy DDS pixel-format mapping. " +
                        "Use a DX10 path or decode to a supported format first.");
            }
        }

        private static void WriteFourCC(BinaryWriter bw, uint fourCC)
        {
            bw.Write(DDPF_FOURCC); // dwFlags
            bw.Write(fourCC);      // dwFourCC
            bw.Write(0u);          // dwRGBBitCount
            bw.Write(0u);          // dwRBitMask
            bw.Write(0u);          // dwGBitMask
            bw.Write(0u);          // dwBBitMask
            bw.Write(0u);          // dwABitMask
        }

        private static void WriteUncompressed(BinaryWriter bw, uint flags, int rgbBitCount,
            uint rMask, uint gMask, uint bMask, uint aMask)
        {
            bw.Write(flags);
            bw.Write(0u);               // dwFourCC = 0 for non-FourCC
            bw.Write((uint)rgbBitCount);
            bw.Write(rMask);
            bw.Write(gMask);
            bw.Write(bMask);
            bw.Write(aMask);
        }

        // -----------------------------------------------------------------------
        // Private: PitchOrLinearSize
        // -----------------------------------------------------------------------

        private static uint PitchOrLinearSize(BitmapFormat format, int width, int height)
        {
            if (BitmapUtils.IsCompressedFormat(format))
                return (uint)LevelBytes(format, width, height);
            int bpp = BitmapFormatUtils.GetBitsPerPixel(format);
            return (uint)((width * bpp + 7) / 8);
        }

        // -----------------------------------------------------------------------
        // Private: DXGI format mapping (for DX10 extension header)
        // -----------------------------------------------------------------------

        private static uint DxgiFormat(BitmapFormat format)
        {
            // Values from DXGI_FORMAT enum. Mirrors donor dds.rs dxgi_format().
            switch (format)
            {
                case BitmapFormat.A8:               return 65;  // DXGI_FORMAT_A8_UNORM
                case BitmapFormat.Y8:
                case BitmapFormat.R8:
                case BitmapFormat.AY8:              return 61;  // DXGI_FORMAT_R8_UNORM
                case BitmapFormat.A8Y8:             return 49;  // DXGI_FORMAT_R8G8_UNORM
                case BitmapFormat.A4R4G4B4:
                case BitmapFormat.A4R4G4B4Font:     return 115; // DXGI_FORMAT_B4G4R4A4_UNORM
                case BitmapFormat.X8R8G8B8:         return 88;  // DXGI_FORMAT_B8G8R8X8_UNORM
                case BitmapFormat.A8R8G8B8:         return 87;  // DXGI_FORMAT_B8G8R8A8_UNORM
                case BitmapFormat.Dxt1:             return 71;  // DXGI_FORMAT_BC1_UNORM
                case BitmapFormat.Dxt3:             return 74;  // DXGI_FORMAT_BC2_UNORM
                case BitmapFormat.Dxt5:
                case BitmapFormat.Dxt5nm:           return 77;  // DXGI_FORMAT_BC3_UNORM
                case BitmapFormat.Dxt5a:            return 80;  // DXGI_FORMAT_BC4_UNORM
                case BitmapFormat.Dxn:              return 83;  // DXGI_FORMAT_BC5_UNORM
                case BitmapFormat.V8U8:             return 51;  // DXGI_FORMAT_R8G8_SNORM
                case BitmapFormat.Q8W8V8U8:         return 32;  // DXGI_FORMAT_R8G8B8A8_SNORM
                case BitmapFormat.Abgrfp16:         return 10;  // DXGI_FORMAT_R16G16B16A16_FLOAT
                case BitmapFormat.Abgrfp32:         return 2;   // DXGI_FORMAT_R32G32B32A32_FLOAT
                case BitmapFormat.A16B16G16R16:     return 11;  // DXGI_FORMAT_R16G16B16A16_UNORM
                case BitmapFormat.SignedR16G16B16A16: return 13; // DXGI_FORMAT_R16G16B16A16_SNORM
                case BitmapFormat.R5G6B5:           return 85;  // DXGI_FORMAT_B5G6R5_UNORM
                case BitmapFormat.A1R5G5B5:         return 86;  // DXGI_FORMAT_B5G5R5A1_UNORM
                case BitmapFormat.A2R10G10B10:      return 24;  // DXGI_FORMAT_R10G10B10A2_UNORM
                default:
                    throw new NotSupportedException(
                        $"BitmapFormat.{format} has no DXGI format mapping for the DX10 header.");
            }
        }

        // -----------------------------------------------------------------------
        // DxnMonoAlpha decoder
        // -----------------------------------------------------------------------

        /// <summary>
        /// Decodes DxnMonoAlpha (Halo BC5 variant) to A8R8G8B8 across all layers and mips.
        ///
        /// Format: 16-byte blocks, two BC4 sub-blocks.
        ///   Bytes 0–7:  red  (luminance) sub-block
        ///   Bytes 8–15: green (alpha)    sub-block
        ///
        /// Output byte layout per pixel (A8R8G8B8 LE): [B=red, G=red, R=red, A=green].
        /// Matches TagTool's DecompressDXNMonoAlpha convention.
        ///
        /// Ports donor blam-tags/src/bitmap/dds.rs decode_dxn_mono_alpha().
        /// </summary>
        private static byte[] DecodeDxnMonoAlpha(ExportBitmap eb)
        {
            // Pre-calculate total output size.
            long outBytes = 0;
            foreach (var layer in eb.Layers)
                foreach (var mip in layer.Mips)
                    outBytes += (long)mip.Width * mip.Height * 4;

            byte[] output = new byte[outBytes];
            int outOffset = 0;

            foreach (var layer in eb.Layers)
            {
                if (layer?.PixelData == null) continue;
                byte[] input = layer.PixelData;

                foreach (var mip in layer.Mips)
                {
                    int w       = mip.Width;
                    int h       = mip.Height;
                    int blocksW = Math.Max(1, (w + 3) / 4);
                    int blocksH = Math.Max(1, (h + 3) / 4);
                    int expected = blocksW * blocksH * 16;

                    if (mip.DataLength != expected)
                        throw new InvalidDataException(
                            $"DxnMonoAlpha block size mismatch at {w}×{h}: " +
                            $"expected {expected} bytes, got {mip.DataLength}.");

                    for (int by = 0; by < blocksH; by++)
                    {
                        for (int bx = 0; bx < blocksW; bx++)
                        {
                            int blockOff = mip.DataOffset + (by * blocksW + bx) * 16;

                            byte[] redVals   = new byte[8];
                            ulong  redIdx    = UnpackBc4Block(input, blockOff,     redVals);
                            byte[] greenVals = new byte[8];
                            ulong  greenIdx  = UnpackBc4Block(input, blockOff + 8, greenVals);

                            for (int j = 0; j < 4; j++)
                            {
                                for (int i = 0; i < 4; i++)
                                {
                                    int px = bx * 4 + i;
                                    int py = by * 4 + j;
                                    if (px >= w || py >= h) continue;

                                    int  bitOff   = 3 * (j * 4 + i);
                                    byte r        = redVals[  (redIdx   >> bitOff) & 0x07];
                                    byte g        = greenVals[(greenIdx >> bitOff) & 0x07];
                                    int  pixelOff = outOffset + (py * w + px) * 4;

                                    // A8R8G8B8 little-endian layout: [B, G, R, A]
                                    output[pixelOff + 0] = r; // B = luminance
                                    output[pixelOff + 1] = r; // G = luminance
                                    output[pixelOff + 2] = r; // R = luminance
                                    output[pixelOff + 3] = g; // A = alpha channel
                                }
                            }
                        }
                    }

                    outOffset += w * h * 4;
                }
            }

            return output;
        }

        /// <summary>
        /// Decodes one BC4 alpha block into an 8-entry palette and returns the 48-bit index field.
        /// Ports donor blam-tags/src/bitmap/dds.rs unpack_bc4_alpha_block().
        /// </summary>
        private static ulong UnpackBc4Block(byte[] data, int offset, byte[] values)
        {
            uint v0 = data[offset];
            uint v1 = data[offset + 1];
            values[0] = (byte)v0;
            values[1] = (byte)v1;

            if (v0 > v1)
            {
                // Mode 1: 2 endpoints + 6 linearly interpolated midpoints
                for (uint k = 0; k < 6; k++)
                    values[2 + k] = (byte)(((6 - k) * v0 + (1 + k) * v1 + 3) / 7);
            }
            else
            {
                // Mode 2: 2 endpoints + 4 midpoints + black (0) + white (255)
                for (uint k = 0; k < 4; k++)
                    values[2 + k] = (byte)(((4 - k) * v0 + (1 + k) * v1 + 2) / 5);
                values[6] = 0;
                values[7] = 255;
            }

            // 6 bytes of 3-bit indices packed into a u64 (48-bit field)
            return (ulong)data[offset + 2]
                | ((ulong)data[offset + 3] << 8)
                | ((ulong)data[offset + 4] << 16)
                | ((ulong)data[offset + 5] << 24)
                | ((ulong)data[offset + 6] << 32)
                | ((ulong)data[offset + 7] << 40);
        }
    }
}
