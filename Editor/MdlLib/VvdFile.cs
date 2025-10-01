using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace MdlLib;

// VVD = Valve Vertex Data
public class VvdFile
{
	public const int VVD_SIGNATURE = 0x56534449; // "IDSV"
	public const int VERSION = 4;

	public int Id { get; set; }
	public int Version { get; set; }
	public int Checksum { get; set; }
	public int NumLods { get; set; }
	public int[] NumLodVertices { get; set; } = new int[8];
	public int NumFixups { get; set; }
	public int FixupTableStart { get; set; }
	public int VertexDataStart { get; set; }
	public int TangentDataStart { get; set; }

	public VvdVertex[] Vertices { get; set; }

	public static VvdFile Read(BinaryReader reader)
	{
		var vvd = new VvdFile();

		vvd.Id = reader.ReadInt32();
		vvd.Version = reader.ReadInt32();
		vvd.Checksum = reader.ReadInt32();
		vvd.NumLods = reader.ReadInt32();
		
		// Read 8 LOD vertex counts
		for (int i = 0; i < 8; i++)
		{
			vvd.NumLodVertices[i] = reader.ReadInt32();
		}
		
		vvd.NumFixups = reader.ReadInt32();
		vvd.FixupTableStart = reader.ReadInt32();
		vvd.VertexDataStart = reader.ReadInt32();
		vvd.TangentDataStart = reader.ReadInt32();

		if (vvd.Id != VVD_SIGNATURE)
		{
			throw new Exception($"Invalid VVD signature: 0x{vvd.Id:X8}");
		}

	if (vvd.Version != VERSION)
	{
		throw new Exception($"Unsupported VVD version: {vvd.Version}");
	}

	// Handle fixups to build correct LOD0 vertex order (matches Source behavior)
	if (vvd.NumFixups > 0 && vvd.FixupTableStart > 0)
	{
		// Read fixup table
		reader.BaseStream.Seek(vvd.FixupTableStart, SeekOrigin.Begin);
		var fixups = new (int lod, int source, int count)[vvd.NumFixups];
		for (int i = 0; i < vvd.NumFixups; i++)
		{
			int lod = reader.ReadInt32();
			int source = reader.ReadInt32();
			int count = reader.ReadInt32();
			fixups[i] = (lod, source, count);
		}

		// Determine how many raw vertices we need to read
		int rawCount = 0;
		for (int i = 0; i < fixups.Length; i++)
		{
			int end = fixups[i].source + fixups[i].count;
			if (end > rawCount) rawCount = end;
		}

		// Read raw vertex pool
		reader.BaseStream.Seek(vvd.VertexDataStart, SeekOrigin.Begin);
		var rawVertices = new VvdVertex[rawCount];
		for (int v = 0; v < rawCount; v++)
		{
			rawVertices[v] = VvdVertex.Read(reader);
		}

		// Assemble LOD0 vertex list using fixups (include fixups with lod >= 0)
		var lod0Vertices = new List<VvdVertex>();
		for (int i = 0; i < fixups.Length; i++)
		{
			if (fixups[i].lod >= 0)
			{
				for (int j = 0; j < fixups[i].count; j++)
				{
					lod0Vertices.Add(rawVertices[fixups[i].source + j]);
				}
			}
		}
		
		vvd.Vertices = lod0Vertices.ToArray();
	}
	else
	{
		// No fixups: vertices are sequential for LOD0
		reader.BaseStream.Seek(vvd.VertexDataStart, SeekOrigin.Begin);
		int vertexCount = vvd.NumLodVertices[0];
		vvd.Vertices = new VvdVertex[vertexCount];

		for (int i = 0; i < vertexCount; i++)
		{
			vvd.Vertices[i] = VvdVertex.Read(reader);
		}
	}

	return vvd;
	}
}

public struct VvdVertex
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector2 TexCoord;
	public BoneWeight[] BoneWeights; // Max 3 weights
	public byte NumBones;

	public static VvdVertex Read(BinaryReader reader)
	{
		var vertex = new VvdVertex();

		// mstudiovertex_t structure
		// Read 3 weights as floats (12 bytes)
		float[] weights = new float[3];
		for (int i = 0; i < 3; i++)
		{
			weights[i] = reader.ReadSingle();
		}
		
		// Read 3 bone indices as bytes (3 bytes)
		byte[] bones = new byte[3];
		for (int i = 0; i < 3; i++)
		{
			bones[i] = reader.ReadByte();
		}
		
		// Read bone count (1 byte)
		vertex.NumBones = reader.ReadByte();
		
		// Pack into BoneWeight structs
		vertex.BoneWeights = new BoneWeight[3];
		for (int i = 0; i < 3; i++)
		{
			vertex.BoneWeights[i] = new BoneWeight
			{
				Weight = weights[i],
				Bone = bones[i]
			};
		}
		
		// Position
		vertex.Position = new Vector3(
			reader.ReadSingle(),
			reader.ReadSingle(),
			reader.ReadSingle()
		);

		// Normal
		vertex.Normal = new Vector3(
			reader.ReadSingle(),
			reader.ReadSingle(),
			reader.ReadSingle()
		);

		// UV
		vertex.TexCoord = new Vector2(
			reader.ReadSingle(),
			reader.ReadSingle()
		);

		return vertex;
	}
}

public struct BoneWeight
{
	public float Weight;
	public byte Bone;
}

