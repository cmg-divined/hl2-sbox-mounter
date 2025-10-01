using System;
using System.IO;
using System.Text;

namespace MdlLib;

// mstudioanimdesc_t - Animation descriptor for MDL version 44
public class MdlAnimDesc
{
	public const int SIZE = 100; // bytes for MDL v44 (v37 is 96)
	
	public int BaseHeaderOffset { get; set; } // v44+ only
	public string Name { get; set; }
	public float Fps { get; set; }
	public int Flags { get; set; }
	public int FrameCount { get; set; }
	
	// Piecewise movement
	public int MovementCount { get; set; }
	public int MovementOffset { get; set; }
	
	public int[] Unused1 { get; set; } = new int[6];
	
	// Animation data
	public int AnimBlock { get; set; }
	public int AnimOffset { get; set; }
	
	// IK rules
	public int IkRuleCount { get; set; }
	public int IkRuleOffset { get; set; }
	public int AnimBlockIkRuleOffset { get; set; }
	
	// Local hierarchy
	public int LocalHierarchyCount { get; set; }
	public int LocalHierarchyOffset { get; set; }
	
	// Sections (for fast lookup)
	public int SectionOffset { get; set; }
	public int SectionFrames { get; set; }
	
	// Zero frame data
	public short ZeroFrameSpan { get; set; }
	public short ZeroFrameCount { get; set; }
	public int ZeroFrameOffset { get; set; }
	public float ZeroFrameStallTime { get; set; } // v44+ only
	
	public static MdlAnimDesc Read(BinaryReader reader, long baseOffset)
	{
		var anim = new MdlAnimDesc();
		long startPos = reader.BaseStream.Position;
		
		// Read baseHeaderOffset first (v44+)
		anim.BaseHeaderOffset = reader.ReadInt32();
		
		// Read name offset
		int nameOffset = reader.ReadInt32();
		if (nameOffset > 0)
		{
			long currentPos = reader.BaseStream.Position;
			reader.BaseStream.Seek(startPos + nameOffset, SeekOrigin.Begin);
			
			var nameBytes = new System.Collections.Generic.List<byte>();
			byte b;
			while ((b = reader.ReadByte()) != 0)
			{
				nameBytes.Add(b);
			}
			anim.Name = Encoding.UTF8.GetString(nameBytes.ToArray());
			
			reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
		}
		
		anim.Fps = reader.ReadSingle();
		anim.Flags = reader.ReadInt32();
		anim.FrameCount = reader.ReadInt32();
		
		anim.MovementCount = reader.ReadInt32();
		anim.MovementOffset = reader.ReadInt32();
		
		// unused1[6] - 6 ints
		for (int i = 0; i < 6; i++)
		{
			reader.ReadInt32(); // Skip unused
		}
		
		anim.AnimBlock = reader.ReadInt32();
		anim.AnimOffset = reader.ReadInt32();
		
		anim.IkRuleCount = reader.ReadInt32();
		anim.IkRuleOffset = reader.ReadInt32();
		anim.AnimBlockIkRuleOffset = reader.ReadInt32();
		
		anim.LocalHierarchyCount = reader.ReadInt32();
		anim.LocalHierarchyOffset = reader.ReadInt32();
		
		anim.SectionOffset = reader.ReadInt32();
		anim.SectionFrames = reader.ReadInt32();
		
		anim.ZeroFrameSpan = reader.ReadInt16();
		anim.ZeroFrameCount = reader.ReadInt16();
		anim.ZeroFrameOffset = reader.ReadInt32();
		anim.ZeroFrameStallTime = reader.ReadSingle();
		
		// That's it for v44! Structure is 100 bytes total.
		
		return anim;
	}
}

// Animation flags
[Flags]
public enum StudioAnimFlags
{
	Looping = 0x0001,
	Snap = 0x0002,
	Delta = 0x0004,
	AutoPlay = 0x0008,
	Post = 0x0010,
	AllZeros = 0x0020,
	CycleposeBlend = 0x0040,
	Hidden = 0x0080,
	DontAnimateBones = 0x0100,
	FrameAnim = 0x0200
}

