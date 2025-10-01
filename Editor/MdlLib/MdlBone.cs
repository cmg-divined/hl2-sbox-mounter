using System.IO;
using System.Text;
using Sandbox;

namespace MdlLib;

public class MdlBone
{
	public string Name { get; set; }
	public int ParentBoneIndex { get; set; }
	public int[] BoneControllerIndex { get; set; } = new int[6];

	// Position
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }

	// Quaternion
	public float QuatX { get; set; }
	public float QuatY { get; set; }
	public float QuatZ { get; set; }
	public float QuatW { get; set; }

	// Rotation (radians)
	public float RotX { get; set; }
	public float RotY { get; set; }
	public float RotZ { get; set; }

	// Position scale
	public float PosScaleX { get; set; }
	public float PosScaleY { get; set; }
	public float PosScaleZ { get; set; }

	// Rotation scale
	public float RotScaleX { get; set; }
	public float RotScaleY { get; set; }
	public float RotScaleZ { get; set; }

	// Pose to bone matrix (3x4)
	public float[] PoseToBone { get; set; } = new float[12];

	// Alignment quaternion
	public float AlignX { get; set; }
	public float AlignY { get; set; }
	public float AlignZ { get; set; }
	public float AlignW { get; set; }

	public int Flags { get; set; }
	public int ProceduralRuleType { get; set; }
	public int ProceduralRuleOffset { get; set; }
	public int PhysicsBoneIndex { get; set; }
	public int SurfacePropNameOffset { get; set; }
	public int Contents { get; set; }

	public int[] Unused { get; set; } = new int[8];

	// Helper methods for transforming vertices
	public Vector3 TransformPoint(Vector3 point)
	{
		// Apply pose-to-bone transformation
		// Matrix is stored as 3x4 (row-major)
		float x = PoseToBone[0] * point.x + PoseToBone[1] * point.y + PoseToBone[2] * point.z + PoseToBone[3];
		float y = PoseToBone[4] * point.x + PoseToBone[5] * point.y + PoseToBone[6] * point.z + PoseToBone[7];
		float z = PoseToBone[8] * point.x + PoseToBone[9] * point.y + PoseToBone[10] * point.z + PoseToBone[11];
		return new Vector3(x, y, z);
	}

	public Vector3 TransformDirection(Vector3 direction)
	{
		// Transform direction (ignore translation)
		float x = PoseToBone[0] * direction.x + PoseToBone[1] * direction.y + PoseToBone[2] * direction.z;
		float y = PoseToBone[4] * direction.x + PoseToBone[5] * direction.y + PoseToBone[6] * direction.z;
		float z = PoseToBone[8] * direction.x + PoseToBone[9] * direction.y + PoseToBone[10] * direction.z;
		return new Vector3(x, y, z);
	}

	public static MdlBone Read(BinaryReader reader, long boneOffset)
	{
		var bone = new MdlBone();

		// Read name offset (but read the name later)
		int nameOffset = reader.ReadInt32();

		bone.ParentBoneIndex = reader.ReadInt32();

		for (int i = 0; i < 6; i++)
		{
			bone.BoneControllerIndex[i] = reader.ReadInt32();
		}

		// Position
		bone.PosX = reader.ReadSingle();
		bone.PosY = reader.ReadSingle();
		bone.PosZ = reader.ReadSingle();

		// Quaternion (this is the rotation we use for s&box)
		bone.QuatX = reader.ReadSingle();
		bone.QuatY = reader.ReadSingle();
		bone.QuatZ = reader.ReadSingle();
		bone.QuatW = reader.ReadSingle();

		// Rotation (radians - euler angles)
		bone.RotX = reader.ReadSingle();
		bone.RotY = reader.ReadSingle();
		bone.RotZ = reader.ReadSingle();

		// Position scale
		bone.PosScaleX = reader.ReadSingle();
		bone.PosScaleY = reader.ReadSingle();
		bone.PosScaleZ = reader.ReadSingle();

		// Rotation scale
		bone.RotScaleX = reader.ReadSingle();
		bone.RotScaleY = reader.ReadSingle();
		bone.RotScaleZ = reader.ReadSingle();

		// Pose to bone matrix
		for (int i = 0; i < 12; i++)
		{
			bone.PoseToBone[i] = reader.ReadSingle();
		}

		// Alignment quaternion
		bone.AlignX = reader.ReadSingle();
		bone.AlignY = reader.ReadSingle();
		bone.AlignZ = reader.ReadSingle();
		bone.AlignW = reader.ReadSingle();

		bone.Flags = reader.ReadInt32();
		bone.ProceduralRuleType = reader.ReadInt32();
		bone.ProceduralRuleOffset = reader.ReadInt32();
		bone.PhysicsBoneIndex = reader.ReadInt32();
		bone.SurfacePropNameOffset = reader.ReadInt32();
		bone.Contents = reader.ReadInt32();

		for (int i = 0; i < 8; i++)
		{
			bone.Unused[i] = reader.ReadInt32();
		}

		// Now read the name
		if (nameOffset != 0)
		{
			long savedPos = reader.BaseStream.Position;
			long namePos = boneOffset + nameOffset;
			
			// Validate name position
			if (namePos >= 0 && namePos < reader.BaseStream.Length)
			{
				reader.BaseStream.Seek(namePos, SeekOrigin.Begin);
				bone.Name = ReadNullTerminatedString(reader);
				reader.BaseStream.Seek(savedPos, SeekOrigin.Begin);
			}
			else
			{
				bone.Name = $"Bone_{boneOffset}";
			}
		}
		else
		{
			bone.Name = "";
		}

		return bone;
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

