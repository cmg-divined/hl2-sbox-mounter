using System;
using System.IO;
using System.Text;

namespace MdlLib;

// mstudioseqdesc_t - Sequence descriptor for MDL version 44
// Sequences are the high-level animations (idle, walk, run) that reference raw animation data
public class MdlSequenceDesc
{
	public const int SIZE = 212; // bytes for version 44
	
	public int BaseHeaderOffset { get; set; }
	public string Label { get; set; } // Sequence name (e.g., "idle", "walk")
	public string ActivityName { get; set; }
	public int Flags { get; set; }
	public int Activity { get; set; }
	public int ActivityWeight { get; set; }
	
	public int NumEvents { get; set; }
	public int EventIndex { get; set; }
	
	public float BbMinX { get; set; }
	public float BbMinY { get; set; }
	public float BbMinZ { get; set; }
	public float BbMaxX { get; set; }
	public float BbMaxY { get; set; }
	public float BbMaxZ { get; set; }
	
	public int NumBlends { get; set; }
	public int AnimIndexIndex { get; set; } // Index to animation descriptor indices
	
	public int MovementIndex { get; set; }
	public int[] GroupSize { get; set; } = new int[2];
	public int[] ParamIndex { get; set; } = new int[2];
	public float[] ParamStart { get; set; } = new float[2];
	public float[] ParamEnd { get; set; } = new float[2];
	public int ParamParent { get; set; }
	
	public float FadeInTime { get; set; }
	public float FadeOutTime { get; set; }
	
	public int LocalEntryNode { get; set; }
	public int LocalExitNode { get; set; }
	public int NodeFlags { get; set; }
	
	public float EntryPhase { get; set; }
	public float ExitPhase { get; set; }
	public float LastFrame { get; set; }
	
	public int NextSeq { get; set; }
	public int Pose { get; set; }
	
	public int NumIkRules { get; set; }
	public int NumAutoLayers { get; set; }
	public int AutoLayerIndex { get; set; }
	
	public int WeightListIndex { get; set; }
	public int PoseKeyIndex { get; set; }
	
	public int NumIkLocks { get; set; }
	public int IkLockIndex { get; set; }
	
	public int KeyValueIndex { get; set; }
	public int KeyValueSize { get; set; }
	
	public int CyclePoseIndex { get; set; }
	public int[] Unused { get; set; } = new int[7];
	
	public static MdlSequenceDesc Read(BinaryReader reader, long baseOffset)
	{
		var seq = new MdlSequenceDesc();
		long startPos = reader.BaseStream.Position;
		
		seq.BaseHeaderOffset = reader.ReadInt32();
		
		// Read label (name)
		int labelOffset = reader.ReadInt32();
		
		// Read activity name offset
		int activityNameOffset = reader.ReadInt32();
		
		seq.Flags = reader.ReadInt32();
		seq.Activity = reader.ReadInt32();
		seq.ActivityWeight = reader.ReadInt32();
		
		seq.NumEvents = reader.ReadInt32();
		seq.EventIndex = reader.ReadInt32();
		
		// Bounding box
		seq.BbMinX = reader.ReadSingle();
		seq.BbMinY = reader.ReadSingle();
		seq.BbMinZ = reader.ReadSingle();
		seq.BbMaxX = reader.ReadSingle();
		seq.BbMaxY = reader.ReadSingle();
		seq.BbMaxZ = reader.ReadSingle();
		
		seq.NumBlends = reader.ReadInt32();
		seq.AnimIndexIndex = reader.ReadInt32();
		
		seq.MovementIndex = reader.ReadInt32();
		seq.GroupSize[0] = reader.ReadInt32();
		seq.GroupSize[1] = reader.ReadInt32();
		seq.ParamIndex[0] = reader.ReadInt32();
		seq.ParamIndex[1] = reader.ReadInt32();
		seq.ParamStart[0] = reader.ReadSingle();
		seq.ParamStart[1] = reader.ReadSingle();
		seq.ParamEnd[0] = reader.ReadSingle();
		seq.ParamEnd[1] = reader.ReadSingle();
		seq.ParamParent = reader.ReadInt32();
		
		seq.FadeInTime = reader.ReadSingle();
		seq.FadeOutTime = reader.ReadSingle();
		
		seq.LocalEntryNode = reader.ReadInt32();
		seq.LocalExitNode = reader.ReadInt32();
		seq.NodeFlags = reader.ReadInt32();
		
		seq.EntryPhase = reader.ReadSingle();
		seq.ExitPhase = reader.ReadSingle();
		seq.LastFrame = reader.ReadSingle();
		
		seq.NextSeq = reader.ReadInt32();
		seq.Pose = reader.ReadInt32();
		
		seq.NumIkRules = reader.ReadInt32();
		seq.NumAutoLayers = reader.ReadInt32();
		seq.AutoLayerIndex = reader.ReadInt32();
		
		seq.WeightListIndex = reader.ReadInt32();
		seq.PoseKeyIndex = reader.ReadInt32();
		
		seq.NumIkLocks = reader.ReadInt32();
		seq.IkLockIndex = reader.ReadInt32();
		
		seq.KeyValueIndex = reader.ReadInt32();
		seq.KeyValueSize = reader.ReadInt32();
		
		seq.CyclePoseIndex = reader.ReadInt32();
		
		for (int i = 0; i < 7; i++)
		{
			seq.Unused[i] = reader.ReadInt32();
		}
		
		// Now read the label string
		if (labelOffset > 0)
		{
			long currentPos = reader.BaseStream.Position;
			reader.BaseStream.Seek(startPos + labelOffset, SeekOrigin.Begin);
			
			var nameBytes = new System.Collections.Generic.List<byte>();
			byte b;
			while ((b = reader.ReadByte()) != 0)
			{
				nameBytes.Add(b);
			}
			seq.Label = Encoding.UTF8.GetString(nameBytes.ToArray());
			
			reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
		}
		
		return seq;
	}
	
	// Get the animation descriptor index for this sequence
	public int GetAnimDescIndex(BinaryReader reader, long sequenceBaseOffset)
	{
		if (AnimIndexIndex == 0)
			return -1;
		
		// Simple case: single animation (groupsize 1x1)
		if (GroupSize[0] <= 1 && GroupSize[1] <= 1)
		{
			long animIndexPos = sequenceBaseOffset + AnimIndexIndex;
			reader.BaseStream.Seek(animIndexPos, SeekOrigin.Begin);
			return reader.ReadInt16(); // Animation indices are stored as shorts
		}
		
		// Blend: read first animation index
		long pos = sequenceBaseOffset + AnimIndexIndex;
		reader.BaseStream.Seek(pos, SeekOrigin.Begin);
		return reader.ReadInt16();
	}
}

