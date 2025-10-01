using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace Sandbox;

// PHY file parser for Source Engine physics data (ragdolls, collision)
// Based on Crowbar's SourcePhyFile.vb
public class PhyFile
{
	public PhyHeader Header { get; set; }
	public List<PhyCollisionData> CollisionData { get; set; } = new List<PhyCollisionData>();
	public List<PhyRagdollConstraint> RagdollConstraints { get; set; } = new List<PhyRagdollConstraint>();
	public string TextData { get; set; }

	public static PhyFile Read(byte[] phyData)
	{
		var phy = new PhyFile();
		
		using var ms = new MemoryStream(phyData);
		using var reader = new BinaryReader(ms);
		
		// Read header
		phy.Header = PhyHeader.Read(reader);
		// Log.Info($"[PhyFile] PHY header: size={phy.Header.Size}, id={phy.Header.Id}, solidCount={phy.Header.SolidCount}");
		
	// Read collision data for each solid
	for (int i = 0; i < phy.Header.SolidCount; i++)
	{
		try
		{
			long posBeforeRead = reader.BaseStream.Position;
			long bytesRemaining = reader.BaseStream.Length - posBeforeRead;
			
			if (bytesRemaining < 4)
			{
				// Log.Warning($"[PhyFile] Not enough data to read solid {i}, only {bytesRemaining} bytes remaining");
				break;
			}
			
			var collision = PhyCollisionData.Read(reader);
			if (collision != null)
			{
				phy.CollisionData.Add(collision);
				// Log.Info($"[PhyFile] Solid {i}: {collision.ConvexMeshes.Count} convex meshes, {collision.Vertices.Count} vertices");
			}
		}
		catch (Exception ex)
		{
			// Log.Warning($"[PhyFile] Failed to read solid {i}: {ex.Message}");
			break;
		}
	}
	
	// Read text section (rest of the file)
	if (reader.BaseStream.Position < reader.BaseStream.Length)
	{
		long textSectionStart = reader.BaseStream.Position;
		int textLength = (int)(reader.BaseStream.Length - textSectionStart);
		
		if (textLength > 0)
		{
			byte[] textBytes = reader.ReadBytes(textLength);
			phy.TextData = System.Text.Encoding.ASCII.GetString(textBytes);
			
			// Parse ragdoll constraints from text data
			phy.RagdollConstraints = ParseRagdollConstraints(phy.TextData);
			
			if (phy.RagdollConstraints.Count > 0)
			{
				// Log.Info($"[PhyFile] Parsed {phy.RagdollConstraints.Count} ragdoll constraints from text section");
				
				// Debug: Log first few constraints
				for (int i = 0; i < Math.Min(3, phy.RagdollConstraints.Count); i++)
				{
					var c = phy.RagdollConstraints[i];
					// Log.Info($"[PhyFile] Constraint {i}: parent={c.ParentIndex}, child={c.ChildIndex}, X=[{c.XMin},{c.XMax}], Y=[{c.YMin},{c.YMax}], Z=[{c.ZMin},{c.ZMax}]");
				}
			}
		}
	}
	
	return phy;
}

private static List<PhyRagdollConstraint> ParseRagdollConstraints(string textData)
{
	var constraints = new List<PhyRagdollConstraint>();
	
	if (string.IsNullOrEmpty(textData))
		return constraints;
	
	var lines = textData.Split('\n');
	PhyRagdollConstraint currentConstraint = null;
	
	for (int i = 0; i < lines.Length; i++)
	{
		string line = lines[i].Trim();
		
		if (line.StartsWith("ragdollconstraint"))
		{
			currentConstraint = new PhyRagdollConstraint();
		}
		else if (line == "}" && currentConstraint != null)
		{
			// End of constraint block
			constraints.Add(currentConstraint);
			currentConstraint = null;
		}
		else if (currentConstraint != null && line.Contains("\""))
		{
			// Parse key-value pair: format is "key" "value"
			// Split by quotes: ["whitespace", "key", "whitespace", "value", ...]
			var parts = line.Split('"');
			if (parts.Length >= 4)
			{
				string key = parts[1].Trim();    // Second element is the key
				string value = parts[3].Trim();  // Fourth element is the value
				
				try
				{
					switch (key)
					{
						case "parent":
							currentConstraint.ParentIndex = int.Parse(value);
							break;
						case "child":
							currentConstraint.ChildIndex = int.Parse(value);
							break;
						case "xmin":
							currentConstraint.XMin = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "xmax":
							currentConstraint.XMax = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "xfriction":
							currentConstraint.XFriction = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "ymin":
							currentConstraint.YMin = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "ymax":
							currentConstraint.YMax = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "yfriction":
							currentConstraint.YFriction = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "zmin":
							currentConstraint.ZMin = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "zmax":
							currentConstraint.ZMax = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "zfriction":
							currentConstraint.ZFriction = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
							break;
					}
				}
				catch
				{
					// Ignore parse errors
				}
			}
		}
	}
	
	return constraints;
}
}

// PHY file header
public class PhyHeader
{
	public int Size { get; set; }
	public int Id { get; set; }
	public int SolidCount { get; set; }
	public int Checksum { get; set; }

	public static PhyHeader Read(BinaryReader reader)
	{
		var header = new PhyHeader();
		long startPos = reader.BaseStream.Position;
		
		header.Size = reader.ReadInt32();
		header.Id = reader.ReadInt32();
		header.SolidCount = reader.ReadInt32();
		header.Checksum = reader.ReadInt32();
		
		// Skip to end of header (in case header is larger than expected)
		reader.BaseStream.Seek(startPos + header.Size, SeekOrigin.Begin);
		
		return header;
	}
}

// Collision data for one solid (bone)
public class PhyCollisionData
{
	public int Size { get; set; }
	public float Mass { get; set; }
	public float Volume { get; set; }
	public float SurfaceArea { get; set; }
	public Vector3 MassCenter { get; set; }
	public List<PhyConvexMesh> ConvexMeshes { get; set; } = new List<PhyConvexMesh>();
	public List<Vector3> Vertices { get; set; } = new List<Vector3>();

	public static PhyCollisionData Read(BinaryReader reader)
	{
		var collision = new PhyCollisionData();
		long startPos = reader.BaseStream.Position;
		
		collision.Size = reader.ReadInt32();
		long nextSolidPos = reader.BaseStream.Position + collision.Size;
		
		// Log.Info($"[PhyFile] CollisionData: size={collision.Size}, startPos={startPos}, nextSolidPos={nextSolidPos}, streamLen={reader.BaseStream.Length}");
		
		if (nextSolidPos > reader.BaseStream.Length)
		{
			// Log.Warning($"[PhyFile] Solid size {collision.Size} extends beyond file end");
			return collision;
		}
		
		long phyDataPos = reader.BaseStream.Position;
		
		// Check for VPHY signature
		char[] vphyId = reader.ReadChars(4);
		bool isVPHY = new string(vphyId) == "VPHY";
		
		// Log.Info($"[PhyFile] Signature check: isVPHY={isVPHY}, pos={reader.BaseStream.Position}");
		
		// Seek back to check the signature
		reader.BaseStream.Seek(phyDataPos, SeekOrigin.Begin);
		
		if (isVPHY)
		{
			// Version 48+ format
			reader.ReadChars(4); // "VPHY"
			reader.ReadUInt16(); // version
			reader.ReadUInt16(); // model type
			reader.ReadInt32(); // surface size
			
			// dragAxisAreas (3 floats) + axisMapSize (1 int) = 16 bytes
			reader.ReadInt32();
			reader.ReadInt32();
			reader.ReadInt32();
			reader.ReadInt32();
			
			// Skip 11 more int32s (44 bytes)
			for (int i = 0; i < 11; i++)
			{
				reader.ReadInt32();
			}
		}
		else
		{
			// Version 37 format - skip 11 int32s (44 bytes)
			for (int i = 0; i < 11; i++)
			{
				reader.ReadInt32();
			}
		}
		
	// Read IVPS signature
	char[] ivpsId = reader.ReadChars(4);
	string ivpsStr = new string(ivpsId);
	if (ivpsStr != "IVPS")
	{
		// Log.Warning($"[PhyFile] Expected IVPS signature, got '{ivpsStr}' at pos {reader.BaseStream.Position-4}");
		// Seek to next solid
		reader.BaseStream.Seek(nextSolidPos, SeekOrigin.Begin);
		return collision;
	}
	
	// Log.Info($"[PhyFile] Found IVPS at pos {reader.BaseStream.Position-4}, reading convex meshes until {nextSolidPos}");
	
	// Read all convex mesh headers and triangle data
	var vertexIndices = new HashSet<int>();
	long vertexDataStreamPos = 0;
	
	int meshCount = 0;
	while (reader.BaseStream.Position < nextSolidPos)
	{
		long meshStartPos = reader.BaseStream.Position;
		
		// Check if we have enough data for mesh header
		if (reader.BaseStream.Position + 16 > nextSolidPos)
		{
			// Log.Info($"[PhyFile] Reached end of face data at pos {reader.BaseStream.Position}");
			break;
		}
		
		var mesh = new PhyConvexMesh();
		
		int vertexDataOffset = reader.ReadInt32();
		// The first mesh's vertexDataOffset tells us where the vertex section starts
		if (meshCount == 0)
		{
			vertexDataStreamPos = meshStartPos + vertexDataOffset;
			// Log.Info($"[PhyFile] Vertex data starts at pos {vertexDataStreamPos} (meshStart={meshStartPos} + offset={vertexDataOffset})");
		}
		
	mesh.BoneIndex = reader.ReadInt32() - 1; // Crowbar subtracts 1
	mesh.Flags = reader.ReadInt32();
	int triangleCount = reader.ReadInt32();
	
	// Only log first few meshes to avoid spam
	if (meshCount < 3)
		// Log.Info($"[PhyFile] Mesh {meshCount}: boneIdx={mesh.BoneIndex}, triangles={triangleCount}, pos={reader.BaseStream.Position}, vertexDataPos={vertexDataStreamPos}");
	
	// Stop if we've reached the vertex data section (check AFTER reading header)
	if (vertexDataStreamPos > 0 && reader.BaseStream.Position >= vertexDataStreamPos)
	{
		// Log.Info($"[PhyFile] Reached vertex data section at pos {reader.BaseStream.Position}, stopping mesh read");
		break;
	}
	
	// Read triangle data
	for (int i = 0; i < triangleCount; i++)
	{
		// Each triangle: 1 byte index, 1 byte unused, 2 bytes unused
		reader.ReadByte();
		reader.ReadByte();
		reader.ReadUInt16();
		
		// Then 3 vertices, each is 2 bytes index + 2 bytes unused
		for (int v = 0; v < 3; v++)
		{
			int vertexIndex = reader.ReadUInt16();
			reader.ReadUInt16(); // unused
			vertexIndices.Add(vertexIndex);
		}
	}
	
	// Check again after reading triangles - did we go into vertex data?
	if (vertexDataStreamPos > 0 && reader.BaseStream.Position > vertexDataStreamPos)
	{
		// Log.Warning($"[PhyFile] Read past vertex data section (now at {reader.BaseStream.Position}), mesh data may be corrupt");
	}
	
	collision.ConvexMeshes.Add(mesh);
	meshCount++;
	}
	
	// Seek to vertex data section
	if (vertexDataStreamPos > 0)
	{
		reader.BaseStream.Seek(vertexDataStreamPos, SeekOrigin.Begin);
	}
	
	// Log.Info($"[PhyFile] Read {meshCount} convex meshes, now reading vertex data from pos {reader.BaseStream.Position}");
	
	// Now read vertex data (all vertices for all meshes)
	// Vertices don't have a count - we read based on the highest index we saw
	int maxVertexIndex = vertexIndices.Count > 0 ? vertexIndices.Max() : 0;
	int vertexCount = maxVertexIndex + 1;
	
	// Log.Info($"[PhyFile] Reading {vertexCount} vertices (max index={maxVertexIndex})");
	
	// Source Engine uses inches as units, PHY files use meters
	// Convert from meters to inches (1 meter = 39.37 inches)
	const float METERS_TO_INCHES = 39.37f;
	
	for (int i = 0; i < vertexCount; i++)
	{
		if (reader.BaseStream.Position + 16 > reader.BaseStream.Length)
		{
			// Log.Warning($"[PhyFile] Not enough data for vertex {i}");
			break;
		}
		
		float x = reader.ReadSingle() * METERS_TO_INCHES;
		float y = reader.ReadSingle() * METERS_TO_INCHES;
		float z = reader.ReadSingle() * METERS_TO_INCHES;
		reader.ReadSingle(); // w component (unused)
		
		Vector3 vertex = new Vector3(x, y, z);
		collision.Vertices.Add(vertex);
		
		// Add to each mesh that uses this vertex
		if (vertexIndices.Contains(i))
		{
			foreach (var mesh in collision.ConvexMeshes)
			{
				mesh.Vertices.Add(vertex);
			}
		}
	}
	
	// Log.Info($"[PhyFile] Successfully read solid with {collision.ConvexMeshes.Count} meshes and {collision.Vertices.Count} vertices");
	
	// Seek to end of this solid's data
	reader.BaseStream.Seek(nextSolidPos, SeekOrigin.Begin);
	
	return collision;
	}
}

// One convex mesh within collision data
public class PhyConvexMesh
{
	public int BoneIndex { get; set; }
	public int Flags { get; set; }
	public List<Vector3> Vertices { get; set; } = new List<Vector3>();
}

// Ragdoll constraint (joint between two bones)
public class PhyRagdollConstraint
{
	public int ParentIndex { get; set; }
	public int ChildIndex { get; set; }
	
	// Rotation limits (in degrees)
	public float XMin { get; set; }
	public float XMax { get; set; }
	public float XFriction { get; set; }
	
	public float YMin { get; set; }
	public float YMax { get; set; }
	public float YFriction { get; set; }
	
	public float ZMin { get; set; }
	public float ZMax { get; set; }
	public float ZFriction { get; set; }
}

