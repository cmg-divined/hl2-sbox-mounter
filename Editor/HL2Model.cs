using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Mounting;
using MdlLib;

namespace Sandbox;

internal class HL2Model : ResourceLoader<HL2Mount>
{
	public string FileName { get; set; }

	public HL2Model(string fileName)
	{
		FileName = fileName;
	}

	protected override object Load()
	{
		Log.Info($"[HL2Model.Load] Loading: {FileName}");
		try
		{
			// Load MDL file
			byte[] mdlData = Host.GetFileBytes(FileName);
			if (mdlData == null || mdlData.Length == 0)
			{
				Log.Warning($"HL2Model: Failed to load MDL file: {FileName}");
				return CreatePlaceholder();
			}

			MdlHeader header;
			using (var ms = new MemoryStream(mdlData))
			using (var reader = new BinaryReader(ms))
			{
			header = MdlHeader.Read(reader);
			if (!header.IsValid())
			{
				Log.Warning($"HL2Model: Invalid MDL file: {FileName}");
				return CreatePlaceholder();
			}
			
			Log.Info($"[HL2Model.Load] MDL Version: {header.Version}");
		}

			// Load VVD file (vertex data)
			string vvdFileName = FileName.Replace(".mdl", ".vvd");
			byte[] vvdData = Host.GetFileBytes(vvdFileName);
			if (vvdData == null)
			{
				Log.Warning($"HL2Model: Missing VVD file: {vvdFileName}");
				return CreatePlaceholder();
			}

			VvdFile vvd;
			using (var ms = new MemoryStream(vvdData))
			using (var reader = new BinaryReader(ms))
			{
				vvd = VvdFile.Read(reader);
			}

			// Load VTX file (strip/mesh data) - try .dx90.vtx first
			string vtxFileName = FileName.Replace(".mdl", ".dx90.vtx");
			byte[] vtxData = Host.GetFileBytes(vtxFileName);
			
			if (vtxData == null)
			{
				// Try .dx80.vtx
				vtxFileName = FileName.Replace(".mdl", ".dx80.vtx");
				vtxData = Host.GetFileBytes(vtxFileName);
			}
			
			if (vtxData == null)
			{
				// Try .sw.vtx
				vtxFileName = FileName.Replace(".mdl", ".sw.vtx");
				vtxData = Host.GetFileBytes(vtxFileName);
			}

			if (vtxData == null)
			{
				Log.Warning($"HL2Model: Missing VTX file for: {FileName}");
				return CreatePlaceholder();
			}

			VtxFile vtx;
			using (var ms = new MemoryStream(vtxData))
			using (var reader = new BinaryReader(ms))
			{
				vtx = VtxFile.Read(reader);
			}

		Log.Info($"HL2Model: Parsed {FileName} - Vertices: {vvd.Vertices.Length}, BodyParts: {vtx.BodyParts.Length}");

		// Load PHY file (physics/ragdoll data) - optional
		PhyFile phy = null;
		string phyFileName = FileName.Replace(".mdl", ".phy");
		byte[] phyData = Host.GetFileBytes(phyFileName);
		if (phyData != null)
		{
			try
			{
				phy = PhyFile.Read(phyData);
				Log.Info($"[HL2Model.Load] Loaded PHY file: {phy.CollisionData.Count} solids");
			}
			catch (Exception ex)
			{
				Log.Warning($"[HL2Model.Load] Failed to parse PHY file: {ex.Message}");
			}
		}

		// Read materials
		var (textures, texturePaths) = ReadMaterials(header, mdlData);
		var meshMaterialIndices = ReadMeshMaterialIndices(header, mdlData);
		
		// Create s&box materials from VTF textures
		var materials = CreateMaterials(textures, texturePaths);

		// Build s&box model with materials
		return BuildModel(header, vvd, vtx, meshMaterialIndices, materials, phy);
		}
		catch (Exception ex)
		{
			Log.Warning($"HL2Model: Exception loading {FileName}: {ex.Message}\n{ex.StackTrace}");
			return CreatePlaceholder();
		}
	}

	private Model BuildModel(MdlHeader header, VvdFile vvd, VtxFile vtx, int[][] meshMaterialIndices, Material[] materials, PhyFile phy)
	{
		try
		{
		Log.Info($"[HL2Model.BuildModel] Starting build for {FileName}");
		
		var builder = Model.Builder;
		builder.WithName(Path);

			var material = Material.Create("model", "simple_color");
			material.Set("Color", Color.White);

	// Read and add bones to the model
	var bones = ReadBones(header);
	bool hasBones = bones.Length > 0;
			
			if (hasBones)
			{
				// Accumulate bone transforms to world space (like gmod does)
				// MDL bones are in parent-local space, but s&box AddBone expects world space
				var worldPos = new Vector3[bones.Length];
				var worldRot = new Rotation[bones.Length];
				
				for (int i = 0; i < bones.Length; i++)
				{
					var bone = bones[i];
					var localPos = new Vector3(bone.PosX, bone.PosY, bone.PosZ);
					var localRot = new Rotation(bone.QuatX, bone.QuatY, bone.QuatZ, bone.QuatW);
					int p = bone.ParentBoneIndex;
					
					if (p < 0 || p >= i)
					{
						// Root bone - use local transform as world
						worldRot[i] = localRot;
						worldPos[i] = localPos;
					}
					else
					{
						// Child bone - accumulate parent transform
						var parentRot = worldRot[p];
						worldRot[i] = parentRot * localRot;
						worldPos[i] = worldPos[p] + parentRot * localPos;
					}
				}
				
				// Add bones to model builder with world transforms
				for (int i = 0; i < bones.Length; i++)
				{
					var bone = bones[i];
					string parentName = null;
					if (bone.ParentBoneIndex >= 0 && bone.ParentBoneIndex < i)
					{
						parentName = bones[bone.ParentBoneIndex].Name;
					}
					
					builder.AddBone(bone.Name, worldPos[i], worldRot[i], parentName);
					
					if (i < 3) // Debug first few bones
					{
						Log.Info($"[HL2Model.BuildModel] Bone {i} '{bone.Name}': Parent={bone.ParentBoneIndex}, WorldPos={worldPos[i]}, WorldRot={worldRot[i]}");
					}
				}
				Log.Info($"[HL2Model.BuildModel] Added {bones.Length} bones to model");
			}

	// Read mesh vertex offsets from MDL (VTX indices are mesh-relative, not VVD-absolute!)
	// We need: BodyPartStart + ModelStart + MeshStart + vtxIndex to get absolute VVD index
	byte[] mdlData = Host.GetFileBytes(FileName);
	var vertexStarts = ReadVertexStarts(mdlData, header);
		
		// Use LOD 0 only for now
		int lodIndex = 0;
		int meshCount = 0;
		int globalMeshIdx = 0; // Track mesh index for material assignment

		Log.Info($"[HL2Model.BuildModel] Processing {vtx.BodyParts.Length} body parts");

	for (int bodyPartIdx = 0; bodyPartIdx < vtx.BodyParts.Length; bodyPartIdx++)
	{
		var vtxBodyPart = vtx.BodyParts[bodyPartIdx];
		Log.Info($"[HL2Model.BuildModel] BodyPart {bodyPartIdx}: {vtxBodyPart.Models.Length} models");

			for (int modelIdx = 0; modelIdx < vtxBodyPart.Models.Length; modelIdx++)
			{
				var vtxModel = vtxBodyPart.Models[modelIdx];
				if (lodIndex >= vtxModel.Lods.Length)
				{
					Log.Warning($"[HL2Model.BuildModel] LOD {lodIndex} not available (has {vtxModel.Lods.Length} LODs)");
					continue;
				}

				var lod = vtxModel.Lods[lodIndex];
				Log.Info($"[HL2Model.BuildModel] LOD {lodIndex}: {lod.Meshes.Length} meshes");

		for (int meshIdx = 0; meshIdx < lod.Meshes.Length; meshIdx++)
		{
			var vtxMesh = lod.Meshes[meshIdx];
			Log.Info($"[HL2Model.BuildModel] Mesh {meshIdx} has {vtxMesh.StripGroups.Length} strip groups");
			
			// Check if we have mesh start data for this mesh
			if (bodyPartIdx >= vertexStarts.MeshStart.Count || 
			    modelIdx >= vertexStarts.MeshStart[bodyPartIdx].Count ||
			    meshIdx >= vertexStarts.MeshStart[bodyPartIdx][modelIdx].Count)
			{
				Log.Warning($"[HL2Model.BuildModel] No mesh start data for BP{bodyPartIdx} Model{modelIdx} Mesh{meshIdx} - skipping");
				continue;
			}
			
			// Calculate vertex offsets for this mesh (used for all strip groups)
			int bodyPartStart = vertexStarts.BodyPartStart[bodyPartIdx];
			int modelStart = vertexStarts.ModelStart[bodyPartIdx][modelIdx];
			int meshStart = vertexStarts.MeshStart[bodyPartIdx][modelIdx][meshIdx];
			
			// COMBINE all strip groups within this VTX mesh into ONE s&box mesh
			if (hasBones)
			{
				var skinnedVertices = new List<HL2SkinnedVertex>();
				var indices = new List<int>();
				var vertexMap = new Dictionary<int, int>(); // VVD index -> output vertex index
				
				// Process all strip groups for this mesh
				foreach (var stripGroup in vtxMesh.StripGroups)
				{
					if (stripGroup.Vertices.Length == 0 || stripGroup.Indices.Length == 0)
					{
						Log.Info($"[HL2Model.BuildModel] Skipping empty strip group");
						continue;
					}

					Log.Info($"[HL2Model.BuildModel] StripGroup: {stripGroup.Vertices.Length} verts, {stripGroup.Indices.Length} indices");

					// Process indices from this strip group
					for (int i = 0; i < stripGroup.Indices.Length; i++)
					{
						int vtxVertexIndex = stripGroup.Indices[i];
						if (vtxVertexIndex >= stripGroup.Vertices.Length)
						{
							Log.Warning($"[HL2Model.BuildModel] VTX vertex index {vtxVertexIndex} out of range");
							continue;
						}
						
						// VTX indices are MESH-RELATIVE, add offsets to get absolute VVD index
						int meshRelativeIndex = stripGroup.Vertices[vtxVertexIndex];
						int vvdIndex = bodyPartStart + modelStart + meshStart + meshRelativeIndex;
						
						if (vvdIndex >= vvd.Vertices.Length)
						{
							Log.Warning($"[HL2Model.BuildModel] VVD index {vvdIndex} out of range");
							continue;
						}
						
						// Check if we already added this vertex
						if (!vertexMap.TryGetValue(vvdIndex, out int outIdx))
						{
							// Add new vertex
							var vvdVert = vvd.Vertices[vvdIndex];
							var pos = new Vector3(vvdVert.Position.x, vvdVert.Position.y, vvdVert.Position.z);
							var normal = new Vector3(vvdVert.Normal.x, vvdVert.Normal.y, vvdVert.Normal.z);
							var uv = new Vector2(vvdVert.TexCoord.x, vvdVert.TexCoord.y);

							// Pack bone indices and weights
							byte[] boneIndices = new byte[4];
							byte[] boneWeights = new byte[4];
							
							int numWeights = Math.Min((int)vvdVert.NumBones, 3);
							for (int bw = 0; bw < numWeights; bw++)
							{
								var weight = vvdVert.BoneWeights[bw];
								boneIndices[bw] = weight.Bone;
								boneWeights[bw] = (byte)(weight.Weight * 255f + 0.5f);
							}
							
							// Normalize weights to sum to 255
							int weightSum = boneWeights[0] + boneWeights[1] + boneWeights[2] + boneWeights[3];
							if (weightSum != 255 && weightSum > 0)
							{
								int diff = 255 - weightSum;
								int maxIdx = 0;
								for (int j = 1; j < numWeights; j++)
								{
									if (boneWeights[j] > boneWeights[maxIdx])
										maxIdx = j;
								}
								boneWeights[maxIdx] = (byte)(boneWeights[maxIdx] + diff);
							}

							var vertex = new HL2SkinnedVertex(
								pos, normal,
								new Vector4(1, 0, 0, 1), // tangent
								uv,
								new Color32(boneIndices[0], boneIndices[1], boneIndices[2], boneIndices[3]),
								new Color32(boneWeights[0], boneWeights[1], boneWeights[2], boneWeights[3])
							);

							outIdx = skinnedVertices.Count;
							skinnedVertices.Add(vertex);
							vertexMap[vvdIndex] = outIdx;
						}
						
						indices.Add(outIdx);
					}
				}
				
				// Flip winding order (Source to s&box)
				for (int t = 0; t + 2 < indices.Count; t += 3)
				{
					int tmp = indices[t + 1];
					indices[t + 1] = indices[t + 2];
					indices[t + 2] = tmp;
				}

			Log.Info($"[HL2Model.BuildModel] Built {skinnedVertices.Count} skinned vertices, {indices.Count} indices");

			if (skinnedVertices.Count > 0 && indices.Count > 0)
			{
				// Get material for this mesh
				Material meshMaterial = Material.Load("materials/default/default.vmat"); // Default fallback
				if (meshMaterialIndices != null && materials != null &&
				    bodyPartIdx < meshMaterialIndices.Length &&
				    globalMeshIdx < meshMaterialIndices[bodyPartIdx].Length)
				{
					int matIdx = meshMaterialIndices[bodyPartIdx][globalMeshIdx];
					if (matIdx >= 0 && matIdx < materials.Length && materials[matIdx] != null)
					{
						meshMaterial = materials[matIdx];
					}
				}
				
				try
				{
					var mesh = new Mesh(meshMaterial);
						mesh.CreateVertexBuffer(skinnedVertices.Count, HL2SkinnedVertex.Layout, skinnedVertices);
						mesh.CreateIndexBuffer(indices.Count, indices.ToArray());
						mesh.Bounds = BBox.FromPoints(skinnedVertices.Select(v => v.position));

					builder.AddMesh(mesh);
					meshCount++;
					globalMeshIdx++; // Track for material assignment
					Log.Info($"[HL2Model.BuildModel] Successfully added skinned mesh #{meshCount}");
					}
					catch (Exception meshEx)
					{
						Log.Warning($"[HL2Model.BuildModel] Failed to create skinned mesh: {meshEx.Message}");
					}
				}
			}
			else
			{
				// Static mesh - use SimpleVertex
				var simpleVertices = new List<SimpleVertex>();
				var indices = new List<int>();
				var vertexMap = new Dictionary<int, int>();
				
				// Process all strip groups for this mesh
				foreach (var stripGroup in vtxMesh.StripGroups)
				{
					if (stripGroup.Vertices.Length == 0 || stripGroup.Indices.Length == 0)
						continue;

					for (int i = 0; i < stripGroup.Indices.Length; i++)
					{
						int vtxVertexIndex = stripGroup.Indices[i];
						if (vtxVertexIndex >= stripGroup.Vertices.Length)
							continue;
						
						int meshRelativeIndex = stripGroup.Vertices[vtxVertexIndex];
						int vvdIndex = bodyPartStart + modelStart + meshStart + meshRelativeIndex;
						
						if (vvdIndex >= vvd.Vertices.Length)
							continue;
						
						if (!vertexMap.TryGetValue(vvdIndex, out int outIdx))
						{
							var vvdVert = vvd.Vertices[vvdIndex];
							var pos = new Vector3(vvdVert.Position.x, vvdVert.Position.y, vvdVert.Position.z);
							var normal = new Vector3(vvdVert.Normal.x, vvdVert.Normal.y, vvdVert.Normal.z);
							var uv = new Vector2(vvdVert.TexCoord.x, vvdVert.TexCoord.y);

							outIdx = simpleVertices.Count;
							simpleVertices.Add(new SimpleVertex(pos, normal, Vector3.Zero, uv));
							vertexMap[vvdIndex] = outIdx;
						}
						
						indices.Add(outIdx);
					}
				}
				
				// Flip winding order
				for (int t = 0; t + 2 < indices.Count; t += 3)
				{
					int tmp = indices[t + 1];
					indices[t + 1] = indices[t + 2];
					indices[t + 2] = tmp;
				}

			Log.Info($"[HL2Model.BuildModel] Built {simpleVertices.Count} simple vertices, {indices.Count} indices");

			if (simpleVertices.Count > 0 && indices.Count > 0)
			{
				// Get material for this mesh
				Material meshMaterial = Material.Load("materials/default/default.vmat"); // Default fallback
				if (meshMaterialIndices != null && materials != null &&
				    bodyPartIdx < meshMaterialIndices.Length &&
				    globalMeshIdx < meshMaterialIndices[bodyPartIdx].Length)
				{
					int matIdx = meshMaterialIndices[bodyPartIdx][globalMeshIdx];
					if (matIdx >= 0 && matIdx < materials.Length && materials[matIdx] != null)
					{
						meshMaterial = materials[matIdx];
					}
				}
				
				try
				{
					var mesh = new Mesh(meshMaterial);
						mesh.CreateVertexBuffer(simpleVertices.Count, SimpleVertex.Layout, simpleVertices);
						mesh.CreateIndexBuffer(indices.Count, indices.ToArray());
						mesh.Bounds = BBox.FromPoints(simpleVertices.Select(v => v.position));

					builder.AddMesh(mesh);
					meshCount++;
					globalMeshIdx++; // Track for material assignment
					Log.Info($"[HL2Model.BuildModel] Successfully added static mesh #{meshCount}");
					}
					catch (Exception meshEx)
					{
						Log.Warning($"[HL2Model.BuildModel] Failed to create static mesh: {meshEx.Message}");
					}
				}
			}
		}
	}
}

	Log.Info($"[HL2Model.BuildModel] Total meshes added: {meshCount}");
	
	// Load animations
	if (header.LocalSequenceCount > 0 && bones.Length > 0)
	{
		try
		{
		LoadAnimations(builder, header, bones);
	}
	catch (Exception animEx)
	{
		Log.Warning($"[HL2Model.BuildModel] Failed to load animations: {animEx.Message}");
	}
}

// Create physics bodies and joints from PHY file
if (phy != null)
{
	try
	{
		CreatePhysicsBodies(builder, phy, bones);
	}
	catch (Exception phyEx)
	{
		Log.Warning($"[HL2Model.BuildModel] Failed to create physics bodies: {phyEx.Message}");
	}
}

var model = builder.Create();
Log.Info($"[HL2Model.BuildModel] Model created successfully");
return model;
	}
		catch (Exception ex)
		{
			Log.Warning($"[HL2Model.BuildModel] Exception: {ex.Message}\n{ex.StackTrace}");
			return CreatePlaceholder();
		}
	}

	private MdlBone[] ReadBones(MdlHeader header)
	{
		try
		{
			if (header.BoneCount == 0)
				return Array.Empty<MdlBone>();

			byte[] mdlData = Host.GetFileBytes(FileName);
			using var ms = new MemoryStream(mdlData);
			using var reader = new BinaryReader(ms);

			var bones = new MdlBone[header.BoneCount];
			
			// Each bone is 216 bytes in MDL format (all versions > 10)
			const int BONE_SIZE = 216;
			
			Log.Info($"[HL2Model.ReadBones] Reading {header.BoneCount} bones from offset {header.BoneOffset}, file size: {mdlData.Length}");
			
			for (int i = 0; i < header.BoneCount; i++)
			{
				long boneStartPos = header.BoneOffset + i * BONE_SIZE;
				reader.BaseStream.Seek(boneStartPos, SeekOrigin.Begin);
				bones[i] = MdlBone.Read(reader, boneStartPos);
				
				if (i < 3)
				{
					Log.Info($"[HL2Model.ReadBones] Bone {i}: Name='{bones[i].Name}', Parent={bones[i].ParentBoneIndex}, Pos=({bones[i].PosX},{bones[i].PosY},{bones[i].PosZ})");
				}
			}

			return bones;
		}
		catch (Exception ex)
		{
			Log.Warning($"[HL2Model.ReadBones] Failed: {ex.Message}\n{ex.StackTrace}");
			return Array.Empty<MdlBone>();
		}
	}

	private Model CreatePlaceholder()
	{
		// Return a small cube as placeholder
		var material = Material.Create("model", "simple_color");
		material.Set("Color", Color.Magenta);
		
		var mesh = new Mesh(material);
		var vertices = new List<SimpleVertex>
		{
			new SimpleVertex(new Vector3(-5, -5, -5), Vector3.Up, Vector3.Zero, Vector2.Zero),
			new SimpleVertex(new Vector3(5, -5, -5), Vector3.Up, Vector3.Zero, Vector2.One),
			new SimpleVertex(new Vector3(5, 5, -5), Vector3.Up, Vector3.Zero, Vector2.One),
			new SimpleVertex(new Vector3(-5, 5, -5), Vector3.Up, Vector3.Zero, Vector2.Zero),
			new SimpleVertex(new Vector3(-5, -5, 5), Vector3.Up, Vector3.Zero, Vector2.Zero),
			new SimpleVertex(new Vector3(5, -5, 5), Vector3.Up, Vector3.Zero, Vector2.One),
			new SimpleVertex(new Vector3(5, 5, 5), Vector3.Up, Vector3.Zero, Vector2.One),
			new SimpleVertex(new Vector3(-5, 5, 5), Vector3.Up, Vector3.Zero, Vector2.Zero),
		};
		
		var indices = new int[] { 
			0, 1, 2, 0, 2, 3,
			4, 6, 5, 4, 7, 6,
			0, 4, 5, 0, 5, 1,
			3, 2, 6, 3, 6, 7,
			0, 3, 7, 0, 7, 4,
			1, 5, 6, 1, 6, 2
		};
		
	mesh.CreateVertexBuffer(vertices.Count, SimpleVertex.Layout, vertices);
	mesh.CreateIndexBuffer(indices.Length, indices);
	mesh.Bounds = new BBox(new Vector3(-5, -5, -5), new Vector3(5, 5, 5));
	
	return Model.Builder.WithName(Path).AddMesh(mesh).Create();
}

private class VertexStarts
{
	public List<int> BodyPartStart = new();
	public List<List<int>> ModelStart = new();
	public List<List<List<int>>> MeshStart = new();
}

private static VertexStarts ReadVertexStarts(byte[] mdlBytes, MdlHeader header)
{
	var starts = new VertexStarts();
	
	try
	{
		using (var stream = new MemoryStream(mdlBytes))
		using (var reader = new BinaryReader(stream))
		{
			int bodyPartAccum = 0; // Cumulative vertex count across body parts
			
			for (int bpIdx = 0; bpIdx < header.BodyPartCount; bpIdx++)
			{
				starts.BodyPartStart.Add(bodyPartAccum);
				
				// Seek to body part - each body part is 16 bytes
				long bodyPartPos = header.BodyPartOffset + (bpIdx * 16);
				stream.Seek(bodyPartPos, SeekOrigin.Begin);
				
				reader.ReadInt32(); // nameOffset
				int modelCount = reader.ReadInt32();
				reader.ReadInt32(); // base
				int modelOffset = reader.ReadInt32();
				
				var modelStarts = new List<int>();
				var meshStartsForModels = new List<List<int>>();
				int modelAccum = 0; // Cumulative vertex count within this body part
				
				for (int mIdx = 0; mIdx < modelCount; mIdx++)
				{
					modelStarts.Add(modelAccum);
					
					// Seek to model - each model is 148 bytes
					long modelPos = bodyPartPos + modelOffset + (mIdx * 148);
					stream.Seek(modelPos, SeekOrigin.Begin);
					
					// Skip 64-byte name
					reader.ReadBytes(64);
					reader.ReadInt32(); // type
					reader.ReadSingle(); // boundingRadius
					int meshCount = reader.ReadInt32();
					int meshOffset = reader.ReadInt32();
					int modelVertexCount = reader.ReadInt32();
					
					var meshStarts = new List<int>();
					
				Log.Info($"[HL2Model.ReadVertexStarts] BP{bpIdx} Model{mIdx}: meshCount={meshCount}");
				
				for (int meshIdx = 0; meshIdx < meshCount; meshIdx++)
				{
					// Seek to mesh - each mesh is 116 bytes
					long meshPos = modelPos + meshOffset + (meshIdx * 116);
					stream.Seek(meshPos, SeekOrigin.Begin);
					
					reader.ReadInt32(); // materialIndex
					reader.ReadInt32(); // modelOffset
					reader.ReadInt32(); // vertexCount
					int vertexIndexStart = reader.ReadInt32(); // Mesh's vertex offset within model
					
					meshStarts.Add(vertexIndexStart);
					
					Log.Info($"[HL2Model.ReadVertexStarts] BP{bpIdx} Model{mIdx} Mesh{meshIdx}: start={vertexIndexStart}");
				}
					
					meshStartsForModels.Add(meshStarts);
					modelAccum += modelVertexCount; // Add this model's vertex count to accumulator
				}
				
				starts.ModelStart.Add(modelStarts);
				starts.MeshStart.Add(meshStartsForModels);
				bodyPartAccum += modelAccum; // Add this body part's total vertex count
			}
		}
	}
	catch (Exception ex)
	{
		Log.Warning($"[HL2Model.ReadVertexStarts] Failed: {ex.Message}");
	}
	
	Log.Info($"[HL2Model.ReadVertexStarts] Read vertex starts for {starts.BodyPartStart.Count} body parts");
	return starts;
}

private int[][] ReadMeshMaterialIndices(MdlHeader header, byte[] mdlData)
{
	var materialIndices = new List<int[]>();
	
	try
	{
		using var stream = new MemoryStream(mdlData);
		using var reader = new BinaryReader(stream);
		
		Log.Info($"[HL2Model.ReadMeshMaterialIndices] Reading {header.BodyPartCount} body parts");
		
		for (int bpIdx = 0; bpIdx < header.BodyPartCount; bpIdx++)
		{
			long bodyPartPos = header.BodyPartOffset + (bpIdx * 16);
			stream.Seek(bodyPartPos, SeekOrigin.Begin);
			
			reader.ReadInt32(); // nameOffset
			int modelCount = reader.ReadInt32();
			reader.ReadInt32(); // base
			int modelOffset = reader.ReadInt32();
			
			Log.Info($"[HL2Model.ReadMeshMaterialIndices] BP{bpIdx}: modelCount={modelCount}, modelOffset={modelOffset}");
			
			var bodyPartMaterials = new List<int>();
			
			for (int mIdx = 0; mIdx < modelCount; mIdx++)
			{
				long modelPos = bodyPartPos + modelOffset + (mIdx * 148);
				stream.Seek(modelPos, SeekOrigin.Begin);
				
				// Skip 64-byte name, 4-byte type, 4-byte boundingRadius
				reader.ReadBytes(64);
				reader.ReadInt32(); // type
				reader.ReadSingle(); // boundingRadius
				
				int meshCount = reader.ReadInt32();
				int meshOffset = reader.ReadInt32();
				
				Log.Info($"[HL2Model.ReadMeshMaterialIndices] BP{bpIdx} Model{mIdx}: meshCount={meshCount}, meshOffset={meshOffset}, modelPos={modelPos}");
				
				for (int meshIdx = 0; meshIdx < meshCount; meshIdx++)
				{
					long meshPos = modelPos + meshOffset + (meshIdx * MdlMesh.SIZE);
					stream.Seek(meshPos, SeekOrigin.Begin);
					
					int materialIndex = reader.ReadInt32(); // First field is materialIndex
					bodyPartMaterials.Add(materialIndex);
					Log.Info($"[HL2Model.ReadMeshMaterialIndices] BP{bpIdx} Model{mIdx} Mesh{meshIdx}: materialIndex={materialIndex}");
				}
			}
			
			Log.Info($"[HL2Model.ReadMeshMaterialIndices] BP{bpIdx} total meshes: {bodyPartMaterials.Count}");
			materialIndices.Add(bodyPartMaterials.ToArray());
		}
	}
	catch (Exception ex)
	{
		Log.Warning($"[HL2Model.ReadMeshMaterialIndices] Failed: {ex.Message}");
		Log.Warning($"[HL2Model.ReadMeshMaterialIndices] Stack trace: {ex.StackTrace}");
	}
	
	return materialIndices.ToArray();
}

private Material[] CreateMaterials(MdlTexture[] textures, string[] texturePaths)
{
	if (textures == null || textures.Length == 0)
		return null;
	
	var materials = new Material[textures.Length];
	
	for (int i = 0; i < textures.Length; i++)
	{
		try
		{
			string textureName = textures[i].Name;
			Log.Info($"[HL2Model.CreateMaterials] Processing material {i}: '{textureName}'");
			
			// Try each texture path until we find the VTF file
			// Note: texture paths in MDL are relative to the materials/ folder
			string vtfPath = null;
			if (texturePaths != null)
			{
				Log.Info($"[HL2Model.CreateMaterials] Searching {texturePaths.Length} texture paths for '{textureName}'");
				foreach (var searchPath in texturePaths)
				{
					// Try with materials/ prefix
					string testPath = $"materials/{searchPath}{textureName}.vtf";
					Log.Info($"[HL2Model.CreateMaterials] Trying path: {testPath}");
					if (Host.GetFileBytes(testPath) != null)
					{
						vtfPath = testPath;
						Log.Info($"[HL2Model.CreateMaterials] Found VTF at: {vtfPath}");
						break;
					}
				}
			}
			
			// If not found, try without texture paths (directly in materials/)
			if (vtfPath == null)
			{
				string testPath = $"materials/{textureName}.vtf";
				Log.Info($"[HL2Model.CreateMaterials] Trying direct path: {testPath}");
				if (Host.GetFileBytes(testPath) != null)
				{
					vtfPath = testPath;
					Log.Info($"[HL2Model.CreateMaterials] Found VTF at: {vtfPath}");
				}
			}
			
			if (vtfPath == null)
			{
				Log.Warning($"[HL2Model.CreateMaterials] Could not find VTF for material '{textureName}'");
				materials[i] = Material.Load("materials/default/default.vmat");
				continue;
			}
			
			// Load VTF texture
			byte[] vtfData = Host.GetFileBytes(vtfPath);
			if (vtfData == null)
			{
				Log.Warning($"[HL2Model.CreateMaterials] Failed to load VTF: {vtfPath}");
				materials[i] = Material.Load("materials/default/default.vmat");
				continue;
			}
			
			Log.Info($"[HL2Model.CreateMaterials] Loaded VTF data: {vtfData.Length} bytes");
			
			VtfFile vtf;
			using (var ms = new MemoryStream(vtfData))
			using (var reader = new BinaryReader(ms))
			{
				vtf = VtfFile.Read(reader);
			}
			
			if (vtf == null || vtf.ImageData == null)
			{
				Log.Warning($"[HL2Model.CreateMaterials] Failed to parse VTF: {vtfPath}");
				materials[i] = Material.Load("materials/default/default.vmat");
				continue;
			}
			
			Log.Info($"[HL2Model.CreateMaterials] Parsed VTF: {vtf.Width}x{vtf.Height}, format={vtf.Format}");
			
			// Convert to RGBA32
			var pixels = vtf.ToRGBA32();
			
			if (pixels == null)
			{
				Log.Warning($"[HL2Model.CreateMaterials] ToRGBA32 returned null for '{textureName}'");
				materials[i] = Material.Load("materials/default/default.vmat");
				continue;
			}
			
			Log.Info($"[HL2Model.CreateMaterials] Converted to RGBA32: {pixels.Length} pixels");
			
			// Convert Color32[] to byte[] for texture data
			byte[] textureData = new byte[pixels.Length * 4];
			for (int p = 0; p < pixels.Length; p++)
			{
				textureData[p * 4 + 0] = pixels[p].r;
				textureData[p * 4 + 1] = pixels[p].g;
				textureData[p * 4 + 2] = pixels[p].b;
				textureData[p * 4 + 3] = pixels[p].a;
			}
			
			Log.Info($"[HL2Model.CreateMaterials] Creating texture: {vtf.Width}x{vtf.Height}, data size={textureData.Length}");
			
			// Create s&box texture
			var texture = Texture.Create(vtf.Width, vtf.Height)
				.WithData(textureData)
				.Finish();
			
			if (texture == null)
			{
				Log.Warning($"[HL2Model.CreateMaterials] Texture.Create returned null for '{textureName}'");
				materials[i] = Material.Load("materials/default/default.vmat");
				continue;
			}
			
			Log.Info($"[HL2Model.CreateMaterials] Texture created successfully");
			
			// Create material with simple_color (same as Quake)
			var material = Material.Create("model", "simple_color");
			material?.Set("Color", texture);
			materials[i] = material;
			
			Log.Info($"[HL2Model.CreateMaterials] Created material {i}: '{textureName}' ({vtf.Width}x{vtf.Height})");
		}
		catch (Exception ex)
		{
			Log.Warning($"[HL2Model.CreateMaterials] Failed to create material {i}: {ex.Message}");
			materials[i] = Material.Load("materials/default/default.vmat");
		}
	}
	
	return materials;
}

private (MdlTexture[] textures, string[] texturePaths) ReadMaterials(MdlHeader header, byte[] mdlData)
{
	using var ms = new MemoryStream(mdlData);
	using var reader = new BinaryReader(ms);
	
	// Read textures
	MdlTexture[] textures = null;
	if (header.TextureCount > 0 && header.TextureOffset > 0)
	{
		textures = new MdlTexture[header.TextureCount];
		for (int i = 0; i < header.TextureCount; i++)
		{
			ms.Seek(header.TextureOffset + (i * MdlTexture.SIZE), SeekOrigin.Begin);
			textures[i] = MdlTexture.Read(reader, header.TextureOffset + (i * MdlTexture.SIZE));
			Log.Info($"[HL2Model.ReadMaterials] Texture {i}: '{textures[i].Name}'");
		}
	}
	
	// Read texture paths
	string[] texturePaths = null;
	if (header.TexturePathCount > 0 && header.TexturePathOffset > 0)
	{
		texturePaths = new string[header.TexturePathCount];
		for (int i = 0; i < header.TexturePathCount; i++)
		{
			ms.Seek(header.TexturePathOffset + (i * 4), SeekOrigin.Begin);
			int pathOffset = reader.ReadInt32();
			
			if (pathOffset != 0)
			{
				ms.Seek(pathOffset, SeekOrigin.Begin);
				var pathBytes = new List<byte>();
				byte b;
				while ((b = reader.ReadByte()) != 0)
				{
					pathBytes.Add(b);
				}
				texturePaths[i] = System.Text.Encoding.UTF8.GetString(pathBytes.ToArray());
				// Normalize path separators
				texturePaths[i] = texturePaths[i].Replace('\\', '/');
				Log.Info($"[HL2Model.ReadMaterials] Texture path {i}: '{texturePaths[i]}'");
			}
			else
			{
				texturePaths[i] = "";
			}
		}
	}
	
	return (textures, texturePaths);
}

private void LoadAnimations(ModelBuilder builder, MdlHeader header, MdlBone[] bones)
{
	// Animation support temporarily disabled
	Log.Info("[HL2Model.LoadAnimations] Animation loading is currently disabled");
	return;
	
	Log.Info($"[HL2Model.LoadAnimations] Reading {header.LocalSequenceCount} sequences, {header.LocalAnimationCount} raw animations");
	Log.Info($"[HL2Model.LoadAnimations] IncludeModelCount={header.IncludeModelCount}, IncludeModelOffset={header.IncludeModelOffset}");
	
	// Load MDL data again for animation reading
	var mdlData = Host.GetFileBytes(FileName);
	
	using var ms = new MemoryStream(mdlData);
	using var reader = new BinaryReader(ms);
	
	// First, read animation blocks (for external .ani files)
	MdlAnimBlock[] animBlocks = null;
	if (header.AnimBlockCount > 0 && header.AnimBlockOffset > 0)
	{
		animBlocks = new MdlAnimBlock[header.AnimBlockCount];
		ms.Seek(header.AnimBlockOffset, SeekOrigin.Begin);
		for (int i = 0; i < header.AnimBlockCount; i++)
		{
			animBlocks[i] = MdlAnimBlock.Read(reader);
		}
		Log.Info($"[HL2Model.LoadAnimations] Read {animBlocks.Length} animation blocks from {FileName}");
		for (int i = 0; i < Math.Min(5, animBlocks.Length); i++)
		{
			Log.Info($"[HL2Model.LoadAnimations] AnimBlock[{i}]: DataStart={animBlocks[i].DataStart}, DataEnd={animBlocks[i].DataEnd}");
		}
	}
	
	// Read all animation descriptors from this model
	var animDescs = new MdlAnimDesc[header.LocalAnimationCount];
	for (int i = 0; i < header.LocalAnimationCount; i++)
	{
		long animDescPos = header.LocalAnimationOffset + (i * MdlAnimDesc.SIZE);
		ms.Seek(animDescPos, SeekOrigin.Begin);
		animDescs[i] = MdlAnimDesc.Read(reader, animDescPos);
	}
	
	int animsLoaded = 0;
	
	// Load sequences from this model
	animsLoaded += LoadSequencesFromData(builder, header, animDescs, animBlocks, mdlData, bones, FileName);
	
	// Load sequences from include models
	for (int incIdx = 0; incIdx < header.IncludeModelCount; incIdx++)
	{
		long includePos = header.IncludeModelOffset + (incIdx * MdlIncludeModel.SIZE);
		ms.Seek(includePos, SeekOrigin.Begin);
		
		var includeModel = MdlIncludeModel.Read(reader, includePos);
		
		if (string.IsNullOrEmpty(includeModel.FileName))
		{
			Log.Warning($"[HL2Model.LoadAnimations] Include model {incIdx} has no filename");
			continue;
		}
		
		Log.Info($"[HL2Model.LoadAnimations] Loading include model: {includeModel.FileName}");
		
		try
		{
			// Include model paths are already full paths from models root
			// e.g., "models/humans/male_shared.mdl"
			string includePath = includeModel.FileName;
			
			// Load the include model MDL
			var includeMdlData = Host.GetFileBytes(includePath);
			
			using var includeMs = new MemoryStream(includeMdlData);
			using var includeReader = new BinaryReader(includeMs);
			
			var includeHeader = MdlHeader.Read(includeReader);
			
		Log.Info($"[HL2Model.LoadAnimations] Include model '{includeModel.FileName}' has {includeHeader.LocalSequenceCount} sequences, {includeHeader.LocalAnimationCount} raw animations");
		
		// Read animation blocks from include model
		MdlAnimBlock[] includeAnimBlocks = null;
		if (includeHeader.AnimBlockCount > 0 && includeHeader.AnimBlockOffset > 0)
		{
			includeAnimBlocks = new MdlAnimBlock[includeHeader.AnimBlockCount];
			includeMs.Seek(includeHeader.AnimBlockOffset, SeekOrigin.Begin);
			for (int i = 0; i < includeHeader.AnimBlockCount; i++)
			{
				includeAnimBlocks[i] = MdlAnimBlock.Read(includeReader);
			}
			Log.Info($"[HL2Model.LoadAnimations] Read {includeAnimBlocks.Length} animation blocks from {includeModel.FileName}");
			for (int i = 0; i < Math.Min(10, includeAnimBlocks.Length); i++)
			{
				Log.Info($"[HL2Model.LoadAnimations] AnimBlock[{i}]: DataStart={includeAnimBlocks[i].DataStart}, DataEnd={includeAnimBlocks[i].DataEnd}");
			}
		}
		else
		{
			Log.Warning($"[HL2Model.LoadAnimations] No anim blocks found for {includeModel.FileName}: AnimBlockCount={includeHeader.AnimBlockCount}, AnimBlockOffset={includeHeader.AnimBlockOffset}");
		}
		
		// Read animation descriptors from include model
		Log.Info($"[HL2Model.LoadAnimations] Reading {includeHeader.LocalAnimationCount} animation descriptors from offset {includeHeader.LocalAnimationOffset}");
		var includeAnimDescs = new MdlAnimDesc[includeHeader.LocalAnimationCount];
		for (int i = 0; i < includeHeader.LocalAnimationCount; i++)
		{
			long animDescPos = includeHeader.LocalAnimationOffset + (i * MdlAnimDesc.SIZE);
			includeMs.Seek(animDescPos, SeekOrigin.Begin);
			includeAnimDescs[i] = MdlAnimDesc.Read(includeReader, animDescPos);
			
			if (i < 3) // Log first 3 for debugging
			{
				Log.Info($"[HL2Model.LoadAnimations] AnimDesc {i}: Name='{includeAnimDescs[i].Name}', AnimBlock={includeAnimDescs[i].AnimBlock}, AnimOffset={includeAnimDescs[i].AnimOffset}");
			}
		}
		
		Log.Info($"[HL2Model.LoadAnimations] Successfully read all animation descriptors, calling LoadSequencesFromData");
		
		// Load sequences from include model
		int loaded = LoadSequencesFromData(builder, includeHeader, includeAnimDescs, includeAnimBlocks, includeMdlData, bones, includePath);
		Log.Info($"[HL2Model.LoadAnimations] Loaded {loaded} animations from '{includeModel.FileName}'");
		animsLoaded += loaded;
		}
	catch (Exception ex)
	{
		Log.Warning($"[HL2Model.LoadAnimations] Failed to load include model '{includeModel.FileName}': {ex.GetType().Name} - {ex.Message}");
		Log.Warning($"[HL2Model.LoadAnimations] Stack trace: {ex.StackTrace}");
	}
	}
	
	Log.Info($"[HL2Model.LoadAnimations] Total animations loaded: {animsLoaded}");
}

private int LoadSequencesFromData(ModelBuilder builder, MdlHeader header, MdlAnimDesc[] animDescs, MdlAnimBlock[] animBlocks, byte[] mdlData, MdlBone[] bones, string sourcePath)
{
	using var ms = new MemoryStream(mdlData);
	using var reader = new BinaryReader(ms);
	
	int animsLoaded = 0;
	int skippedExternal = 0;
	int skippedNoData = 0;
	int skippedOther = 0;
	
	// Load .ani file upfront if this model uses external animation files
	byte[] aniFileCache = null;
	
	// Try to load .ani file for this model (many shared animation files use .ani files)
	try
	{
		string modelBasePath = sourcePath.Substring(0, sourcePath.LastIndexOf('.'));
		string aniFilePath = modelBasePath + ".ani";
		
		aniFileCache = Host?.GetFileBytes(aniFilePath);
		
		if (aniFileCache != null)
		{
			Log.Info($"[HL2Model.LoadSequences] Loaded ANI file: {aniFilePath} ({aniFileCache.Length} bytes)");
		}
		else
		{
			Log.Info($"[HL2Model.LoadSequences] No .ani file found for {sourcePath}, animations may be embedded");
		}
	}
	catch (Exception ex)
	{
		Log.Info($"[HL2Model.LoadSequences] Could not load .ani file: {ex.Message}");
	}
	
	// Read sequences (these are the actual usable animations)
	for (int seqIdx = 0; seqIdx < header.LocalSequenceCount; seqIdx++)
	{
		long seqDescPos = header.LocalSequenceOffset + (seqIdx * MdlSequenceDesc.SIZE);
		ms.Seek(seqDescPos, SeekOrigin.Begin);
		
		var seqDesc = MdlSequenceDesc.Read(reader, seqDescPos);
		
		if (string.IsNullOrEmpty(seqDesc.Label))
		{
			skippedOther++;
			continue;
		}
		
		// Get the animation descriptor index this sequence uses
		int animDescIndex = seqDesc.GetAnimDescIndex(reader, seqDescPos);
		
		if (animDescIndex < 0 || animDescIndex >= animDescs.Length)
		{
			skippedOther++;
			continue;
		}
		
		var animDesc = animDescs[animDescIndex];
		
		if (animDesc.FrameCount <= 0)
		{
			skippedOther++;
			continue;
		}
		
	// Skip animations with no data
	if (animDesc.AnimOffset == 0)
	{
		skippedNoData++;
		continue;
	}
	
	// Check if animation data is external (.ani file) or embedded
	byte[] animDataSource = mdlData;
	long animDataOffset;
	
	// Check AnimBlock field: 0 = embedded in MDL, > 0 = external .ani file
	if (animDesc.AnimBlock > 0 && aniFileCache != null)
	{
		// Animation data is in external .ani file
		animDataSource = aniFileCache;
		
		// Calculate offset using anim blocks
		if (animBlocks != null && animDesc.AnimBlock < animBlocks.Length)
		{
			// Offset = animBlocks[AnimBlock].DataStart + AnimOffset
			int blockStart = animBlocks[animDesc.AnimBlock].DataStart;
			animDataOffset = blockStart + animDesc.AnimOffset;
			Log.Info($"[HL2Model.LoadSequences] Calculated offset: animBlocks[{animDesc.AnimBlock}].DataStart ({blockStart}) + animOffset ({animDesc.AnimOffset}) = {animDataOffset}");
		}
		else
		{
			// Fallback if anim blocks not loaded
			Log.Warning($"[HL2Model.LoadSequences] No anim block data for AnimBlock={animDesc.AnimBlock}, animBlocks={(animBlocks == null ? "null" : animBlocks.Length.ToString())}, using AnimOffset directly");
			animDataOffset = animDesc.AnimOffset;
		}
	}
	else if (animDesc.AnimBlock > 0 && aniFileCache == null)
	{
		// Animation is external but .ani file not loaded
		skippedExternal++;
		continue;
	}
	else
	{
		// Animation data is embedded in MDL
		// Calculate absolute offset for embedded animation data
		long animDescPos = header.LocalAnimationOffset + (animDescIndex * MdlAnimDesc.SIZE);
		animDataOffset = animDescPos + animDesc.AnimOffset;
	}
		
		Log.Info($"[HL2Model.LoadSequences] Trying to load '{seqDesc.Label}' from {sourcePath}: animBlock={animDesc.AnimBlock}, animDescIdx={animDescIndex}, animOffset={animDesc.AnimOffset}, finalOffset={animDataOffset}, isExternal={animDataSource != mdlData}, dataLen={animDataSource.Length}, animDescFlags=0x{animDesc.Flags:X}");
		
		try
		{
			// Decode animation frames from the appropriate data source
			Transform[][] frames;
			
			if (animDataSource == mdlData)
			{
				// Use existing reader for embedded animations
				frames = MdlAnim.DecodeAnimation(reader, animDataOffset, bones.Length, animDesc.FrameCount, bones);
			}
			else
			{
				// Create new reader for external ANI file data
				using var aniMs = new MemoryStream(animDataSource);
				using var aniReader = new BinaryReader(aniMs);
				frames = MdlAnim.DecodeAnimation(aniReader, animDataOffset, bones.Length, animDesc.FrameCount, bones);
			}
			
			if (frames == null || frames.Length == 0)
			{
				continue;
			}
			
			// Add animation to model using sequence name
			var animBuilder = builder.AddAnimation(seqDesc.Label, (int)MathF.Round(animDesc.Fps));
			
			// Set animation flags from sequence
			if ((seqDesc.Flags & (int)StudioAnimFlags.Looping) != 0)
			{
				animBuilder.WithLooping(true);
			}
			
			// Add each frame
			for (int frameIdx = 0; frameIdx < frames.Length; frameIdx++)
			{
				animBuilder.AddFrame(frames[frameIdx]);
			}
			
			animsLoaded++;
		}
		catch (Exception ex)
		{
			Log.Warning($"[HL2Model.LoadSequences] Failed to load sequence '{seqDesc.Label}' from {sourcePath}: {ex.Message}");
		}
	}
	
	if (skippedExternal > 0 || skippedNoData > 0 || skippedOther > 0)
	{
		Log.Info($"[HL2Model.LoadSequences] Skipped from {sourcePath}: {skippedExternal} external .ani, {skippedNoData} no data, {skippedOther} other");
	}
	
	return animsLoaded;
}

private static string ReadNullTerminatedString(BinaryReader reader)
{
	var bytes = new List<byte>();
	byte b;
	while ((b = reader.ReadByte()) != 0)
	{
		bytes.Add(b);
	}
	return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
}

private void CreatePhysicsBodies(ModelBuilder builder, PhyFile phy, MdlBone[] bones)
{
	if (phy == null || phy.CollisionData.Count == 0)
		return;

	Log.Info($"[HL2Model.CreatePhysicsBodies] Creating {phy.CollisionData.Count} physics bodies");

	var physicsBodies = new List<PhysicsBodyBuilder>();
	var boneToBodyMap = new Dictionary<int, int>(); // Map bone index to physics body index

	// Create a physics body for each solid in the PHY file
	for (int solidIdx = 0; solidIdx < phy.CollisionData.Count; solidIdx++)
	{
		var collision = phy.CollisionData[solidIdx];
		
		if (collision.ConvexMeshes.Count == 0)
		{
			Log.Warning($"[HL2Model.CreatePhysicsBodies] Solid {solidIdx} has no convex meshes, skipping");
			continue;
		}

		// Get the bone index from the first convex mesh
		// (all meshes in a solid should reference the same bone)
		int boneIndex = collision.ConvexMeshes[0].BoneIndex;
		
		// Find the bone name
		string boneName = null;
		if (boneIndex >= 0 && boneIndex < bones.Length)
		{
			boneName = bones[boneIndex].Name;
		}

		// Create physics body attached to bone for initial positioning
		float mass = collision.Mass > 0 ? collision.Mass : 1.0f;
		var physicsBody = builder.AddBody(mass, default(Surface), boneName);
		
		Log.Info($"[HL2Model.CreatePhysicsBodies] Body {solidIdx}: bone='{boneName}' (index={boneIndex}), mass={mass:F2}kg");

		// Add collision hulls from convex meshes
		int hullsAdded = 0;
		foreach (var mesh in collision.ConvexMeshes)
		{
			if (mesh.Vertices.Count >= 4) // Need at least 4 vertices for a hull
			{
				try
				{
					// Debug: log vertex range
					var minVertex = mesh.Vertices[0];
					var maxVertex = mesh.Vertices[0];
					foreach (var v in mesh.Vertices)
					{
						minVertex = new Vector3(Math.Min(minVertex.x, v.x), Math.Min(minVertex.y, v.y), Math.Min(minVertex.z, v.z));
						maxVertex = new Vector3(Math.Max(maxVertex.x, v.x), Math.Max(maxVertex.y, v.y), Math.Max(maxVertex.z, v.z));
					}
					
					// s&box has issues with very complex hulls - if too many verts, create a simple box instead
					Vector3[] hullVerts;
					if (mesh.Vertices.Count > 64)
					{
						Log.Warning($"[HL2Model.CreatePhysicsBodies] Body {solidIdx} hull has {mesh.Vertices.Count} verts (too many), using bounding box instead");
						Log.Info($"[HL2Model.CreatePhysicsBodies] Body {solidIdx} box bounds=({minVertex.x:F2},{minVertex.y:F2},{minVertex.z:F2}) to ({maxVertex.x:F2},{maxVertex.y:F2},{maxVertex.z:F2})");
						
						// Create a box from the bounds
						hullVerts = new Vector3[]
						{
							new Vector3(minVertex.x, minVertex.y, minVertex.z),
							new Vector3(maxVertex.x, minVertex.y, minVertex.z),
							new Vector3(maxVertex.x, maxVertex.y, minVertex.z),
							new Vector3(minVertex.x, maxVertex.y, minVertex.z),
							new Vector3(minVertex.x, minVertex.y, maxVertex.z),
							new Vector3(maxVertex.x, minVertex.y, maxVertex.z),
							new Vector3(maxVertex.x, maxVertex.y, maxVertex.z),
							new Vector3(minVertex.x, maxVertex.y, maxVertex.z)
						};
					}
					else
					{
						Log.Info($"[HL2Model.CreatePhysicsBodies] Body {solidIdx} hull: {mesh.Vertices.Count} verts, bounds=({minVertex.x:F2},{minVertex.y:F2},{minVertex.z:F2}) to ({maxVertex.x:F2},{maxVertex.y:F2},{maxVertex.z:F2})");
						hullVerts = mesh.Vertices.ToArray();
					}
					
					physicsBody.AddHull(hullVerts);
					hullsAdded++;
				}
				catch (Exception ex)
				{
					Log.Warning($"[HL2Model.CreatePhysicsBodies] Failed to add hull to body {solidIdx}: {ex.Message}");
				}
			}
		}

		if (hullsAdded == 0)
		{
			// Fallback: create a small box if no hulls were added
			Log.Warning($"[HL2Model.CreatePhysicsBodies] No hulls added for body {solidIdx}, using fallback box");
			var fallbackHull = new Vector3[]
			{
				new Vector3(-2, -2, -2),
				new Vector3( 2, -2, -2),
				new Vector3( 2,  2, -2),
				new Vector3(-2,  2, -2),
				new Vector3(-2, -2,  2),
				new Vector3( 2, -2,  2),
				new Vector3( 2,  2,  2),
				new Vector3(-2,  2,  2)
			};
			physicsBody.AddHull(fallbackHull);
		}
		else
		{
			Log.Info($"[HL2Model.CreatePhysicsBodies] Added {hullsAdded} collision hulls to body {solidIdx}");
		}

		physicsBodies.Add(physicsBody);
		if (boneIndex >= 0)
		{
			boneToBodyMap[boneIndex] = physicsBodies.Count - 1;
		}
	}

	// Recalculate bone world transforms (needed for joint positioning)
	var boneWorldPos = new Vector3[bones.Length];
	var boneWorldRot = new Rotation[bones.Length];
	
	for (int i = 0; i < bones.Length; i++)
	{
		var bone = bones[i];
		Vector3 localPos = new Vector3(bone.PosX, bone.PosY, bone.PosZ);
		Rotation localRot = new Rotation(bone.QuatX, bone.QuatY, bone.QuatZ, bone.QuatW);
		
		if (bone.ParentBoneIndex >= 0 && bone.ParentBoneIndex < i)
		{
			// Accumulate: world = parentWorld * local
			boneWorldPos[i] = boneWorldPos[bone.ParentBoneIndex] + boneWorldRot[bone.ParentBoneIndex] * localPos;
			boneWorldRot[i] = boneWorldRot[bone.ParentBoneIndex] * localRot;
		}
		else
		{
			// Root bone
			boneWorldPos[i] = localPos;
			boneWorldRot[i] = localRot;
		}
	}
	
	// Create ball joints between physics bodies based on bone hierarchy
	int jointsCreated = 0;
	for (int i = 0; i < physicsBodies.Count; i++)
	{
		var collision = phy.CollisionData[i];
		int boneIndex = collision.ConvexMeshes[0].BoneIndex;
		
		if (boneIndex < 0 || boneIndex >= bones.Length)
			continue;
		
		var bone = bones[boneIndex];
		
		// Walk up the bone hierarchy to find the nearest ancestor with a physics body
		int parentBoneIndex = bone.ParentBoneIndex;
		int parentBodyIndex = -1;
		
		while (parentBoneIndex >= 0)
		{
			if (boneToBodyMap.ContainsKey(parentBoneIndex))
			{
				parentBodyIndex = boneToBodyMap[parentBoneIndex];
				break;
			}
			parentBoneIndex = bones[parentBoneIndex].ParentBoneIndex;
		}
		
		// If we found a parent body, create a joint
		if (parentBodyIndex >= 0)
		{
			
		try
		{
			// Get the parent bone that has the physics body
			int parentPhysicsBoneIndex = phy.CollisionData[parentBodyIndex].ConvexMeshes[0].BoneIndex;
			
			Log.Info($"[HL2Model.CreatePhysicsBodies] Creating joint: parentBody={parentBodyIndex} (bone={parentPhysicsBoneIndex}), childBody={i} (bone={boneIndex})");
			
			// Get bone world positions and rotations
			Vector3 childBonePos = boneWorldPos[boneIndex];
			Vector3 parentBonePos = boneWorldPos[parentPhysicsBoneIndex];
			Rotation parentBoneRot = boneWorldRot[parentPhysicsBoneIndex];
			Rotation childBoneRot = boneWorldRot[boneIndex];
			
			// Transform the joint position from world space to parent body's local space
			Vector3 jointOffsetWorld = childBonePos - parentBonePos;
			Vector3 jointOffsetLocal = parentBoneRot.Inverse * jointOffsetWorld;
			
			// Transform the child bone rotation into parent's local space
			Rotation jointRotLocal = parentBoneRot.Inverse * childBoneRot;
			
			// frame1: where joint attaches on parent body (in parent's local space with child's orientation)
			// frame2: where joint attaches on child body (at origin with identity rotation since body is centered on bone)
			Transform frame1 = new Transform(jointOffsetLocal, jointRotLocal);
			Transform frame2 = new Transform(Vector3.Zero, Rotation.Identity);
			
			var jointBuilder = builder.AddBallJoint(parentBodyIndex, i, frame1, frame2, false);
			
			// Look up ragdoll constraint from PHY file
			// Try matching by solid index first, then by bone index
			var constraint = phy.RagdollConstraints.FirstOrDefault(c => 
				c.ParentIndex == parentBodyIndex && c.ChildIndex == i);
			
			// If not found by body index, try matching by bone index
			if (constraint == null)
			{
				int parentBoneIdx = phy.CollisionData[parentBodyIndex].ConvexMeshes[0].BoneIndex;
				constraint = phy.RagdollConstraints.FirstOrDefault(c => 
					c.ParentIndex == parentBoneIdx && c.ChildIndex == boneIndex);
			}
			
			float swingLimit, twistMin, twistMax;
			
			if (constraint != null)
			{
				// Use PHY file constraints
				// Source uses X/Y/Z limits, we need to convert to swing/twist
				// Typically: X and Y are swing axes, Z is twist axis
				swingLimit = Math.Max(Math.Abs(constraint.XMax), Math.Abs(constraint.YMax));
				twistMin = constraint.ZMin;
				twistMax = constraint.ZMax;
				
				jointBuilder.WithSwingLimit(swingLimit)
				           .WithTwistLimit(twistMin, twistMax);
			}
			else
			{
				// Fallback to reasonable defaults
				swingLimit = 45.0f;
				twistMin = -30.0f;
				twistMax = 30.0f;
				
				jointBuilder.WithSwingLimit(swingLimit)
				           .WithTwistLimit(twistMin, twistMax);
			}
			
			jointsCreated++;
			
			string childName = bones[boneIndex].Name;
			string parentName = bones[parentPhysicsBoneIndex].Name;
			string constraintInfo = constraint != null ? 
				$"swing={swingLimit:F1}°, twist=[{twistMin:F1}°,{twistMax:F1}°]" :
				$"swing={swingLimit:F1}° (default)";
			Log.Info($"[HL2Model.CreatePhysicsBodies] Created joint: '{childName}' -> '{parentName}' ({constraintInfo})");
			}
			catch (Exception ex)
			{
				Log.Warning($"[HL2Model.CreatePhysicsBodies] Failed to create joint for body {i}: {ex.Message}");
			}
		}
	}
	
	Log.Info($"[HL2Model.CreatePhysicsBodies] Created {physicsBodies.Count} physics bodies and {jointsCreated} ball joints successfully");
}
}

