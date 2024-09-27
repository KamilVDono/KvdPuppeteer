using AclUnity;
using KVD.Puppeteer.Data.Authoring;
using KVD.Puppeteer.Managers;
using UnityEditor;
using UnityEngine;

namespace KVD.Puppeteer.Editor
{
	[CustomEditor(typeof(AnimationClipAsset))]
	public class AnimationClipAssetEditor : UnityEditor.Editor
	{
		ushort _clipIndex = ushort.MaxValue;

		void OnEnable()
		{
			_clipIndex = ClipsManager.Instance.RegisterClip((AnimationClipAsset)target);
		}

		void OnDisable()
		{
			if (_clipIndex != ushort.MaxValue)
			{
				ClipsManager.Instance.UnregisterClip(_clipIndex);
			}
			_clipIndex = ushort.MaxValue;
		}

		public override unsafe void OnInspectorGUI()
		{
			if (_clipIndex == ushort.MaxValue)
			{
				OnEnable();
			}

			var clipData = ClipsManager.Instance.ClipData[_clipIndex];
			var clipHeader = ClipHeader.Read(clipData.data.Ptr);

			var bytesSize = clipData.data.Length;
			GUILayout.Label($"GUID: {target}");
			GUILayout.Label($"Size: {bytesSize} bytes");
			GUILayout.Label($"Clip type: {clipHeader.clipType}");
			GUILayout.Label($"Track count: {clipHeader.trackCount}");
			GUILayout.Label($"Sample rate: {clipHeader.sampleRate}");
			GUILayout.Label($"Duration: {clipHeader.duration}");
		}
	}
}
