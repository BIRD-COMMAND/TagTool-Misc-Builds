using System;
using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Tags;
using TagTool.Tags.Definitions.Gen4;

namespace TagTool.Tags.Resources.Gen4
{
    [TagStructure(Size = 0x38, Version = CacheVersion.Halo4280911)]
    [TagStructure(Size = 0x50)]
    public class RenderTextureInteropDefinition : TagStructure
    {
        public TagData PixelData;
        public TagData MediumResData;
        [TagField(MinVersion = CacheVersion.Halo4)]
        public TagData HighResData;
        public short Width;
        public short Height;
        public sbyte Depth;
        public sbyte TotalMipmapCount;
        public BitmapTypes Type;
        [TagField(Version = CacheVersion.Halo4280911)]
        public BooleanEnum IsMediumResBitmapHalo4280911;
        [TagField(MinVersion = CacheVersion.Halo4)]
        public sbyte Pad11;
        [TagField(MinVersion = CacheVersion.Halo4)]
        public BooleanEnum IsHighResBitmap;
        [TagField(MinVersion = CacheVersion.Halo4)]
        public BooleanEnum IsMediumResBitmap;
        [TagField(MinVersion = CacheVersion.Halo4)]
        public BooleanEnum Pad21;
        [TagField(MinVersion = CacheVersion.Halo4)]
        public BooleanEnum Pad22;
        public int ExponentBias;
        public int XenonD3dFormat;
        
        public enum BitmapTypes : sbyte
        {
            _2dTexture,
            _3dTexture,
            CubeMap,
            Array
        }
        
        public enum BooleanEnum : sbyte
        {
            False,
            True
        }
    }
}
