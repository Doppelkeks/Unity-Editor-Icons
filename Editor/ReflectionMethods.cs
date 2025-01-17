using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityEditorIconScraper {
    /// <summary>
    /// This class gives us access to serveral hidden unity methods that are not publicly documented or accessible
    /// </summary>
    public static class ReflectionMethods {
        private static MethodInfo _getFileIDHintMethod;
        private static bool _fileIDReflectionAttempted = false;
        private static MethodInfo _getEditorAssetBundleMethod;
        private static bool _EditorAssetBundleReflectionAttempted = false;

        /// <summary>
        /// Reflective call into internal EditorGUIUtility.GetEditorAssetBundle() 
        /// which contains built-in icons. 
        /// (Unity Editor API usage: main thread only)
        /// </summary>
        public static AssetBundle GetEditorAssetBundle() {
            void TryInitializeReflection() {
                // 'Unsupported' is a class in UnityEditor, but it's internal / undocumented.
                Type editorGUIUtility = typeof(EditorGUIUtility);
                _getEditorAssetBundleMethod = editorGUIUtility.GetMethod(
                    "GetEditorAssetBundle",
                    BindingFlags.NonPublic | BindingFlags.Static);
            }

            if (!_EditorAssetBundleReflectionAttempted) {
                _EditorAssetBundleReflectionAttempted = true;
                TryInitializeReflection();
            }

            if (_getEditorAssetBundleMethod == null) {
                Debug.LogWarning("Could not find method 'GetEditorAssetBundle' via reflection.");
                return null;
            }

            try {
                return (AssetBundle)_getEditorAssetBundleMethod.Invoke(null, new object[] { });
            } catch (Exception ex) {
                Debug.LogError($"Reflection call to GetEditorAssetBundle failed: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Attempts to call 'Unsupported.GetFileIDHint(Object)'.
        /// Returns null if reflection fails or method is absent.
        /// </summary>
        public static string GetFileIDHint(UnityEngine.Object obj) {
            if (obj == null)
                return null;

            void TryInitializeReflection() {
                // 'Unsupported' is a class in UnityEditor, but it's internal / undocumented.
                Assembly editorAssembly = typeof(Editor).Assembly;
                Type unsupportedType = editorAssembly.GetType("UnityEditor.Unsupported", false);
                if (unsupportedType == null) {
                    Debug.LogWarning("Could not find UnityEditor.Unsupported type via reflection.");
                    return;
                }

                // Look for 'GetFileIDHint(Object obj)' (public or internal static)
                _getFileIDHintMethod = unsupportedType.GetMethod(
                    "GetFileIDHint",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new Type[] { typeof(UnityEngine.Object) },
                    null
                );

                if (_getFileIDHintMethod == null) {
                    Debug.LogWarning("Could not find method 'GetFileIDHint(Object)' via reflection.");
                }
            }

            if (!_fileIDReflectionAttempted) {
                _fileIDReflectionAttempted = true;
                TryInitializeReflection();
            }

            if (_getFileIDHintMethod == null) {
                Debug.LogWarning("Could not find method 'GetFileIDHint(Object)' via reflection.");
                return null;
            }

            try {
                object result = _getFileIDHintMethod.Invoke(null, new object[] { obj });
                return result.ToString();
            } catch (Exception ex) {
                Debug.LogError($"Reflection call to Unsupported.GetFileIDHint failed: {ex}");
                return null;
            }
        }

    }
}