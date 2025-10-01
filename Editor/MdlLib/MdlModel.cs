using System.IO;
using System.Text;

namespace MdlLib;

public class MdlModel
{
	public string Name { get; set; }
	public int Type { get; set; }
	public float BoundingRadius { get; set; }
	public int MeshCount { get; set; }
	public int MeshOffset { get; set; }
	public int VertexCount { get; set; }
	public int VertexOffset { get; set; }
	public int TangentOffset { get; set; }
	public int AttachmentCount { get; set; }
	public int AttachmentOffset { get; set; }
	public int EyeballCount { get; set; }
	public int EyeballOffset { get; set; }
	public int Unused1 { get; set; }
	public int Unused2 { get; set; }
	public int Unused3 { get; set; }
	public int Unused4 { get; set; }
	public int Unused5 { get; set; }
	public int Unused6 { get; set; }
	public int Unused7 { get; set; }
	public int Unused8 { get; set; }

	public static MdlModel Read(BinaryReader reader, long baseOffset, int nameOffset)
	{
		var model = new MdlModel();

		// Read name (64 bytes inline for older versions, or offset for newer)
		long currentPos = reader.BaseStream.Position;
		
		// In MDL format, model name is stored as 64-byte inline string
		byte[] nameBytes = reader.ReadBytes(64);
		model.Name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

		model.Type = reader.ReadInt32();
		model.BoundingRadius = reader.ReadSingle();
		model.MeshCount = reader.ReadInt32();
		model.MeshOffset = reader.ReadInt32();
		model.VertexCount = reader.ReadInt32();
		model.VertexOffset = reader.ReadInt32();
		model.TangentOffset = reader.ReadInt32();
		model.AttachmentCount = reader.ReadInt32();
		model.AttachmentOffset = reader.ReadInt32();
		model.EyeballCount = reader.ReadInt32();
		model.EyeballOffset = reader.ReadInt32();

		model.Unused1 = reader.ReadInt32();
		model.Unused2 = reader.ReadInt32();
		model.Unused3 = reader.ReadInt32();
		model.Unused4 = reader.ReadInt32();
		model.Unused5 = reader.ReadInt32();
		model.Unused6 = reader.ReadInt32();
		model.Unused7 = reader.ReadInt32();
		model.Unused8 = reader.ReadInt32();

		return model;
	}
}

