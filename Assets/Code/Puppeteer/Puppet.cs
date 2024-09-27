using KVD.Puppeteer.Managers;
using UnityEngine;

namespace KVD.Puppeteer
{
	[ExecuteAlways]
	public class Puppet : MonoBehaviour
	{
		[SerializeField] VirtualBones _bones;
		uint _slot = unchecked((uint)-1);

		public uint Slot => _slot;

		void OnEnable()
		{
			if (!_bones)
			{
				return;
			}

			_bones.EnsureInitialized();

			_slot = PuppeteerManager.Instance.RegisterPuppet(_bones);
		}

		void OnDisable()
		{
			if (_slot == unchecked((uint)-1))
			{
				return;
			}
			PuppeteerManager.Instance.UnregisterPuppet(_slot);
		}

		public readonly struct EditorAccess
		{
			readonly Puppet _player;

			public ref VirtualBones Bones => ref _player._bones;
			public ref uint Slot => ref _player._slot;

			public EditorAccess(Puppet player)
			{
				_player = player;
			}
		}
	}
}