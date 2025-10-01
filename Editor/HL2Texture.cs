using System;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox;

internal class HL2Texture : ResourceLoader<HL2Mount>
{
	public string FileName { get; set; }

	public HL2Texture(string fileName)
	{
		FileName = fileName;
	}

	protected override object Load()
	{
		Log.Info($"[HL2Texture.Load] CALLED for: {FileName} (Path: {Path})");
		try
		{
			byte[] vtfData = Host.GetFileBytes(FileName);
			if (vtfData == null || vtfData.Length == 0)
			{
				Log.Warning($"[HL2Texture.Load] Failed to load VTF file: {FileName}");
				return null;
			}

			Log.Info($"[HL2Texture.Load] Loaded {vtfData.Length} bytes for {FileName}");

			VtfFile vtf;
			using (var ms = new MemoryStream(vtfData))
			using (var reader = new BinaryReader(ms))
			{
				vtf = VtfFile.Read(reader);
			}

			if (vtf == null || vtf.ImageData == null)
			{
				Log.Warning($"[HL2Texture.Load] Failed to parse VTF: {FileName}");
				return null;
			}

			Log.Info($"[HL2Texture.Load] Parsed VTF: {vtf.Width}x{vtf.Height}, format={vtf.Format}");

			// Convert to RGBA32
			var pixels = vtf.ToRGBA32();

			if (pixels == null)
			{
				Log.Warning($"[HL2Texture.Load] ToRGBA32 returned null for {FileName}");
				return null;
			}

			Log.Info($"[HL2Texture.Load] Converted to RGBA32: {pixels.Length} pixels");

			// Convert Color32[] to byte[] for texture data
			byte[] textureData = new byte[pixels.Length * 4];
			for (int p = 0; p < pixels.Length; p++)
			{
				textureData[p * 4 + 0] = pixels[p].r;
				textureData[p * 4 + 1] = pixels[p].g;
				textureData[p * 4 + 2] = pixels[p].b;
				textureData[p * 4 + 3] = pixels[p].a;
			}

			Log.Info($"[HL2Texture.Load] Creating texture: {vtf.Width}x{vtf.Height}, data size={textureData.Length}");

			var texture = Texture.Create(vtf.Width, vtf.Height)
				.WithData(textureData)
				.Finish();

			if (texture == null)
			{
				Log.Warning($"[HL2Texture.Load] Texture.Create returned null for {FileName}");
				return null;
			}

			Log.Info($"[HL2Texture.Load] Successfully created texture for {FileName}");
			return texture;
		}
		catch (Exception ex)
		{
			Log.Warning($"[HL2Texture.Load] Exception loading {FileName}: {ex.Message}\n{ex.StackTrace}");
			return null;
		}
	}
}

