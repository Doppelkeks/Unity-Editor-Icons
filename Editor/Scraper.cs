using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using static UnityEditorIconScraper.Settings;

namespace UnityEditorIconScraper {
	public static class Scraper {
		/// <summary>
		/// Constants for identifiers, paths or other types
		/// Adjust as needed.
		/// </summary> 
		private const string README_TEMPLATE_UNITY_VERSION = "{unityVersion}";
        private const string ICON_PARTIAL_ESCAPEDPATH = "{escapedPath}";
        private const string ICON_PARTIAL_ICONNAME = "{iconName}";
        private const string ICON_PARTIAL_FILEID = "{fileID}";
        private const string GENERATE_README_MESSAGE = "Generate README";
		private const string EXPORT_ICONS_MESSAGE = "Export Icons";
		private const string ICON_FILE_ENDING = ".png";
        private const string ASSET_FILE_ENDING = ".asset";
		 
		/// <summary>
		/// Cached template contents
		/// </summary>
		private static string _readmeTemplateText = null;
		private static string _readmeTemplateFileName = null;
		private static string _iconPartialText = null;

		/// <summary>
		/// Basic data structure for parallel file writes.
		/// </summary>
		private struct IconFileData {
			public string filePath;
			public byte[] bytes;
			public string fileID;
		}

		/// <summary>
		/// Menu command to export all built-in icons in parallel.
		/// </summary>
		[MenuItem(WINDOW_PATH + "/" + EXPORT_ICONS_MESSAGE, priority = -1001)]
		private static void ExportIconsMenuItem() {
			// Using an async entry point for a menu item is fine.
			ExportIconsAsync();
		}

		/// <summary>
		/// Menu command to generate a README, exporting smaller icons in parallel.
		/// </summary>
		[MenuItem(WINDOW_PATH + "/" + GENERATE_README_MESSAGE, priority = -1000)]
		private static void GenerateReadmeMenuItem() {
			GenerateReadmeAsync();
		}

		/// <summary>
		/// Asynchronously exports all built-in icons, parallelizing file writes.
		/// </summary>
		private static async void ExportIconsAsync() {
			EditorUtility.DisplayProgressBar(EXPORT_ICONS_MESSAGE, "Gathering icon data...", 0.0f);

			try {
				// 1. Get all relevant icons on main thread.
				List<IconFileData> iconFiles = CreateIconData(Settings.instance.originalIconsOutputPath);

				// 2. Perform parallel writes on a background thread
				EditorUtility.DisplayProgressBar(EXPORT_ICONS_MESSAGE, "Writing files...", 1.0f);

				await Task.Run(() => {
					// If you'd like to limit concurrency, 
					// you can use a ParallelOptions with MaxDegreeOfParallelism.
					Parallel.ForEach(iconFiles, iconInfo => {
						File.WriteAllBytes(iconInfo.filePath, iconInfo.bytes);
					});
				});

				Debug.Log($"{iconFiles.Count} icons have been exported to '{Settings.instance.originalIconsOutputPath}'.");

			} catch (Exception ex) {
				Debug.LogError($"ExportIconsAsync failed: {ex}");
			} finally {
				EditorUtility.ClearProgressBar();
			}
		}

		/// <summary>
		/// Asynchronously generates a README, exporting smaller icons in parallel and 
		/// then running pngquant. Also writes the README in an async manner.
		/// </summary>
		private static async void GenerateReadmeAsync() {
			EditorUtility.DisplayProgressBar(GENERATE_README_MESSAGE, "Gathering icon data...", 0.0f);
			string readmeOutputDirectory = Settings.instance.readmeOutputPath;

            try {
				// 1) Read template files asynchronously
				if (_readmeTemplateText == null) {
					string readmeTemplatePath = Path.GetFullPath(AssetDatabase.GUIDToAssetPath(Settings.instance.readmeTemplateGUID));
					_readmeTemplateFileName = Path.GetFileName(readmeTemplatePath);
					_readmeTemplateText = await File.ReadAllTextAsync(readmeTemplatePath);
					// Replace {unityVersion} if present
					_readmeTemplateText = _readmeTemplateText.Replace(README_TEMPLATE_UNITY_VERSION, Application.unityVersion);
				}
				if (_iconPartialText == null) {
					string iconPartialTemplatePath = Path.GetFullPath(AssetDatabase.GUIDToAssetPath(Settings.instance.iconPartialTemplateGUID));
					// Use async file reads
					_iconPartialText = await File.ReadAllTextAsync(iconPartialTemplatePath);
				}

				// 2) Gather icons for "small" output
				EditorUtility.DisplayProgressBar(GENERATE_README_MESSAGE, "Gathering small icon data...", 0.25f);
				List<IconFileData> iconFiles = CreateIconData(Settings.instance.smallIconsOutputPath);

				// 3) Parallel write small icons
				EditorUtility.DisplayProgressBar(GENERATE_README_MESSAGE, "Writing icons in parallel...", 0.5f);
				await Task.Run(() => {
					Parallel.ForEach(iconFiles, iconInfo => {
						File.WriteAllBytes(iconInfo.filePath, iconInfo.bytes);
					});
				});

				// 4) Optional compression with pngquant
				EditorUtility.DisplayProgressBar(GENERATE_README_MESSAGE, "Running pngquant...", 0.6f);
				await RunPngQuantAsync(Settings.instance.smallIconsOutputPath);

				// 5) Build the final README by inserting rows for each icon
				EditorUtility.DisplayProgressBar(GENERATE_README_MESSAGE, "Building README content...", 0.8f);
				StringBuilder sb = new StringBuilder(_readmeTemplateText);

				for (int i = 0; i < iconFiles.Count; i++) {
					if (i % 50 == 0) {
						float progress = (float)i / iconFiles.Count;
						EditorUtility.DisplayProgressBar(
							GENERATE_README_MESSAGE,
							$"Generating README lines... {i}/{iconFiles.Count}",
							0.8f + (0.2f * progress)
						);
					}

					IconFileData iconFileData = iconFiles[i];
					string filePath = iconFileData.filePath;
                    filePath = filePath.Substring(readmeOutputDirectory.Length);

                    string iconName = Path.GetFileNameWithoutExtension(filePath);

					// Escape for markdown
					string escapedPath = filePath.Replace(" ", "%20").Replace("\\", "/");

					// Insert into partial template
					string partialLine = _iconPartialText
						.Replace(ICON_PARTIAL_ESCAPEDPATH, escapedPath)
						.Replace(ICON_PARTIAL_ICONNAME, iconName)
						.Replace(ICON_PARTIAL_FILEID, iconFileData.fileID);

					sb.AppendLine(partialLine);
				}

				// 6) Write the final README asynchronously
				string fullReadmeOutputPath = Path.Combine(Settings.instance.readmeOutputPath, _readmeTemplateFileName);
				await File.WriteAllTextAsync(fullReadmeOutputPath, sb.ToString());

				Debug.Log($"{_readmeTemplateFileName} has been generated at '{readmeOutputDirectory}'.");
			} catch (Exception ex) {
				Debug.LogError($"{nameof(GenerateReadmeAsync)} failed: {ex}");
			} finally {
				EditorUtility.ClearProgressBar();
			}
		}

		/// <summary>
		/// Runs 'pngquant' recursively (-r) on the specified folder, on Windows only.
		/// This compresses all *.png in the folder and subfolders.
		/// </summary>
		private static async Task RunPngQuantAsync(string targetFolder) {
			if (Application.platform != RuntimePlatform.WindowsEditor) {
				Debug.Log("Skipping pngquant (not on Windows).");
				return;
			}

			string fullPathToExe = Path.GetFullPath(Settings.instance.pngquantExePath);
			if (!File.Exists(fullPathToExe)) {
				Debug.LogWarning($"pngquant not installed in: '{fullPathToExe}'");
				return;
			}

			if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder)) {
				Debug.LogWarning($"Cannot run pngquant on '{targetFolder}' — folder does not exist!");
				return;
			}

			string arguments = Settings.instance.pngquantArguments;
			string dir = Path.GetFullPath(targetFolder);

			try {
				await Task.Run(() => {
					ProcessStartInfo psi = new ProcessStartInfo {
						FileName = fullPathToExe,
						Arguments = arguments,
						CreateNoWindow = true,
						UseShellExecute = false,
						WorkingDirectory = dir
					};

					using (Process proc = Process.Start(psi)) {
						proc?.WaitForExit();
					}
				});

				Debug.Log($"pngquant done in folder: '{targetFolder}'.");

			} catch (Exception ex) {
				Debug.LogError($"{nameof(RunPngQuantAsync)} failed: {ex}");
			}
		}

		/// <summary>
		/// Enumerates all asset names that correspond to icons in the Editor asset bundle.
		/// </summary>
		private static IEnumerable<string> EnumerateIcons(AssetBundle editorAssetBundle, string iconsPath) {
			foreach (string assetName in editorAssetBundle.GetAllAssetNames()) {
				// Must be in the icons folder and end with .png or .asset
				if (!assetName.StartsWith(iconsPath, StringComparison.OrdinalIgnoreCase))
					continue;

				if (!assetName.EndsWith(ICON_FILE_ENDING, StringComparison.OrdinalIgnoreCase) &&
					!assetName.EndsWith(ASSET_FILE_ENDING, StringComparison.OrdinalIgnoreCase))
					continue;

				yield return assetName;
			}
		}

		/// <summary>
		/// Create all the relevant icon data
		/// </summary>
		/// <returns></returns>
		private static List<IconFileData> CreateIconData(string outputPath) {
			AssetBundle editorAssetBundle = ReflectionMethods.GetEditorAssetBundle();
			string iconsPath = GetIconsPath();
			string[] assetNames = EnumerateIcons(editorAssetBundle, iconsPath).ToArray();

			List<IconFileData> iconFiles = new List<IconFileData>(assetNames.Length);

			for (int i = 0; i < assetNames.Length; i++) {
				if (i % 50 == 0) {
					float progress = (float)i / assetNames.Length;
					EditorUtility.DisplayProgressBar(EXPORT_ICONS_MESSAGE, $"Processing icon {i + 1}/{assetNames.Length}", progress);
				}

				string assetName = assetNames[i];
				Texture2D icon = editorAssetBundle.LoadAsset<Texture2D>(assetName);
				if (icon == null)
					continue;

				string fileid = ReflectionMethods.GetFileIDHint(icon);

				// Make it readable on the main thread
				Texture2D readableTexture = new Texture2D(icon.width, icon.height, icon.format, icon.mipmapCount > 1);
				Graphics.CopyTexture(icon, readableTexture);

				// Decompress on main thread
				Texture2D finalTexture = DeCompress(readableTexture);
				byte[] bytes = finalTexture.EncodeToPNG();

				// Build the final file path
				string relativeSubFolder = assetName.Substring(iconsPath.Length).TrimStart('/');
				string folderPath = Path.GetDirectoryName(Path.Combine(outputPath, relativeSubFolder));
				if (!Directory.Exists(folderPath))
					Directory.CreateDirectory(folderPath);

				string iconFilePath = Path.Combine(folderPath, icon.name + ICON_FILE_ENDING);

				// Store in a list for parallel I/O
				iconFiles.Add(new IconFileData {
					filePath = iconFilePath,
					bytes = bytes,
					fileID = fileid,
				});
			}

			return iconFiles;
		}

		/// <summary>
		/// Converts a texture to an uncompressed and readable texture 
		/// by blitting to a RenderTexture (MUST be done on the main thread).
		/// </summary>
		public static Texture2D DeCompress(Texture2D source) {
			RenderTexture renderTex = RenderTexture.GetTemporary(
				source.width,
				source.height,
				0,
				RenderTextureFormat.Default,
				RenderTextureReadWrite.Linear);

			Graphics.Blit(source, renderTex);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = renderTex;

			Texture2D readableTex = new Texture2D(source.width, source.height);
			readableTex.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
			readableTex.Apply();

			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(renderTex);
			return readableTex;
		}

		/// <summary>
		/// Gets the built-in icons path, e.g., "Resources/unity_editor_resources/icons/" 
		/// This is an experimental property and may change in future Unity versions.
		/// </summary>
		private static string GetIconsPath() {
			return UnityEditor.Experimental.EditorResources.iconsPath;
		}

	}
}
