using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace MdlLib;

public static class MdlAnim
{
	// Animation flags for mstudioanim_t (from Source Engine bone_setup.cpp)
	private const int STUDIO_ANIM_RAWPOS = 0x01;   // Vector48
	private const int STUDIO_ANIM_RAWROT = 0x02;   // Quaternion48
	private const int STUDIO_ANIM_ANIMPOS = 0x04;  // Position offsets (compressed)
	private const int STUDIO_ANIM_ANIMROT = 0x08;  // Rotation offsets (compressed)
	private const int STUDIO_ANIM_DELTA = 0x10;    // Delta animation (don't add to bind pose)
	private const int STUDIO_ANIM_RAWROT2 = 0x20;  // Quaternion64
	
	// Bone animation header structure
	private class BoneAnimHeader
	{
		public byte BoneIndex;
		public byte Flags;
		public long BasePos; // File position of this bone header (start of boneIndex byte)
		public long OffsetBase; // File position where offsets start (BasePos + 4 bytes for header)
		
		// Union data (28 bytes)
		// If animated (flags set): 6 short offsets + 7 short unused (total 13 shorts = 26 bytes) + 2 bytes padding
		public short[] Offsets; // [6] offsets to compressed anim data (RELATIVE TO OffsetBase)
		
		// If static (no flags): raw position + quaternion
		public Vector3? StaticPos;
		public Rotation? StaticRot;
	}

	// Decode animation data for all bones across all frames
	//
	// Based on Crowbar's SourceMdlFile44.vb ReadMdlAnimation()
	// The animation data is a variable-length array of per-bone animation structures:
	//   byte boneIndex (255 = end of list)
	//   byte flags
	//   short nextOffset
	//   [variable data based on flags]
	//
	public static Transform[][] DecodeAnimation(BinaryReader reader, long animDataOffset, int boneCount, int frameCount, MdlBone[] bones)
	{
		if (animDataOffset == 0 || frameCount == 0 || boneCount == 0)
			return null;
		
		try
		{
		long streamLength = reader.BaseStream.Length;
		
		if (animDataOffset >= streamLength)
			{
				// Log.Warning($"[MdlAnim] Animation offset {animDataOffset} is beyond stream length {streamLength}");
				return null;
			}
			
		// Seek to start of animation data (no alignment - test if this fixes the issue)
		reader.BaseStream.Seek(animDataOffset, SeekOrigin.Begin);
			
			// Result: [frame][bone] in parent-local space
			var framesLocal = new Transform[frameCount][];
			for (int frame = 0; frame < frameCount; frame++)
			{
				framesLocal[frame] = new Transform[boneCount];
			}
			
		// Initialize all bones to their bind pose
		for (int frame = 0; frame < frameCount; frame++)
		{
			for (int b = 0; b < boneCount; b++)
			{
				var bone = bones[b];
				framesLocal[frame][b] = new Transform(
					new Vector3(bone.PosX, bone.PosY, bone.PosZ),
					new Rotation(bone.QuatX, bone.QuatY, bone.QuatZ, bone.QuatW)
				);
			}
		}
			
		// Read per-bone animation structures and store them
		var boneAnims = new List<BoneAnimHeader>();
		var seenBones = new HashSet<byte>();
		int boneHeaderCount = 0;
		
		while (true)
		{
			long boneStartPos = reader.BaseStream.Position;
			
			// Safety checks
			if (boneHeaderCount >= boneCount * 2)
			{
				// Log.Warning($"[MdlAnim] Read too many bone headers ({boneHeaderCount}), stopping");
				break;
			}
			
			if (reader.BaseStream.Position >= streamLength - 4)
			{
				// Log.Warning($"[MdlAnim] Reached near end of stream, stopping");
				break;
			}
			
			// Read bone index
			byte boneIndex = reader.ReadByte();
			
			// boneIndex == 255 means end of animation data
			if (boneIndex == 255)
			{
				reader.ReadByte();   // padding
				reader.ReadInt16();  // padding
				break;
			}
			
			// Validate bone index
			if (boneIndex >= boneCount)
			{
				// Log.Warning($"[MdlAnim] Invalid boneIndex={boneIndex}");
				break;
			}
			
			// Check for duplicate
			if (seenBones.Contains(boneIndex))
			{
				reader.BaseStream.Seek(-1, SeekOrigin.Current);
				break;
			}
			
			// Read flags and next offset
			byte flags = reader.ReadByte();
			short nextOffset = reader.ReadInt16();
			
		// Create bone anim header
		var boneAnim = new BoneAnimHeader
		{
			BoneIndex = boneIndex,
			Flags = flags,
			BasePos = boneStartPos,
			OffsetBase = boneStartPos + 4 // Offsets are relative to start of union data (after 4-byte header)
		};
		
		// Read the union data based on flags
		bool hasCompressedAnim = (flags & (STUDIO_ANIM_ANIMPOS | STUDIO_ANIM_ANIMROT)) != 0;
		bool hasRawAnim = (flags & (STUDIO_ANIM_RAWPOS | STUDIO_ANIM_RAWROT | STUDIO_ANIM_RAWROT2)) != 0;
		
		if (hasCompressedAnim)
		{
			// Compressed animation: Read 6 short offsets (12 bytes) + 16 bytes padding = 28 bytes total
			boneAnim.Offsets = new short[6];
			for (int i = 0; i < 6; i++)
			{
				boneAnim.Offsets[i] = reader.ReadInt16();
			}
			reader.ReadBytes(16); // Skip unused shorts + padding
		}
		else if (hasRawAnim)
		{
			// Raw animation: frame data is stored inline (variable length based on frameCount)
			// We don't read it here - the decoding functions will seek back to BasePos + 4 to read it
			// Just use nextOffset to jump to the next bone header (if it exists)
			// Don't manually skip - nextOffset already accounts for the variable-length data
		}
		else
		{
			// Static pose: Read 3 floats position + 4 floats quaternion (28 bytes)
			float px = reader.ReadSingle();
			float py = reader.ReadSingle();
			float pz = reader.ReadSingle();
			boneAnim.StaticPos = new Vector3(px, py, pz);
			
			float qx = reader.ReadSingle();
			float qy = reader.ReadSingle();
			float qz = reader.ReadSingle();
			float qw = reader.ReadSingle();
			boneAnim.StaticRot = new Rotation(qx, qy, qz, qw);
		}
			
			boneAnims.Add(boneAnim);
			seenBones.Add(boneIndex);
			boneHeaderCount++;
			
			// Jump to next bone using nextOffset
			if (nextOffset > 0)
			{
				reader.BaseStream.Seek(boneStartPos + nextOffset, SeekOrigin.Begin);
			}
			else
			{
				// Last bone header
				break;
			}
		}
		
		// Now decode animation values for each bone
		foreach (var boneAnim in boneAnims)
		{
			int boneIdx = boneAnim.BoneIndex;
			var bone = bones[boneIdx];
			
		// If static pose, this is an absolute transform (not delta)
		// Use it directly for all frames
		if (boneAnim.StaticPos.HasValue && boneAnim.StaticRot.HasValue)
		{
			for (int frame = 0; frame < frameCount; frame++)
			{
				framesLocal[frame][boneIdx] = new Transform(
					boneAnim.StaticPos.Value,
					boneAnim.StaticRot.Value
				);
			}
			continue;
		}
			
			// Otherwise, decode animated data
			if (boneAnim.Offsets != null)
			{
				DecodeAnimatedBone(reader, boneAnim, bone, framesLocal, frameCount);
			}
		}
		
		// s&box AnimationBuilder.AddFrame expects parent-local transforms (like HLA)
		// Even though AddBone uses world-space, AddFrame uses local
		return framesLocal;
	}
	catch (Exception ex)
	{
		// Log.Warning($"[MdlAnim] Failed to decode animation: {ex.Message}");
		return null;
	}
}

// Decode animated bone data (compressed values)
private static void DecodeAnimatedBone(BinaryReader reader, BoneAnimHeader boneAnim, MdlBone bone, Transform[][] frames, int frameCount)
{
	int boneIdx = boneAnim.BoneIndex;
	
	bool hasAnimPos = (boneAnim.Flags & STUDIO_ANIM_ANIMPOS) != 0;
	bool hasRawPos = (boneAnim.Flags & STUDIO_ANIM_RAWPOS) != 0;
	bool hasAnimRot = (boneAnim.Flags & STUDIO_ANIM_ANIMROT) != 0;
	bool hasRawRot = (boneAnim.Flags & STUDIO_ANIM_RAWROT) != 0;
	bool hasRawRot2 = (boneAnim.Flags & STUDIO_ANIM_RAWROT2) != 0;
	
	bool hasDelta = (boneAnim.Flags & STUDIO_ANIM_DELTA) != 0;
	
	if (boneIdx < 3 && frameCount > 0)
	{
		// Log.Info($"[MdlAnim] Bone {boneIdx} anim flags byte: 0x{boneAnim.Flags:X2} - RAWPOS={hasRawPos}, ANIMPOS={hasAnimPos}, RAWROT={hasRawRot}, RAWROT2={hasRawRot2}, ANIMROT={hasAnimRot}, DELTA={hasDelta}");
	}
	
	// Decode position (if animated)
	Vector3[] positions = null;
	if (hasAnimPos || hasRawPos)
	{
		positions = DecodePositionChannel(reader, boneAnim, bone, frameCount);
	}
	
	// Decode rotation (if animated)
	Rotation[] rotations = null;
	if (hasAnimRot || hasRawRot || hasRawRot2)
	{
		rotations = DecodeRotationChannel(reader, boneAnim, bone, frameCount);
	}
	
	// Apply to frames (using bind pose as default)
	for (int frame = 0; frame < frameCount; frame++)
	{
		Vector3 pos = positions != null ? positions[frame] : new Vector3(bone.PosX, bone.PosY, bone.PosZ);
		Rotation rot = rotations != null ? rotations[frame] : new Rotation(bone.QuatX, bone.QuatY, bone.QuatZ, bone.QuatW);
		
		frames[frame][boneIdx] = new Transform(pos, rot);
		
		if (boneIdx < 3 && frame == 0)
		{
			// Log.Info($"[MdlAnim] Bone {boneIdx} Frame0 result: pos={pos}, rot={rot}");
		}
	}
}

// Decode position channel
private static Vector3[] DecodePositionChannel(BinaryReader reader, BoneAnimHeader boneAnim, MdlBone bone, int frameCount)
{
	var result = new Vector3[frameCount];
	
	// Check if we have compressed data or raw data
	if ((boneAnim.Flags & STUDIO_ANIM_RAWPOS) != 0)
	{
		// Raw position data (Vector48 format) - 3x float16 (6 bytes per frame)
		// All frames are stored sequentially in the union section
		long posOffset = boneAnim.BasePos + 4; // Skip 4-byte header
		
		// Check if there's also rotation data before position (all frames of rotation come first)
		if ((boneAnim.Flags & STUDIO_ANIM_RAWROT) != 0)
		{
			posOffset += 6 * frameCount; // Quaternion48 is 6 bytes per frame
		}
		else if ((boneAnim.Flags & STUDIO_ANIM_RAWROT2) != 0)
		{
			posOffset += 8 * frameCount; // Quaternion64 is 8 bytes per frame
		}
		
		try
		{
			reader.BaseStream.Seek(posOffset, SeekOrigin.Begin);
			for (int i = 0; i < frameCount; i++)
			{
				ushort x16 = reader.ReadUInt16();
				ushort y16 = reader.ReadUInt16();
				ushort z16 = reader.ReadUInt16();
				result[i] = new Vector3(
					Float16ToFloat32(x16),
					Float16ToFloat32(y16),
					Float16ToFloat32(z16)
				);
			}
		}
		catch (Exception ex)
		{
			// Log.Warning($"[MdlAnim] Failed to decode RAWPOS for bone {boneAnim.BoneIndex}: {ex.Message}");
			for (int i = 0; i < frameCount; i++)
			{
				result[i] = new Vector3(bone.PosX, bone.PosY, bone.PosZ);
			}
		}
		return result;
	}
	
	if ((boneAnim.Flags & STUDIO_ANIM_ANIMPOS) != 0)
	{
		// Compressed position data (offsets are relative to OffsetBase)
		long xOffset = boneAnim.OffsetBase + boneAnim.Offsets[0];
		long yOffset = boneAnim.OffsetBase + boneAnim.Offsets[1];
		long zOffset = boneAnim.OffsetBase + boneAnim.Offsets[2];
		
		float[] xValues = ExtractAnimValues(reader, xOffset, frameCount, bone.PosScaleX);
		float[] yValues = ExtractAnimValues(reader, yOffset, frameCount, bone.PosScaleY);
		float[] zValues = ExtractAnimValues(reader, zOffset, frameCount, bone.PosScaleZ);
		
		// Add animation delta to bind pose (Crowbar approach)
		for (int i = 0; i < frameCount; i++)
		{
			result[i] = new Vector3(
				bone.PosX + xValues[i],
				bone.PosY + yValues[i],
				bone.PosZ + zValues[i]
			);
		}
		return result;
	}
	
	// No animation data, use bind pose
	for (int i = 0; i < frameCount; i++)
	{
		result[i] = new Vector3(bone.PosX, bone.PosY, bone.PosZ);
	}
	return result;
}

// Decode rotation channel  
private static Rotation[] DecodeRotationChannel(BinaryReader reader, BoneAnimHeader boneAnim, MdlBone bone, int frameCount)
{
	var result = new Rotation[frameCount];
	
	// Check if we have compressed data or raw data
	if ((boneAnim.Flags & (STUDIO_ANIM_RAWROT | STUDIO_ANIM_RAWROT2)) != 0)
	{
		// Raw rotation data (Quaternion48/64 format) - all frames stored sequentially
		long rotOffset = boneAnim.BasePos + 4; // Skip 4-byte header
		
		try
		{
			reader.BaseStream.Seek(rotOffset, SeekOrigin.Begin);
			
			if ((boneAnim.Flags & STUDIO_ANIM_RAWROT2) != 0)
			{
				// Quaternion64: 8 bytes per frame (x:21, y:21, z:21, wneg:1)
				// Read all frames sequentially
				for (int i = 0; i < frameCount; i++)
				{
					byte[] qBytes = reader.ReadBytes(8);
					result[i] = DecodeQuaternion64(qBytes);
				}
			}
			else if ((boneAnim.Flags & STUDIO_ANIM_RAWROT) != 0)
			{
				// Quaternion48: 6 bytes per frame (3x float16) - TODO: implement properly
				// For now use bind pose
				// Log.Warning($"[MdlAnim] Quaternion48 not implemented for bone {boneAnim.BoneIndex}, using bind pose");
				for (int i = 0; i < frameCount; i++)
				{
					result[i] = new Rotation(bone.QuatX, bone.QuatY, bone.QuatZ, bone.QuatW);
				}
			}
		}
		catch (Exception ex)
		{
			// Log.Warning($"[MdlAnim] Failed to decode RAWROT for bone {boneAnim.BoneIndex}: {ex.Message}");
			for (int i = 0; i < frameCount; i++)
			{
				result[i] = new Rotation(bone.QuatX, bone.QuatY, bone.QuatZ, bone.QuatW);
			}
		}
		return result;
	}
	
	if ((boneAnim.Flags & STUDIO_ANIM_ANIMROT) != 0)
	{
		// Compressed rotation data (stored as Euler angles, offsets are relative to OffsetBase)
		float[] xValues = ExtractAnimValues(reader, boneAnim.OffsetBase + boneAnim.Offsets[3], frameCount, bone.RotScaleX);
		float[] yValues = ExtractAnimValues(reader, boneAnim.OffsetBase + boneAnim.Offsets[4], frameCount, bone.RotScaleY);
		float[] zValues = ExtractAnimValues(reader, boneAnim.OffsetBase + boneAnim.Offsets[5], frameCount, bone.RotScaleZ);
		
		// Add animation delta to bind pose (Crowbar approach)
		for (int i = 0; i < frameCount; i++)
		{
			// Convert Euler angles (radians) to quaternion, add bind pose
			result[i] = EulerToRotation(
				xValues[i] + bone.RotX,
				yValues[i] + bone.RotY,
				zValues[i] + bone.RotZ
			);
		}
		return result;
	}
	
	// No animation data, use bind pose
	for (int i = 0; i < frameCount; i++)
	{
		result[i] = new Rotation(bone.QuatX, bone.QuatY, bone.QuatZ, bone.QuatW);
	}
	return result;
}

// Extract compressed animation values (RLE decompression)
// Based on Crowbar's ExtractAnimValue
private static float[] ExtractAnimValues(BinaryReader reader, long offset, int frameCount, float scale)
{
	var result = new float[frameCount];
	
	if (offset == 0)
	{
		// No data, all zeros
		return result;
	}
	
	try
	{
		reader.BaseStream.Seek(offset, SeekOrigin.Begin);
		
		int frame = 0;
		while (frame < frameCount)
		{
			// Read RLE header
			byte valid = reader.ReadByte();
			byte total = reader.ReadByte();
			
		// Read 'valid' number of values
		for (int i = 0; i < valid && frame < frameCount; i++)
		{
			short value = reader.ReadInt16();
			result[frame] = value * scale;
			
			if (frame == 0 && offset >= 902320 && offset <= 902330)
			{
				// Log.Info($"[MdlAnim] ExtractAnimValues at offset {offset}: frame0 raw short={value}, scale={scale}, result={result[frame]}");
			}
			
			frame++;
		}
			
			// Skip 'total - valid' frames (use last value)
			if (frame > 0 && frame < frameCount)
			{
				float lastValue = result[frame - 1];
				for (int i = valid; i < total && frame < frameCount; i++)
				{
					result[frame] = lastValue;
					frame++;
				}
			}
		}
	}
	catch (Exception ex)
	{
		// Log.Warning($"[MdlAnim] Failed to extract anim values at offset {offset}: {ex.Message}");
	}
	
	return result;
}

// Convert Euler angles (radians) to Rotation
private static Rotation EulerToRotation(float pitch, float yaw, float roll)
{
	// Source Engine uses XYZ order (pitch, yaw, roll)
	float cy = MathF.Cos(yaw * 0.5f);
	float sy = MathF.Sin(yaw * 0.5f);
	float cp = MathF.Cos(pitch * 0.5f);
	float sp = MathF.Sin(pitch * 0.5f);
	float cr = MathF.Cos(roll * 0.5f);
	float sr = MathF.Sin(roll * 0.5f);
	
	float qw = cr * cp * cy + sr * sp * sy;
	float qx = sr * cp * cy - cr * sp * sy;
	float qy = cr * sp * cy + sr * cp * sy;
	float qz = cr * cp * sy - sr * sp * cy;
	
	return new Rotation(qx, qy, qz, qw);
}

// Convert parent-local animation frames to world space
// s&box expects world-space transforms (like we use for AddBone)
private static Transform[][] ConvertToWorldSpace(Transform[][] framesLocal, MdlBone[] bones)
{
	int frameCount = framesLocal.Length;
	int boneCount = bones.Length;
	
	var framesWorld = new Transform[frameCount][];
	
	for (int frame = 0; frame < frameCount; frame++)
	{
		framesWorld[frame] = new Transform[boneCount];
		
		// Accumulate transforms from parent to child
		for (int boneIdx = 0; boneIdx < boneCount; boneIdx++)
		{
			var bone = bones[boneIdx];
			var localTransform = framesLocal[frame][boneIdx];
			
			if (bone.ParentBoneIndex == -1)
			{
				// Root bone - already in world space
				framesWorld[frame][boneIdx] = localTransform;
			}
			else
			{
				// Child bone - accumulate parent world transform
				var parentWorld = framesWorld[frame][bone.ParentBoneIndex];
				
				// World = Parent.World * Local
				var worldPos = parentWorld.Position + parentWorld.Rotation * localTransform.Position;
				var worldRot = parentWorld.Rotation * localTransform.Rotation;
				
				framesWorld[frame][boneIdx] = new Transform(worldPos, worldRot);
			}
		}
	}
	
	return framesWorld;
}

// Convert 16-bit half-precision float to 32-bit float
private static float Float16ToFloat32(ushort value)
{
	// Extract components: sign (1 bit), exponent (5 bits), mantissa (10 bits)
	int mantissa = value & 0x3FF;
	int exponent = (value & 0x7C00) >> 10;
	int sign = (value & 0x8000) >> 15;
	
	// Special cases
	if (exponent == 31 && mantissa == 0) // Infinity
	{
		return (sign == 1 ? -1 : 1) * 65504.0f;
	}
	if (exponent == 31 && mantissa != 0) // NaN
	{
		return 0.0f;
	}
	if (exponent == 0 && mantissa != 0) // Denormalized
	{
		float mantissaFloat = mantissa / 1024.0f;
		return (sign == 1 ? -1 : 1) * mantissaFloat * (1.0f / 16384.0f);
	}
	if (exponent == 0 && mantissa == 0) // Zero
	{
		return 0.0f;
	}
	
	// Convert to float32 representation
	// Mantissa: shift from 10 bits to 23 bits
	int resultMantissa = mantissa << 13;
	// Exponent: rebias from 15 to 127
	int resultExponent = (exponent - 15 + 127) << 23;
	// Sign: move to bit 31
	int resultSign = sign << 31;
	
	int resultBits = resultSign | resultExponent | resultMantissa;
	unsafe
	{
		return *(float*)&resultBits;
	}
}

// Decode Quaternion64 from 8 bytes (x:21, y:21, z:21, wneg:1)
private static Rotation DecodeQuaternion64(byte[] bytes)
{
	// Extract 21-bit x, y, z components and 1-bit wneg
	int x = (bytes[0] & 0xFF) | ((bytes[1] & 0xFF) << 8) | ((bytes[2] & 0x1F) << 16);
	int y = ((bytes[2] & 0xE0) >> 5) | ((bytes[3] & 0xFF) << 3) | ((bytes[4] & 0xFF) << 11) | ((bytes[5] & 0x3) << 19);
	int z = ((bytes[5] & 0xFC) >> 2) | ((bytes[6] & 0xFF) << 6) | ((bytes[7] & 0x7F) << 14);
	int wneg = (bytes[7] & 0x80) >> 7;
	
	// Convert from 21-bit integer range to -1..1 float range
	// Formula: (value - 1048576) * (1 / 1048576.5)
	float qx = (x - 1048576) * (1.0f / 1048576.5f);
	float qy = (y - 1048576) * (1.0f / 1048576.5f);
	float qz = (z - 1048576) * (1.0f / 1048576.5f);
	float qw = MathF.Sqrt(Math.Max(0, 1 - qx * qx - qy * qy - qz * qz));
	
	if (wneg == 1)
	{
		qw = -qw;
	}
	
	return new Rotation(qx, qy, qz, qw);
}
}
