using System.IO;
using System.Text;

namespace MdlLib;

// mstudiotexture_t - Texture/Material reference in MDL
public class MdlTexture
{
	public const int SIZE = 64; // bytes
	
	public int NameOffset { get; set; }
	public int Flags { get; set; }
	public int Used { get; set; }
	public int Unused1 { get; set; }
	public int MaterialP { get; set; }
	public int ClientMaterialP { get; set; }
	public int[] Unused { get; set; } = new int[10];
	
	public string Name { get; set; } // e.g. "citizen_sheet"
	
	public static MdlTexture Read(BinaryReader reader, long baseOffset)
	{
		var texture = new MdlTexture();
		long startPos = reader.BaseStream.Position;
		
		texture.NameOffset = reader.ReadInt32();
		texture.Flags = reader.ReadInt32();
		texture.Used = reader.ReadInt32();
		texture.Unused1 = reader.ReadInt32();
		texture.MaterialP = reader.ReadInt32();
		texture.ClientMaterialP = reader.ReadInt32();
		
		for (int i = 0; i < 10; i++)
		{
			texture.Unused[i] = reader.ReadInt32();
		}
		
		// Read texture name if offset is valid
		if (texture.NameOffset != 0)
		{
			long currentPos = reader.BaseStream.Position;
			reader.BaseStream.Seek(startPos + texture.NameOffset, SeekOrigin.Begin);
			
			var nameBytes = new System.Collections.Generic.List<byte>();
			byte b;
			while ((b = reader.ReadByte()) != 0)
			{
				nameBytes.Add(b);
			}
			texture.Name = Encoding.UTF8.GetString(nameBytes.ToArray());
			
			reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
		}
		
		return texture;
	}
}

