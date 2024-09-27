using System;
using System.Collections.Generic;
using KVD.Puppeteer.Data.Authoring;
using KVD.Puppeteer.Managers;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace KVD.Puppeteer
{
	[ExecuteInEditMode]
	public class BlendTreePreview : MonoBehaviour
	{
		[SerializeField, Range(-1f, 1f)] float _x;
		[SerializeField, Range(-1f, 1f)] float _y;

		[SerializeField] BlendTreeAsset _blendTreeAsset;

		[NonSerialized] bool _isRunning;
		uint _slot;
		ushort _blendTreeId;
		uint _stateId;

		void OnDisable()
		{
			StopRunning();
		}

		bool StartRunning()
		{
			if (_isRunning)
			{
				return true;
			}

			if (!_blendTreeAsset)
			{
				return false;
			}

			var puppet = GetComponent<Puppet>();
			if (!puppet)
			{
				return false;
			}
			_slot = puppet.Slot;

			_blendTreeAsset.ToBlendTreeData(Allocator.Persistent, out var blendTreeData);
			_blendTreeId = BlendTreesManager.Instance.AddBlendTree(blendTreeData);
			_stateId = StatesManager.Instance.AddBlendTreeState(_slot, _blendTreeId);
			TransitionsManager.Instance.UnregisterPuppet(_slot);
			TransitionsManager.Instance.StartTransition(_slot, _stateId, 0);

			_isRunning = true;
			Update();

			return true;
		}

		void StopRunning()
		{
			if (!_isRunning)
			{
				return;
			}

			_isRunning = false;

			StatesManager.Instance.RemoveState(_stateId);
		}

		void Update()
		{
			if (!_isRunning)
			{
				return;
			}

			BlendTreesManager.Instance.UpdateParameters(_blendTreeId, new float2(_x, _y));
		}

		public readonly struct EditorAccess
		{
			readonly BlendTreePreview _instance;

			public ref readonly bool IsRunning => ref _instance._isRunning;
			public uint Slot => _instance._slot;
			public ushort BlendTreeId => _instance._blendTreeId;
			public uint StateId => _instance._stateId;

			public EditorAccess(BlendTreePreview instance)
			{
				_instance = instance;
			}

			public void StartRunning()
			{
				_instance.StartRunning();
			}

			public void StopRunning()
			{
				_instance.StopRunning();
			}
		}
	}
}
