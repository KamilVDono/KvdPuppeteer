using KVD.Puppeteer.Managers;
using Unity.Collections;
using UnityEngine;

namespace KVD.Puppeteer.Data.Authoring
{
	public class SetAnimationTest : MonoBehaviour
	{
		[SerializeField] AnimationClipAsset _clip;
		[SerializeField] BlendTreeAsset _blendTree;
		[SerializeField] Puppet _puppet;

		uint _stateId = uint.MaxValue;

		void OnEnable()
		{
			_puppet.AfterRegistration(StartAnimation);
		}

		void StartAnimation()
		{
			if (_clip)
			{
				var clipIndex = ClipsManager.Instance.RegisterClip(_clip);
				_stateId = StatesManager.Instance.AddClipState(_puppet.Slot, clipIndex);
			}
			else
			{
				_blendTree.ToBlendTreeData(Allocator.Persistent, out var blendTreeData);
				var blendTreeIndex = BlendTreesManager.Instance.AddBlendTree(blendTreeData);
				_stateId = StatesManager.Instance.AddBlendTreeState(_puppet.Slot, blendTreeIndex);
			}
			TransitionsManager.Instance.StartTransition(_puppet.Slot, _stateId, 0);
		}

		void OnDisable()
		{
			StatesManager.Instance.RemoveState(_stateId, true);
			_stateId = uint.MaxValue;
		}
	}
}
