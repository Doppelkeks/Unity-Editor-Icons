#if UNITY_6000_0_OR_NEWER

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditorIconScraper {

    public class SimpleSettingsProvider<T> : SettingsProvider where T : SimpleSettings<T> {
		
		private SerializedObject serializedData;
		private string headline;

		public SimpleSettingsProvider(string path, string headline=null, string[] keywords = null) : base(path, SettingsScope.Project) {
			this.headline = headline ?? typeof(T).Name;
			this.keywords = keywords ?? (new string[] { typeof(T).Name });
		}

		public override void OnActivate(string searchContext, VisualElement rootElement) {
			serializedData = new SerializedObject(SimpleSettings<T>.instance);
			CreateHeaderAndAddStyles(rootElement);

			// Create a scroll view with a inspector inside
			ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
			InspectorElement inspectorRoot = new InspectorElement();

			scrollView.Add(inspectorRoot);
			rootElement.Add(scrollView);

			// Bind a callback to save changes
			// Binding it to the root is enough
			rootElement.RegisterCallback<SerializedPropertyChangeEvent>(ValueChanged);

			// Populate the properties
			inspectorRoot.Bind(serializedData);
		}

		protected virtual void CreateHeaderAndAddStyles(VisualElement root) {
			// create and add label
			Label title = new Label(headline);
			title.style.fontSize = 20;
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.marginBottom = 10;
			title.AddToClassList(nameof(title));
			title.style.paddingTop = title.style.paddingRight = 2;
			title.style.paddingBottom = 0;
			title.style.paddingLeft = 5;

			root.Add(title);
		}

		protected virtual void ValueChanged(SerializedPropertyChangeEvent evt) {
			(serializedData.targetObject as T).Save();
		}
	}
}
#endif