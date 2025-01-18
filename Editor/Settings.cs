using UnityEditor;
using UnityEngine;

namespace UnityEditorIconScraper {
    /// <summary>
    /// Holds user-configurable settings for the Unity Editor Icon Scraper tool.
    /// Stored in the ProjectSettings/UnityEditorIconScraper.asset file.
    /// </summary>
    [FilePath("ProjectSettings/" + nameof(UnityEditorIconScraper) + ".asset", FilePathAttribute.Location.ProjectFolder)]
    public class Settings : SimpleSettings<Settings> {
        /// <summary>
        /// The display path in the Project Settings window.
        /// </summary>
        public const string WINDOW_PATH = "Tools/" + nameof(UnityEditorIconScraper);

        /// <summary>
        /// Maximum pixel width of small icons for the readme generation.
        /// </summary>
        [Header("General"), Tooltip("Maximum pixel width of small icons for the readme generation.")]
        public int maxWidthForReadmeIcons = 128;

        /// <summary>
        /// The folder path where exported (original/full-size) icons will be written.
        /// </summary>
        [Header("Paths"), Tooltip("Path where original icons will be stored (relative to the project root).")]
        public string originalIconsOutputPath = "Icons~/Original";
 
        /// <summary>
        /// The folder path where smaller icons (for README or other purposes) will be written.
        /// </summary>
        [Tooltip("Path where smaller icons will be stored (relative to the project root).")]
        public string smallIconsOutputPath = "Icons~/Small";

        /// <summary>
        /// The folder path (or file path) where the README will be generated.
        /// </summary>
        [Tooltip("Path (folder or full file name) where the README.md will be placed.")]
        public string readmeOutputPath = "Icons~/";

        /// <summary>
        /// The folder path where the generated constants should be placed.
        /// </summary>
        [Tooltip("The folder path where the generated constants should be placed.")]
        public string constantsCodeGenPath = "Assets/Scripts";

        /// <summary>
        /// Arguments passed to pngquant.exe for processing/compressing PNG files.
        /// </summary>
        [Header("PNGQuant (compress icons)"), Tooltip("Command-line arguments for pngquant (e.g., --force, --quality=10-60, etc.).")]
        public string pngquantArguments = "--force 128 --quality=10-60 --ext .png *.png *\\*.png *\\*\\*.png *\\*\\*\\*.png *\\*\\*\\*\\*.png";
         
        /// <summary>
        /// Absolute or relative path to pngquant.exe.
        /// If it's on your PATH, "pngquant.exe" might suffice.
        /// Otherwise, specify a full path, e.g. "C:/Tools/pngquant.exe".
        /// </summary>
        [Tooltip("Absolute or relative path to pngquant.exe. If it's on your PATH, \"pngquant.exe\" might suffice. Otherwise, specify a full path, e.g. \"C:/Tools/pngquant.exe\". ")]
        public string pngquantExePath = "pngquant.exe";

        /// <summary>
        /// GUID referencing the main README template asset in the Unity project.
        /// </summary>
        [Header("File Templates"), Tooltip("GUID of the README template file in the project (e.g. a .txt or .md file).")]
        public string readmeTemplateGUID = "9ffe4f209f5676c4db63c0fa71a267ad";

        /// <summary>
        /// GUID referencing the icon partial template asset used for inserting icon details.
        /// </summary>
        [Tooltip("GUID of the partial template file for inserting icon lines in the README.")]
        public string iconPartialTemplateGUID = "3cc672dcb0cd71f43a276c3b10b8fab6";

        /// <summary>
        /// GUID referencing the constant csharp template
        /// </summary>
        [Tooltip("GUID of the partial template file for inserting icon lines in the README.")]
        public string constantRuntimeTemplateGUID = "7c9c59198cf6db141823d2a2454cde60";


        /// <summary>
        /// Name of the constants class
        /// </summary>
        [Header("Code Generator"), Tooltip("Name of the constants class")]
        public string constantsClassName = "EditorIcons";

        /// <summary>
        /// Registers this settings object in the Project Settings window under 'Tools/UnityEditorIconScraper'.
        /// </summary>
        /// <returns>A new <see cref="SettingsProvider"/> instance.</returns>
        [SettingsProvider]
        private static SettingsProvider RegisterInProjectSettings() {
            return new SimpleSettingsProvider<Settings>(WINDOW_PATH);
        }
    }
}