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
        /// constant generation variables
        /// </summary>
        private const string PARAM_SIGN = "%";
        private const string COMMENT_SIGN = "//";
        private const string COMMENT_SIGN_AND_PARAM_SIGN = COMMENT_SIGN + PARAM_SIGN;
        private const string CONSTANT_LINE = COMMENT_SIGN_AND_PARAM_SIGN + "0";
        private const string LOOKUP_ENTRY_LINE = COMMENT_SIGN_AND_PARAM_SIGN + "1";
        private const string CONST_KEY = PARAM_SIGN + "constKey";
        private const string CONST_VALUE = PARAM_SIGN + "constValue";
        private const string CONST_TYPE = PARAM_SIGN + "constType";
        private const string CONST_COMMENT = PARAM_SIGN + "constComment";
        private const string CLASS_NAME_KEYS = COMMENT_SIGN_AND_PARAM_SIGN + "classNameKeys";
        private const string NAMESPACE_RUNTIME = COMMENT_SIGN_AND_PARAM_SIGN + "namespaceRuntime";
        private const string CSHARP_FILE_ENDING = ".cs";

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
            public string validAssetName;
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
        /// Menu command to generate a README, exporting smaller icons in parallel.
        /// </summary>
        [MenuItem(WINDOW_PATH + "/Generate Constants", priority = -1000)]
        private static void GenerateConstants() {
            GenerateConstantsFromTemplates();
        }

        /// <summary>
        /// Asynchronously exports all built-in icons, parallelizing file writes.
        /// </summary>
        private static async void ExportIconsAsync() {
			EditorUtility.DisplayProgressBar(EXPORT_ICONS_MESSAGE, "Gathering icon data...", 0.0f);

			try {
				// 1. Get all relevant icons on main thread.
				List<IconFileData> iconFiles = CreateIconData(Settings.instance.originalIconsOutputPath, false);

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
#if UNITY_6000_0_OR_NEWER
					_readmeTemplateText = await File.ReadAllTextAsync(readmeTemplatePath);
#else
                    _readmeTemplateText = File.ReadAllText(readmeTemplatePath);
#endif
                    // Replace {unityVersion} if present
                    _readmeTemplateText = _readmeTemplateText.Replace(README_TEMPLATE_UNITY_VERSION, Application.unityVersion);
				}
				if (_iconPartialText == null) {
					string iconPartialTemplatePath = Path.GetFullPath(AssetDatabase.GUIDToAssetPath(Settings.instance.iconPartialTemplateGUID));
                    // Use async file reads
#if UNITY_6000_0_OR_NEWER
					_iconPartialText = await File.ReadAllTextAsync(iconPartialTemplatePath);
#else
                    _iconPartialText = File.ReadAllText(iconPartialTemplatePath);
#endif
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

					// Escape for markdown
					string escapedPath = filePath.Replace(" ", "%20").Replace("\\", "/");

					// Insert into partial template
					string partialLine = _iconPartialText
						.Replace(ICON_PARTIAL_ESCAPEDPATH, escapedPath)
						.Replace(ICON_PARTIAL_ICONNAME, iconFileData.validAssetName)
						.Replace(ICON_PARTIAL_FILEID, iconFileData.fileID);

					sb.AppendLine(partialLine);
				}

				// 6) Write the final README asynchronously
				string fullReadmeOutputPath = Path.Combine(Settings.instance.readmeOutputPath, _readmeTemplateFileName);
#if UNITY_6000_0_OR_NEWER
				await File.WriteAllTextAsync(fullReadmeOutputPath, sb.ToString());
#else
                File.WriteAllText(fullReadmeOutputPath, sb.ToString());
#endif

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
		/// Create icon data without the actuat byte info
		/// </summary>
		/// <returns></returns>
		private static List<(string, string, string)> CreateIconData() {
			AssetBundle editorAssetBundle = ReflectionMethods.GetEditorAssetBundle();
			string iconsPath = GetIconsPath();
			string[] assetNames = EnumerateIcons(editorAssetBundle, iconsPath).ToArray();

			List<(string, string, string)> iconFiles = new List<(string, string, string)>(assetNames.Length);

			for (int i = 0; i < assetNames.Length; i++) {

                string assetName = assetNames[i];
                Texture2D icon = editorAssetBundle.LoadAsset<Texture2D>(assetName);
                if (icon == null)
                    continue;

                string validAssetName = GetValidAssetName(assetName);
                string fileid = ReflectionMethods.GetFileIDHint(icon);

				iconFiles.Add((fileid, icon.name, validAssetName));
            }

			return iconFiles;
		}

		/// <summary>
		/// Create a valid asset name
		/// </summary>
		/// <param name="assetName"></param>
		/// <returns></returns>
		private static string GetValidAssetName(string assetName) {
            string validAssetName = assetName.Replace("icons/", "");
            if (validAssetName.EndsWith(ICON_FILE_ENDING)) {
                validAssetName = validAssetName.Substring(0, validAssetName.Length - ICON_FILE_ENDING.Length);
            } else if (validAssetName.EndsWith(ASSET_FILE_ENDING)) {
                validAssetName = validAssetName.Substring(0, validAssetName.Length - ASSET_FILE_ENDING.Length);
            }
			return validAssetName;
        }

        /// <summary>
        /// Create all the relevant icon data
        /// </summary>
        /// <returns></returns>
        private static List<IconFileData> CreateIconData(string outputPath, bool resize = true) {
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

				string validAssetName = GetValidAssetName(assetName);
                string fileid = ReflectionMethods.GetFileIDHint(icon);

				// Make it readable on the main thread
				Texture2D readableTexture = new Texture2D(icon.width, icon.height, icon.format, icon.mipmapCount > 1);
				Graphics.CopyTexture(icon, readableTexture);

				// Decompress on main thread
				Texture2D finalTexture = DeCompress(readableTexture);

				// resize to max width
				if (resize) {
					finalTexture = ResizeToMaxWidth(finalTexture, Settings.instance.maxWidthForReadmeIcons);
				}

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
					validAssetName=validAssetName,
				});
			}

			return iconFiles;
		}

		/// <summary>
		/// Resizes the given texture so that its width does not exceed <paramref name="maxWidth"/>,
		/// and its height is automatically adjusted to maintain the original aspect ratio.
		/// </summary>
		/// <param name="source">The source texture to resize.</param>
		/// <param name="maxWidth">The maximum allowed width for the resized texture.</param>
		/// <returns>A new Texture2D with the resized dimensions.</returns>
		public static Texture2D ResizeToMaxWidth(Texture2D source, int maxWidth) {
			Texture2D CopyTexture(Texture2D src) {
				Texture2D copy = new Texture2D(src.width, src.height, src.format, src.mipmapCount > 1);
				Graphics.CopyTexture(src, copy);
				return copy;
			}

			// If the source texture is already within the limit, return a copy or just return source.
			if (source.width <= maxWidth) {
				// Optionally, just return the existing texture.
				// Or return a copy if you prefer not to mutate the original reference.
				return CopyTexture(source);
			}

			// Calculate new dimensions
			float aspectRatio = (float)source.height / source.width;
			int newWidth = maxWidth;
			int newHeight = Mathf.RoundToInt(newWidth * aspectRatio);

			// Create a temporary RenderTexture to do the scaling.
			RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
			rt.filterMode = FilterMode.Bilinear;

			// Blit the source texture into the RenderTexture, automatically scaling it to the new size.
			Graphics.Blit(source, rt);

			// Create a new, readable Texture2D to copy the scaled result.
			Texture2D newTexture = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = rt;

			// Read back the RenderTexture into the new Texture2D.
			newTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
			newTexture.Apply();

			// Restore the previous RenderTexture and release the temporary one.
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(rt);

			return newTexture;
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


		private static void GenerateConstantsFromTemplates() {
            // runtime script paths
            string assetPathOfRuntimeTemplate = AssetDatabase.GUIDToAssetPath(Settings.instance.constantRuntimeTemplateGUID);
            string pathToRuntimeTemplate = Path.GetFullPath(assetPathOfRuntimeTemplate);

            string line = null;
            string constLineTemplate = null;
            string commentTemplate = null;
            string commentTemplateOriginal = null;
            string constLineOriginal = null;

            // go over every line of the runtime template
            StringBuilder runtimeFileContent = new StringBuilder();
            using (StreamReader file = new StreamReader(pathToRuntimeTemplate)) {
                while ((line = file.ReadLine()) != null) {
                    if (constLineTemplate == null) {
                        if (line.Contains(CONSTANT_LINE)) {
                            constLineTemplate = line.Replace(CONSTANT_LINE, "");
                            constLineOriginal = line;
                        } else if (line.Contains(LOOKUP_ENTRY_LINE)) {
                            commentTemplate = line.Replace(LOOKUP_ENTRY_LINE, "");
                            commentTemplateOriginal = line;
                        }
                    }
                    runtimeFileContent.AppendLine(line);
                }
            }
            constLineTemplate = constLineTemplate.Replace(CONST_TYPE, "string");
            
			StringBuilder constLines = new StringBuilder();

			List<(string, string, string)> iconData = CreateIconData();
			if (iconData != null) {
                foreach ((string fileid, string name, string assetName) data in iconData) {

                    string validName = data.assetName
                        .Replace("/", "_")
                        .Replace("\\", "_")
                        .Replace(".", "_")
                        .Replace("-", "_")
                        .Replace(" ", "_")
                        .Replace("@", "_");

					if (char.IsDigit(validName[0])) {
                        validName = "_"+validName;
                    }

                    constLines.AppendLine(commentTemplate.Replace(CONST_COMMENT, $"EditorGUIUtility.IconContent({Settings.instance.constantsClassName}.{validName})"));
                    constLines.AppendLine(constLineTemplate.Replace(CONST_VALUE, validName).Replace(CONST_KEY, $"\"{data.assetName}\"" ));
                }
            }

            // assign correct filename & replace constant template with the concrete data.
            runtimeFileContent = runtimeFileContent.Replace(CLASS_NAME_KEYS, Settings.instance.constantsClassName);
            runtimeFileContent = runtimeFileContent.Replace(NAMESPACE_RUNTIME, nameof(UnityEditorIconScraper));
            runtimeFileContent = runtimeFileContent.Replace(constLineOriginal, constLines.ToString());
            runtimeFileContent = runtimeFileContent.Replace(commentTemplateOriginal, "");

            string finalPath = Path.Combine(Path.GetFullPath(Settings.instance.constantsCodeGenPath), Settings.instance.constantsClassName + CSHARP_FILE_ENDING);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
            File.WriteAllText(finalPath, runtimeFileContent.ToString());
			AssetDatabase.Refresh();
        }
	}
}
