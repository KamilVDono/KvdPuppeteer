using KVD.Puppeteer.Managers;
using UnityEditor;
using UnityEngine;

namespace KVD.Puppeteer.Editor
{
	[CustomEditor(typeof(BlendTreePreview))]
	public class BlendTreePreviewEditor : UnityEditor.Editor
	{
		SerializedProperty _xProperty;
		SerializedProperty _yProperty;
		SerializedProperty _blendTreeAssetProperty;

		void OnEnable()
		{
			_xProperty = serializedObject.FindProperty("_x");
			_yProperty = serializedObject.FindProperty("_y");
			_blendTreeAssetProperty = serializedObject.FindProperty("_blendTreeAsset");
		}

		public override void OnInspectorGUI()
		{
			var access = new BlendTreePreview.EditorAccess((BlendTreePreview)target);

			serializedObject.Update();

			using (new EditorGUI.DisabledScope(access.IsRunning))
			{
				EditorGUILayout.PropertyField(_blendTreeAssetProperty, true);
			}

			EditorGUILayout.PropertyField(_xProperty);
			EditorGUILayout.PropertyField(_yProperty);

			if (access.IsRunning)
			{
				using (new EditorGUI.DisabledScope(true))
				{
					var asset = access.BlendTreeAsset;
					var clips = asset.clips;
					var blendTree = BlendTreesManager.Instance.BlendTrees[access.BlendTreeId];
					var blends = blendTree.blends;
					for (var i = 0; i < blends.Length; i++)
					{
						EditorGUILayout.Slider(clips[i].clipAsset.name, blends[i], 0f, 1f);
					}
				}
			}

			if (!access.IsRunning && GUILayout.Button("Start"))
			{
				access.StartRunning();
			}
			if (access.IsRunning && GUILayout.Button("Stop"))
			{
				access.StopRunning();
			}

			serializedObject.ApplyModifiedProperties();
		}
	}
}
