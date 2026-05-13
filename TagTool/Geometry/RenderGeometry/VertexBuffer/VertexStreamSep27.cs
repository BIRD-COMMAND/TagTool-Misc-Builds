using System.IO;

namespace TagTool.Geometry
{
    public class VertexStreamSep27 : VertexStreamReach
    {
        public VertexStreamSep27(Stream stream) : base(stream) { }
        // Override specific vertex read methods here as Sep27 format differences are confirmed
    }
}
