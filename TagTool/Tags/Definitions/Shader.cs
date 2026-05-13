using TagTool.Cache;
using TagTool.Common;
using static TagTool.Tags.TagFieldFlags;

namespace TagTool.Tags.Definitions
{
	[TagStructure(Name = "shader", Tag = "rmsh", Size = 0x4)]
    [TagStructure(Name = "shader", Tag = "rmsh", Size = 0x4, MinVersion = CacheVersion.Halo4280911, MaxVersion = CacheVersion.Halo4280911)]
    public class Shader : RenderMethod
    {
        [TagField(Flags = TagFieldFlags.GlobalMaterial)]
        public StringId Material;
    }
}
