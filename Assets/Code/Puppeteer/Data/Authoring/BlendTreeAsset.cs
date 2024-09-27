using System;
using KVD.Puppeteer.Managers;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace KVD.Puppeteer.Data.Authoring
{
	[CreateAssetMenu(fileName = "BlendTree", menuName = "Puppeteer/BlendTree", order = 0)]
	public class BlendTreeAsset : ScriptableObject
	{
		public BlendTreeData.Type type;
		public ClipData[] clips;

		public void ToBlendTreeData(Allocator allocator, out BlendTreeData blendTreeData)
		{
			var count = (uint)clips.Length;
			blendTreeData = default;
			blendTreeData.type = type;
			blendTreeData.clips = new UnsafeArray<ushort>(count, allocator);
			blendTreeData.clipPositions = new UnsafeArray<float2>(count, allocator);
			blendTreeData.blends = new UnsafeArray<float>(count, allocator);

			var clipsManager = ClipsManager.Instance;
			for (var i = 0; i < count; i++)
			{
				blendTreeData.clips[i] = clipsManager.RegisterClip(clips[i].clipAsset);
				blendTreeData.clipPositions[i] = clips[i].position;
				blendTreeData.blends[i] = 0;
			}
		}

		[Serializable]
		public struct ClipData
		{
			public AnimationClipAsset clipAsset;
			public float2 position;
		}
	}
}
