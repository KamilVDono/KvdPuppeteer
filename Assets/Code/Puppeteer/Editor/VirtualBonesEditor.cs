using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AclUnity;
using KVD.Puppeteer.Data;
using KVD.Puppeteer.Data.Authoring;
using KVD.Puppeteer.Managers;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using KVD.Utils.Maths;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace KVD.Puppeteer.Editor
{
	[CustomEditor(typeof(VirtualBones))]
	public class VirtualBonesEditor : UnityEditor.Editor
	{
		Transform _root;
		bool _expandedBones;
		bool[] _expandedBoneChildren;
		bool[] _editingBones;
		StringBuilder _sb = new StringBuilder();
		GUIStyle _labelStyle;

		bool HasFrameBounds()
		{
			var virtualBones = (VirtualBones)target;
			return virtualBones.IsValid;
		}

		Bounds OnGetFrameBounds()
		{
			var virtualBones = (VirtualBones)target;

			var localToWorlds = virtualBones.Skeleton.localToWorlds;

			var bounds = new Bounds(localToWorlds[0].extractPosition(), Vector3.zero);
			for (var i = 1; i < localToWorlds.Length; i++)
			{
				var localToWorld = localToWorlds[i];
				var worldPosition = localToWorld.extractPosition();
				bounds.Encapsulate(worldPosition);
			}

			return bounds;
		}

		public override void OnInspectorGUI()
		{
			var virtualBones = (VirtualBones)target;
			if (virtualBones.skeletonAsset)
			{
				DrawInspector(virtualBones);
			}
			else
			{
				DrawGenerate(virtualBones);
			}
		}

		void DrawInspector(VirtualBones virtualBones)
		{
			base.OnInspectorGUI();

			if (!virtualBones.IsValid)
			{
				GUILayout.Label("Invalid state");
				return;
			}

			_expandedBones = EditorGUILayout.Foldout(_expandedBones, "Bones");
			if (_expandedBones)
			{
				DrawBones(virtualBones);
			}

			if (GUILayout.Button("Reset to relax pose"))
			{
				var skeletonIndex = virtualBones.SkeletonIndex;
				var skeleton = SkeletonsManager.Instance.Skeletons[skeletonIndex];
				var sharedIndex = SkeletonsManager.Instance.SharedSkeletonIndices[skeletonIndex];
				var relaxClipIndex = SkeletonsManager.Instance.RelaxClipIndices[sharedIndex];
				var relaxClip = ClipsManager.Instance.ClipData[relaxClipIndex];

				relaxClip.SampleFirst(skeleton, 0, 1);
			}
		}

		void DrawBones(VirtualBones virtualBones)
		{
			if (!virtualBones.IsValid)
			{
				return;
			}

			_labelStyle ??= new GUIStyle(EditorStyles.label)
			{
				richText = true,
				wordWrap = true,
			};

			var skeletonIndex = virtualBones.SkeletonIndex;
			var sharedIndex = SkeletonsManager.Instance.SharedSkeletonIndices[skeletonIndex];
			var sharedSkeleton = SkeletonsManager.Instance.SharedSkeletons[sharedIndex];
			var boneNames = sharedSkeleton.boneNames;
			var bones = virtualBones.Skeleton.localBones;
			var localToWorlds = virtualBones.Skeleton.localToWorlds;

			_expandedBoneChildren ??= new bool[bones.Length];
			_editingBones ??= new bool[bones.Length];

			++EditorGUI.indentLevel;
			for (var i = 0; i < bones.Length; i++)
			{
				GUILayout.BeginHorizontal();
				_expandedBoneChildren[i] = EditorGUILayout.Foldout(_expandedBoneChildren[i], boneNames[i].ToString(), true);
				_editingBones[i] = GUILayout.Toggle(_editingBones[i], "Edit", GUILayout.Width(50));
				GUILayout.EndHorizontal();

				if (!_expandedBoneChildren[i])
				{
					continue;
				}

				++EditorGUI.indentLevel;
				var bone = bones[i];
				var localToWorld = localToWorlds[i];

				var worldPosition = localToWorld.extractPosition();
				var worldRotation = localToWorld.extractRotation();
				var worldScale = localToWorld.extractScale();

				_sb.Append("Local:\n\t");
				_sb.AppendPosition(bone.position);
				_sb.Append("\n\t");
				_sb.AppendRotation(bone.rotation);
				_sb.Append("\n\t");
				_sb.AppendScale(bone.stretch);
				_sb.Append("\nWorld:\n\t");
				_sb.AppendPosition(worldPosition);
				_sb.Append("\n\t");
				_sb.AppendRotation(worldRotation);
				_sb.Append("\n\t");
				_sb.AppendScale(worldScale);

				EditorGUILayout.LabelField(_sb.ToString(), _labelStyle);
				_sb.Clear();
				--EditorGUI.indentLevel;
			}
			--EditorGUI.indentLevel;
		}

		unsafe void DrawGenerate(VirtualBones virtualBones)
		{
			_root = EditorGUILayout.ObjectField("Root", _root, typeof(Transform), true) as Transform;
			if (_root && GUILayout.Button("Generate"))
			{
				var basePath = Application.dataPath;
				if (PrefabUtility.IsPartOfPrefabInstance(_root))
				{
					basePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_root);
				}
				var assetPath = EditorUtility.SaveFilePanelInProject(
					"Save skeleton",
					virtualBones.name,
					"asset",
					"KUPA",
					basePath);

				if (assetPath.Length == 0)
				{
					return;
				}

				var breadthQueue = new Queue<(Transform, sbyte)>();
				var boneNames = new List<FixedString32Bytes>();
				var parentIndices = new List<sbyte>();
				var relaxPosesList = new UnsafeList<Qvvs>(_root.childCount, Allocator.Temp);

				breadthQueue.Enqueue((_root, -1));
				while (breadthQueue.Count > 0)
				{
					var (bone, parentIndex) = breadthQueue.Dequeue();
					boneNames.Add(bone.name.ToFixedString32());
					relaxPosesList.Add(new Qvvs(bone.localPosition, bone.localRotation, 1f, bone.localScale));
					parentIndices.Add(parentIndex);
					parentIndex = (sbyte)(boneNames.Count - 1);

					for (var i = 0; i < bone.childCount; i++)
					{
						var child = bone.GetChild(i);
						breadthQueue.Enqueue((child, parentIndex));
					}
				}

				var sharedSkeleton = new SharedSkeleton()
				{
					boneNames = boneNames.ToUnsafeArray(Allocator.Temp),
					parentIndices = parentIndices.ToUnsafeArray(Allocator.Temp),
				};
				var skeletonAsset = ScriptableObject.CreateInstance<SkeletonAsset>();
				skeletonAsset.guid = SerializableGuid.NewGuid();
				skeletonAsset.name = virtualBones.name;
				AssetDatabase.CreateAsset(skeletonAsset, assetPath);

				var skeletonPath = StreamingManager.SkeletonPath(skeletonAsset);

				var skeletonDirectory = Directory.GetParent(skeletonPath);
				if (!skeletonDirectory.Exists)
				{
					skeletonDirectory.Create();
				}

				var stream = new FileStream(skeletonPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
				stream.Write(new ReadOnlySpan<byte>(sharedSkeleton.boneNames.Ptr, sizeof(FixedString32Bytes) * sharedSkeleton.boneNames.LengthInt));
				stream.Write(new ReadOnlySpan<byte>(sharedSkeleton.parentIndices.Ptr, sizeof(sbyte) * sharedSkeleton.parentIndices.LengthInt));
				stream.Flush();
				stream.Close();

				CreateClipWizard.CreateRelaxPoseClip(skeletonAsset, relaxPosesList, sharedSkeleton);
				relaxPosesList.Dispose();

				sharedSkeleton.Dispose();

				virtualBones.skeletonAsset = AssetDatabase.LoadAssetAtPath<SkeletonAsset>(assetPath);
				serializedObject.Update();
			}
		}

		void OnSceneGUI()
		{
			var virtualBones = (VirtualBones)target;

			if (_editingBones == null)
			{
				return;
			}

			if (!virtualBones.IsValid)
			{
				return;
			}

			var bones = virtualBones.Skeleton.localBones;
			var localToWorlds = virtualBones.Skeleton.localToWorlds;
			var parentIndices = virtualBones.SharedSkeleton.parentIndices;

			if (_editingBones.Length != bones.Length)
			{
				return;
			}

			var originalMatrix = Handles.matrix;
			for (var i = 0; i < bones.Length; i++)
			{
				if (!_editingBones[i])
				{
					continue;
				}
				ref var bone = ref bones[i];

				var localPosition = (Vector3)bone.position;
				var localRotation = (Quaternion)bone.rotation;
				var localScale = (Vector3)bone.stretch;

				var parentIndex = parentIndices[i];
				var parentMatrix = parentIndex >= 0 ? (Matrix4x4)localToWorlds[parentIndex].toFloat4x4() : virtualBones.transform.localToWorldMatrix;

				Handles.matrix = parentMatrix;
				EditorGUI.BeginChangeCheck();
				Handles.TransformHandle(ref localPosition, ref localRotation, ref localScale);
				if (EditorGUI.EndChangeCheck())
				{
					bone.position = localPosition;
					bone.rotation = localRotation;
				}
			}
			Handles.matrix = originalMatrix;
		}
	}
}
