using System;
using System.IO;
using System.Text;

namespace MdlLib;

// mstudiomodelgroup_t - Include model reference
public class MdlIncludeModel
{
	public const int SIZE = 8; // 2 ints
	
	public string Label { get; set; }
	public string FileName { get; set; }
	
	public static MdlIncludeModel Read(BinaryReader reader, long baseOffset)
	{
		var include = new MdlIncludeModel();
		long startPos = reader.BaseStream.Position;
		
		int labelOffset = reader.ReadInt32();
		int fileNameOffset = reader.ReadInt32();
		
		// Read label
		if (labelOffset > 0)
		{
			long currentPos = reader.BaseStream.Position;
			reader.BaseStream.Seek(startPos + labelOffset, SeekOrigin.Begin);
			
			var bytes = new System.Collections.Generic.List<byte>();
			byte b;
			while ((b = reader.ReadByte()) != 0)
			{
				bytes.Add(b);
			}
			include.Label = Encoding.UTF8.GetString(bytes.ToArray());
			
			reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
		}
		
		// Read filename
		if (fileNameOffset > 0)
		{
			long currentPos = reader.BaseStream.Position;
			reader.BaseStream.Seek(startPos + fileNameOffset, SeekOrigin.Begin);
			
			var bytes = new System.Collections.Generic.List<byte>();
			byte b;
			while ((b = reader.ReadByte()) != 0)
			{
				bytes.Add(b);
			}
			include.FileName = Encoding.UTF8.GetString(bytes.ToArray());
			
			reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
		}
		
		return include;
	}
}

