using KVD.Puppeteer.Data.Authoring;
using UnityEngine;

namespace KVD.Puppeteer
{
	public class TransitionsPreview : MonoBehaviour
	{
		[SerializeField] AnimationClipAsset _clip;
		[SerializeField, Range(0.001f, 5f)] float _duration = 1f;

		public readonly struct EditorAccess
		{
			readonly TransitionsPreview _target;

			public ref AnimationClipAsset Clip => ref _target._clip;
			public ref float Duration => ref _target._duration;

			public EditorAccess(TransitionsPreview target)
			{
				_target = target;
			}
		}
	}
}
