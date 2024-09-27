using KVD.Puppeteer.Data;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace KVD.Puppeteer.Managers
{
	public class ClipsManager
	{
		UnsafeBitmask _takenClips;
		UnsafeHashMap<int, RegisteredClip> _hashToClip;
		UnsafeList<int> _clipHashes;
		UnsafeList<AnimationClipData> _clipData;

		public static ClipsManager Instance { get; private set; }

		public UnsafeArray<AnimationClipData> ClipData => _clipData.AsUnsafeArray();

		public ClipsManager()
		{
			Instance = this;

			_takenClips = new UnsafeBitmask(128, Allocator.Domain);

			_hashToClip = new UnsafeHashMap<int, RegisteredClip>(164, Allocator.Domain);
			_clipHashes = new UnsafeList<int>(128, Allocator.Domain);
			_clipHashes.Length = 128;
			_clipData = new UnsafeList<AnimationClipData>(128, Allocator.Domain);
			_clipData.Length = 128;
		}

		public unsafe ushort RegisterClip(in SerializableGuid guid)
		{
			var hash = guid.GetHashCode();
			if (!_hashToClip.TryGetValue(hash, out var data))
			{
				// TODO: Check and resize if needed
				var clipIndex = (ushort)_takenClips.FirstZero();
				_takenClips.Up(clipIndex);

				data = new RegisteredClip
				{
					refCount = 0,
					clipIndex = clipIndex,
				};

				StreamingManager.Instance.LoadClip(guid, out var clip);
				_clipData[clipIndex] = clip;
				_clipHashes[clipIndex] = hash;
			}
			_hashToClip[hash] = data;
			return data.clipIndex;
		}

		public void UnregisterClip(in SerializableGuid guid)
		{
			var hash = guid.GetHashCode();
			var index = (ushort)_clipHashes.FindIndexOf(hash);
			UnregisterClip(index);
		}

		public void UnregisterClip(ushort clip)
		{
			var hash = _clipHashes[clip];
			UnregisterClip(hash);
		}

		public void UnregisterClip(int hash)
		{
			if (!_hashToClip.TryGetValue(hash, out var data))
			{
				return;
			}
			data.refCount--;
			if (data.refCount == 0)
			{
				_takenClips.Down(data.clipIndex);
				_clipData[data.clipIndex].Dispose();
				_clipData[data.clipIndex] = default;
				_clipHashes[data.clipIndex] = 0;
			}
			else
			{
				_hashToClip[hash] = data;
			}
		}

		struct RegisteredClip
		{
			public ushort refCount;
			public ushort clipIndex;
		}
	}
}
