using System.IO;
using System.Text;

namespace MdlLib;

// Based on Source Engine studiohdr_t structure
public class MdlHeader
{
	public const int IDST_HEADER = 0x54534449; // "IDST"
	public const int VERSION_44 = 44; // HL2
	public const int VERSION_48 = 48; // TF2, CSS, etc
	public const int VERSION_49 = 49; // L4D, L4D2, Portal 2, CSGO

	public int Id { get; set; }
	public int Version { get; set; }
	public int Checksum { get; set; }
	public string Name { get; set; }
	public int FileSize { get; set; }

	// Eye position
	public float EyePositionX { get; set; }
	public float EyePositionY { get; set; }
	public float EyePositionZ { get; set; }

	// Illumination position
	public float IllumPositionX { get; set; }
	public float IllumPositionY { get; set; }
	public float IllumPositionZ { get; set; }

	// Hull bounding box
	public float HullMinX { get; set; }
	public float HullMinY { get; set; }
	public float HullMinZ { get; set; }
	public float HullMaxX { get; set; }
	public float HullMaxY { get; set; }
	public float HullMaxZ { get; set; }

	// View bounding box
	public float ViewBBMinX { get; set; }
	public float ViewBBMinY { get; set; }
	public float ViewBBMinZ { get; set; }
	public float ViewBBMaxX { get; set; }
	public float ViewBBMaxY { get; set; }
	public float ViewBBMaxZ { get; set; }

	public int Flags { get; set; }

	// Bones
	public int BoneCount { get; set; }
	public int BoneOffset { get; set; }

	// Bone controllers
	public int BoneControllerCount { get; set; }
	public int BoneControllerOffset { get; set; }

	// Hitboxes
	public int HitboxSetCount { get; set; }
	public int HitboxSetOffset { get; set; }

	// Animations
	public int LocalAnimationCount { get; set; }
	public int LocalAnimationOffset { get; set; }

	// Sequences
	public int LocalSequenceCount { get; set; }
	public int LocalSequenceOffset { get; set; }
	
	// Activity/events (v44+)
	public int ActivityListVersion { get; set; }
	public int EventsIndexed { get; set; }

	// Textures
	public int TextureCount { get; set; }
	public int TextureOffset { get; set; }

	// Texture paths
	public int TexturePathCount { get; set; }
	public int TexturePathOffset { get; set; }

	// Skin families
	public int SkinReferenceCount { get; set; }
	public int SkinFamilyCount { get; set; }
	public int SkinFamilyOffset { get; set; }

	// Body parts
	public int BodyPartCount { get; set; }
	public int BodyPartOffset { get; set; }

	// Attachments
	public int LocalAttachmentCount { get; set; }
	public int LocalAttachmentOffset { get; set; }

	// Nodes (v44+)
	public int LocalNodeCount { get; set; }
	public int LocalNodeOffset { get; set; }
	public int LocalNodeNameOffset { get; set; }

	// Flex
	public int FlexDescCount { get; set; }
	public int FlexDescOffset { get; set; }
	public int FlexControllerCount { get; set; }
	public int FlexControllerOffset { get; set; }
	public int FlexRuleCount { get; set; }
	public int FlexRuleOffset { get; set; }

	// IK
	public int IkChainCount { get; set; }
	public int IkChainOffset { get; set; }

	// Mouths
	public int MouthCount { get; set; }
	public int MouthOffset { get; set; }

	// Pose parameters
	public int LocalPoseParameterCount { get; set; }
	public int LocalPoseParameterOffset { get; set; }

	// Surface properties
	public int SurfacePropOffset { get; set; }

	// Key values
	public int KeyValueOffset { get; set; }
	public int KeyValueSize { get; set; }

	// IK locks
	public int LocalIkAutoPlayLockCount { get; set; }
	public int LocalIkAutoPlayLockOffset { get; set; }

	// Physics
	public float Mass { get; set; }
	public int Contents { get; set; }

	// Include models
	public int IncludeModelCount { get; set; }
	public int IncludeModelOffset { get; set; }

	// Virtual model
	public int VirtualModelPtr { get; set; }

	// Animation blocks
	public int AnimBlockNameOffset { get; set; }
	public int AnimBlockCount { get; set; }
	public int AnimBlockOffset { get; set; }
	public int AnimBlockModelPtr { get; set; }

	// Bone table
	public int BoneTableByNameOffset { get; set; }

	// Vertex base (for VVD file)
	public int VertexBasePtr { get; set; }
	public int IndexBasePtr { get; set; }

	// Lighting
	public byte DirectionalLightDot { get; set; }
	public byte RootLod { get; set; }
	
	// Version-specific fields (v48+)
	public byte AllowedRootLodCount { get; set; }
	public byte Unused { get; set; }
	public int Unused2 { get; set; }
	public int FlexControllerUiCount { get; set; }
	public int FlexControllerUiOffset { get; set; }
	public int StudioHeader2Offset { get; set; }

	public static MdlHeader Read(BinaryReader reader)
	{
		var header = new MdlHeader();

		header.Id = reader.ReadInt32();
		header.Version = reader.ReadInt32();
		header.Checksum = reader.ReadInt32();

		byte[] nameBytes = reader.ReadBytes(64);
		header.Name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

		header.FileSize = reader.ReadInt32();

		// Eye position
		header.EyePositionX = reader.ReadSingle();
		header.EyePositionY = reader.ReadSingle();
		header.EyePositionZ = reader.ReadSingle();

		// Illumination position
		header.IllumPositionX = reader.ReadSingle();
		header.IllumPositionY = reader.ReadSingle();
		header.IllumPositionZ = reader.ReadSingle();

		// Hull min/max
		header.HullMinX = reader.ReadSingle();
		header.HullMinY = reader.ReadSingle();
		header.HullMinZ = reader.ReadSingle();
		header.HullMaxX = reader.ReadSingle();
		header.HullMaxY = reader.ReadSingle();
		header.HullMaxZ = reader.ReadSingle();

		// View BB min/max
		header.ViewBBMinX = reader.ReadSingle();
		header.ViewBBMinY = reader.ReadSingle();
		header.ViewBBMinZ = reader.ReadSingle();
		header.ViewBBMaxX = reader.ReadSingle();
		header.ViewBBMaxY = reader.ReadSingle();
		header.ViewBBMaxZ = reader.ReadSingle();

		header.Flags = reader.ReadInt32();

		// Bones
		header.BoneCount = reader.ReadInt32();
		header.BoneOffset = reader.ReadInt32();

		// Bone controllers
		header.BoneControllerCount = reader.ReadInt32();
		header.BoneControllerOffset = reader.ReadInt32();

		// Hitboxes
		header.HitboxSetCount = reader.ReadInt32();
		header.HitboxSetOffset = reader.ReadInt32();

		// Animations
		header.LocalAnimationCount = reader.ReadInt32();
		header.LocalAnimationOffset = reader.ReadInt32();

		// Sequences
		header.LocalSequenceCount = reader.ReadInt32();
		header.LocalSequenceOffset = reader.ReadInt32();
		
		// Activity/events (v44+)
		header.ActivityListVersion = reader.ReadInt32();
		header.EventsIndexed = reader.ReadInt32();

		// Textures
		header.TextureCount = reader.ReadInt32();
		header.TextureOffset = reader.ReadInt32();

		// Texture paths
		header.TexturePathCount = reader.ReadInt32();
		header.TexturePathOffset = reader.ReadInt32();

		// Skin families
		header.SkinReferenceCount = reader.ReadInt32();
		header.SkinFamilyCount = reader.ReadInt32();
		header.SkinFamilyOffset = reader.ReadInt32();

		// Body parts
		header.BodyPartCount = reader.ReadInt32();
		header.BodyPartOffset = reader.ReadInt32();

		// Attachments
		header.LocalAttachmentCount = reader.ReadInt32();
		header.LocalAttachmentOffset = reader.ReadInt32();

		// Nodes (v44+)
		header.LocalNodeCount = reader.ReadInt32();
		header.LocalNodeOffset = reader.ReadInt32();
		header.LocalNodeNameOffset = reader.ReadInt32();

		// Flex
		header.FlexDescCount = reader.ReadInt32();
		header.FlexDescOffset = reader.ReadInt32();
		header.FlexControllerCount = reader.ReadInt32();
		header.FlexControllerOffset = reader.ReadInt32();
		header.FlexRuleCount = reader.ReadInt32();
		header.FlexRuleOffset = reader.ReadInt32();

		// IK
		header.IkChainCount = reader.ReadInt32();
		header.IkChainOffset = reader.ReadInt32();

		// Mouths
		header.MouthCount = reader.ReadInt32();
		header.MouthOffset = reader.ReadInt32();

		// Pose parameters
		header.LocalPoseParameterCount = reader.ReadInt32();
		header.LocalPoseParameterOffset = reader.ReadInt32();

		// Surface properties
		header.SurfacePropOffset = reader.ReadInt32();

		// Key values
		header.KeyValueOffset = reader.ReadInt32();
		header.KeyValueSize = reader.ReadInt32();

		// IK locks
		header.LocalIkAutoPlayLockCount = reader.ReadInt32();
		header.LocalIkAutoPlayLockOffset = reader.ReadInt32();

		// Physics
		header.Mass = reader.ReadSingle();
		header.Contents = reader.ReadInt32();
		
		// For v37 only, header ends with 9 unused integers
		if (header.Version < 44)
		{
			// Skip 9 unused integers (36 bytes)
			for (int i = 0; i < 9; i++)
			{
				reader.ReadInt32();
			}
			return header;
		}

		// For v44+: Continue reading additional fields
		
		// Include models (v44+)
		header.IncludeModelCount = reader.ReadInt32();
		header.IncludeModelOffset = reader.ReadInt32();

		// Virtual model
		header.VirtualModelPtr = reader.ReadInt32();

		// Animation blocks
		header.AnimBlockNameOffset = reader.ReadInt32();
		header.AnimBlockCount = reader.ReadInt32();
		header.AnimBlockOffset = reader.ReadInt32();
		header.AnimBlockModelPtr = reader.ReadInt32();

		// Bone table
		header.BoneTableByNameOffset = reader.ReadInt32();

		// Vertex/index base
		header.VertexBasePtr = reader.ReadInt32();
		header.IndexBasePtr = reader.ReadInt32();

		// Lighting
		header.DirectionalLightDot = reader.ReadByte();
		header.RootLod = reader.ReadByte();
		
		// Version-specific fields
		if (header.Version >= 48)
		{
			header.AllowedRootLodCount = reader.ReadByte();
			header.Unused = reader.ReadByte();
			header.Unused2 = reader.ReadInt32(); // unused4
			header.FlexControllerUiCount = reader.ReadInt32();
			header.FlexControllerUiOffset = reader.ReadInt32();
			reader.ReadInt32(); // unused3[0]
			reader.ReadInt32(); // unused3[1]
			header.StudioHeader2Offset = reader.ReadInt32();
			reader.ReadInt32(); // unused2
		}
		else // v44-47
		{
			reader.ReadByte(); // unused[0]
			reader.ReadByte(); // unused[1]
			reader.ReadInt32(); // zeroframecacheindex
			// unused2[6] = 24 bytes
			for (int i = 0; i < 6; i++)
			{
				reader.ReadInt32();
			}
			// Header ends here for v44-47
		}

		return header;
	}

	public bool IsValid()
	{
		return Id == IDST_HEADER && (Version >= VERSION_44 && Version <= VERSION_49);
	}
}

