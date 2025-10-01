using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Mounting;
using VpkLib;

namespace Sandbox;

public class HL2Mount : BaseGameMount
{
	// Half-Life 2 Steam App ID
	private const long HL2_APP_ID = 220L;

	private readonly Dictionary<string, VpkFile> vpkFiles = new Dictionary<string, VpkFile>();

	public override string Ident => "hl2";
	public override string Title => "Half-Life 2";

	protected override void Initialize(InitializeContext context)
	{
		Log.Info($"[HL2Mount] Initializing...");
		
		if (!context.IsAppInstalled(HL2_APP_ID))
		{
			Log.Info($"[HL2Mount] Half-Life 2 (AppID {HL2_APP_ID}) is not installed");
			return;
		}

		string appDirectory = context.GetAppDirectory(HL2_APP_ID);
		Log.Info($"[HL2Mount] HL2 directory: {appDirectory}");
		
		// Look for VPK files in the hl2 directory
		string hl2Directory = Path.Combine(appDirectory, "hl2");
		
		if (!System.IO.Directory.Exists(hl2Directory))
		{
			Log.Warning($"[HL2Mount] hl2 subdirectory not found: {hl2Directory}");
			return;
		}

		// Find all _dir.vpk files
		Log.Info($"[HL2Mount] Scanning for VPK files in: {hl2Directory}");
		foreach (string vpkPath in System.IO.Directory.EnumerateFiles(hl2Directory, "*_dir.vpk", SearchOption.TopDirectoryOnly))
		{
			try
			{
				Log.Info($"[HL2Mount] Loading VPK: {vpkPath}");
				var vpk = new VpkFile(vpkPath);
				if (vpk.FileData.IsValid)
				{
					string vpkName = Path.GetFileNameWithoutExtension(vpkPath);
					if (vpkName.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
					{
						vpkName = vpkName.Substring(0, vpkName.Length - 4);
					}
					vpkFiles[vpkName] = vpk;
					Log.Info($"[HL2Mount] Loaded VPK '{vpkName}' with {vpk.FileData.Entries.Count} files");
				}
				else
				{
					Log.Warning($"[HL2Mount] Invalid VPK signature: {vpkPath}");
				}
			}
			catch (Exception ex)
			{
				Log.Warning($"[HL2Mount] Failed to load VPK: {vpkPath} - {ex.Message}");
			}
		}

		IsInstalled = vpkFiles.Count > 0;
		Log.Info($"[HL2Mount] Initialization complete. VPK files loaded: {vpkFiles.Count}, IsInstalled: {IsInstalled}");
	}

	public Stream GetFileStream(string filename)
	{
		byte[] fileBytes = GetFileBytes(filename);
		if (fileBytes == null)
		{
			return Stream.Null;
		}
		return new MemoryStream(fileBytes);
	}

	public byte[] GetFileBytes(string filename)
	{
		// Normalize path
		filename = filename.Replace('\\', '/');

		// Try each VPK file
		foreach (var vpk in vpkFiles.Values)
		{
			if (vpk.FileExists(filename))
			{
				Log.Info($"[HL2Mount] Loading file: {filename}");
				return vpk.GetFileBytes(filename);
			}
		}

		Log.Warning($"[HL2Mount] File not found: {filename}");
		return null;
	}

	public bool FileExists(string filename)
	{
		// Normalize path
		filename = filename.Replace('\\', '/');

		foreach (var vpk in vpkFiles.Values)
		{
			if (vpk.FileExists(filename))
			{
				return true;
			}
		}

		return false;
	}

	protected override Task Mount(MountContext context)
	{
		Log.Info($"[HL2Mount] Starting mount process...");
		
		int modelCount = 0;
		int textureCount = 0;
		int soundCount = 0;
		int otherCount = 0;

		foreach (var vpkEntry in vpkFiles)
		{
			var vpk = vpkEntry.Value;
			Log.Info($"[HL2Mount] Processing VPK '{vpkEntry.Key}' with {vpk.FileData.Entries.Count} entries");

			foreach (var entry in vpk.FileData.Entries)
			{
				string extension = Path.GetExtension(entry.PathFileName)?.ToLowerInvariant();
				
				if (string.IsNullOrWhiteSpace(extension))
				{
					otherCount++;
					continue;
				}

				string path = entry.PathFileName;

				switch (extension)
				{
					case ".mdl":
						// Change .mdl to .vmdl for s&box
						string vmdlPath = path.Substring(0, path.Length - 4) + ".vmdl";
						context.Add(ResourceType.Model, vmdlPath, new HL2Model(path));
						modelCount++;
						if (modelCount <= 3) // Log first 3 for debugging
							Log.Info($"[HL2Mount] Registered model: '{vmdlPath}' (source: '{path}')");
						break;

					case ".vtf":
						// Change .vtf to .vtex for s&box
						string vtexPath = path.Substring(0, path.Length - 4) + ".vtex";
						context.Add(ResourceType.Texture, vtexPath, new HL2Texture(path));
						textureCount++;
						if (textureCount <= 3) // Log first 3 for debugging
							Log.Info($"[HL2Mount] Registered texture: '{vtexPath}' (source: '{path}')");
						break;

					case ".wav":
					case ".mp3":
						// Change to .vsnd for s&box
						string vsndPath = path.Substring(0, path.Length - extension.Length) + ".vsnd";
						context.Add(ResourceType.Sound, vsndPath, new HL2Sound(path));
						soundCount++;
						if (soundCount <= 3) // Log first 3 for debugging
							Log.Info($"[HL2Mount] Registered sound: '{vsndPath}' (source: '{path}')");
						break;

					default:
						otherCount++;
						break;
				}
			}
		}

		Log.Info($"[HL2Mount] Mount complete - Models: {modelCount}, Textures: {textureCount}, Sounds: {soundCount}, Other: {otherCount}");
		IsMounted = true;
		return Task.CompletedTask;
	}
}

