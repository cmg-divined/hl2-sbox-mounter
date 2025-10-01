using System;
using Sandbox.Mounting;

namespace Sandbox;

internal class HL2Sound : ResourceLoader<HL2Mount>
{
	public string FileName { get; set; }

	public HL2Sound(string fileName)
	{
		FileName = fileName;
	}

	protected override object Load()
	{
		try
		{
			byte[] soundData = Host.GetFileBytes(FileName);
			if (soundData == null || soundData.Length == 0)
			{
				Log.Warning($"HL2Sound: Failed to load sound file: {FileName}");
				return null;
			}

			// Detect format by extension
			string extension = System.IO.Path.GetExtension(FileName).ToLowerInvariant();

			switch (extension)
			{
				case ".wav":
					return SoundFile.FromWav(Path, soundData, loop: false);

				case ".mp3":
					// MP3 support might need different handling
					Log.Warning($"HL2Sound: MP3 format not yet supported: {FileName}");
					return null;

				default:
					Log.Warning($"HL2Sound: Unsupported sound format: {extension}");
					return null;
			}
		}
		catch (Exception ex)
		{
			Log.Warning($"HL2Sound: Exception loading {FileName}: {ex.Message}");
			return null;
		}
	}
}

