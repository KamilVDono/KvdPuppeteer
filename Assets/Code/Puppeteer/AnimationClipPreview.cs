using System;
using KVD.Puppeteer.Data.Authoring;
using KVD.Puppeteer.Managers;
using UnityEngine;

namespace KVD.Puppeteer
{
	[ExecuteInEditMode]
	public class AnimationClipPreview : MonoBehaviour
	{
		[SerializeField] AnimationClipAsset _clip;
		[SerializeField] VirtualBones _bones;
		[SerializeField] float _time;

		[NonSerialized] ushort _clipIndex = ushort.MaxValue;

		void OnEnable()
		{
			if (!_clip)
			{
				return;
			}
			_clipIndex = ClipsManager.Instance.RegisterClip(_clip);
		}

		void OnDisable()
		{
			if (_clipIndex != ushort.MaxValue)
			{
				ClipsManager.Instance.UnregisterClip(_clipIndex);
			}
			_clipIndex = ushort.MaxValue;
		}

		void Update()
		{
			if (_clipIndex == ushort.MaxValue || !_bones || !_bones.IsValid)
			{
				return;
			}

			_time += Time.deltaTime;
			var clip = ClipsManager.Instance.ClipData[_clipIndex];
			var sampleTime = clip.LoopToClipTime(_time);
			clip.Sample(_bones.Skeleton, sampleTime, 1, true);
		}
	}
}
