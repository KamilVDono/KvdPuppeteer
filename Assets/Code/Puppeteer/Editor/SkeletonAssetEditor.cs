using System.Text;
using AclUnity;
using Codice.Client.ChangeTrackerService;
using KVD.Puppeteer.Data.Authoring;
using KVD.Puppeteer.Managers;
using UnityEditor;
using UnityEngine;

namespace KVD.Puppeteer.Editor
{
	[CustomEditor(typeof(SkeletonAsset))]
	public class SkeletonAssetEditor : UnityEditor.Editor
	{
		uint _skeletonIndex = uint.MaxValue;
		StringBuilder _sb = new StringBuilder();

		void OnEnable()
		{
			_skeletonIndex = SkeletonsManager.Instance.RegisterSkeleton((SkeletonAsset)target);
		}

		void OnDisable()
		{
			if (_skeletonIndex != uint.MaxValue)
			{
				SkeletonsManager.Instance.UnregisterSkeleton(_skeletonIndex);
			}
			_skeletonIndex = uint.MaxValue;
		}

		public override unsafe void OnInspectorGUI()
		{
			if (_skeletonIndex == uint.MaxValue)
			{
				OnEnable();
			}

			var skeleton = SkeletonsManager.Instance.Skeletons[_skeletonIndex];
			var sharedSkeletonIndex = SkeletonsManager.Instance.SharedSkeletonIndices[_skeletonIndex];
			var sharedSkeleton = SkeletonsManager.Instance.SharedSkeletons[sharedSkeletonIndex];

			GUILayout.Label($"GUID: {target}");

			GUILayout.Label("Skeleton:", EditorStyles.boldLabel);
			GUILayout.Label($"Bone count: {skeleton.localBones.Length}");
			++EditorGUI.indentLevel;
			for (var i = 0; i < sharedSkeleton.boneNames.Length; i++)
			{
				_sb.Append(sharedSkeleton.boneNames[i].ToString());
				_sb.Append(" -> ");
				_sb.AppendPosition(skeleton.localBones[i].position);
				_sb.Append(" ");
				_sb.AppendRotation(skeleton.localBones[i].rotation);
				_sb.Append(" ");
				_sb.AppendScale(skeleton.localBones[i].scale);
				EditorGUILayout.LabelField(_sb.ToString());
				_sb.Clear();
			}
			--EditorGUI.indentLevel;

			// Clip
			var relaxPoseIndex = SkeletonsManager.Instance.RelaxClipIndices[sharedSkeletonIndex];
			var clipData = ClipsManager.Instance.ClipData[relaxPoseIndex];
			var clipHeader = ClipHeader.Read(clipData.data.Ptr);

			var bytesSize = clipData.data.Length;

			GUILayout.Label("Relax Pose:", EditorStyles.boldLabel);
			GUILayout.Label($"Size: {bytesSize} bytes");
			GUILayout.Label($"Clip type: {clipHeader.clipType}");
			GUILayout.Label($"Track count: {clipHeader.trackCount}");
			GUILayout.Label($"Sample rate: {clipHeader.sampleRate}");
			GUILayout.Label($"Duration: {clipHeader.duration}");
		}
	}
}
