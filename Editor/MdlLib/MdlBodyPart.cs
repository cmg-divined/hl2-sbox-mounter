using System.IO;
using System.Text;

namespace MdlLib;

public class MdlBodyPart
{
	public string Name { get; set; }
	public int ModelCount { get; set; }
	public int Base { get; set; }
	public int ModelOffset { get; set; }

	public static MdlBodyPart Read(BinaryReader reader, long baseOffset, int nameOffset)
	{
		var bodyPart = new MdlBodyPart();

		// Read name
		long currentPos = reader.BaseStream.Position;
		reader.BaseStream.Seek(baseOffset + nameOffset, SeekOrigin.Begin);
		bodyPart.Name = ReadNullTerminatedString(reader);
		reader.BaseStream.Seek(currentPos + 4, SeekOrigin.Begin);

		bodyPart.ModelCount = reader.ReadInt32();
		bodyPart.Base = reader.ReadInt32();
		bodyPart.ModelOffset = reader.ReadInt32();

		return bodyPart;
	}

	private static string ReadNullTerminatedString(BinaryReader reader)
	{
		var bytes = new System.Collections.Generic.List<byte>();
		byte b;
		while ((b = reader.ReadByte()) != 0)
		{
			bytes.Add(b);
		}
		return Encoding.UTF8.GetString(bytes.ToArray());
	}
}

