using System.IO;

namespace MdlLib;

// mstudioanimblock_t - Animation block for external .ani files
public class MdlAnimBlock
{
	public const int SIZE = 8; // bytes
	
	public int DataStart { get; set; } // Offset in .ani file where this block starts
	public int DataEnd { get; set; }   // Offset in .ani file where this block ends
	
	public static MdlAnimBlock Read(BinaryReader reader)
	{
		return new MdlAnimBlock
		{
			DataStart = reader.ReadInt32(),
			DataEnd = reader.ReadInt32()
		};
	}
}

