using System.IO;
using KVD.Puppeteer.Data;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;

namespace KVD.Puppeteer.Managers
{
	public class StreamingManager
	{
		public const string ClipExtension = "clip";
		public const string SkeletonExtension = "skl";

		public static StreamingManager Instance { get; private set; }

		public StreamingManager()
		{
			Instance = this;
		}

		public unsafe bool LoadClip(in SerializableGuid guid, out AnimationClipData clip)
		{
			var path = ClipPath(guid);
			var fileInfo = new FileInfoResult();
			AsyncReadManager.GetFileInfo(path, &fileInfo).JobHandle.Complete();
			if (fileInfo.FileState != FileState.Exists)
			{
				clip = default;
				return false;
			}

			clip = new AnimationClipData
			{
				data = new UnsafeArray<byte>((uint)fileInfo.FileSize, Allocator.Persistent),
			};
			var readCommand = new ReadCommand
			{
				Offset = 0,
				Size = clip.data.Length,
				Buffer = clip.data.Ptr,
			};
			AsyncReadManager.Read(path, &readCommand, 1).JobHandle.Complete();

			return true;
		}

		public unsafe bool LoadSkeleton(in SerializableGuid guid, out SharedSkeleton sharedSkeleton)
		{
			var path = SkeletonPath(guid);
			var fileInfo = new FileInfoResult();
			AsyncReadManager.GetFileInfo(path, &fileInfo).JobHandle.Complete();
			if (fileInfo.FileState != FileState.Exists)
			{
				sharedSkeleton = default;
				return false;
			}

			var skeletonAoSSize = SharedSkeleton.AoSStride;
			var bonesCount = (uint)(fileInfo.FileSize / skeletonAoSSize);

			sharedSkeleton = new SharedSkeleton
			{
				boneNames = new UnsafeArray<FixedString32Bytes>(bonesCount, Allocator.Persistent),
				parentIndices = new UnsafeArray<sbyte>(bonesCount, Allocator.Persistent),
			};

			var readBuffer = UnsafeUtility.Malloc(fileInfo.FileSize, UnsafeUtility.AlignOf<byte>(), Allocator.Temp);
			var readCommand = new ReadCommand
			{
				Offset = 0,
				Size = fileInfo.FileSize,
				Buffer = readBuffer,
			};
			AsyncReadManager.Read(path, &readCommand, 1).JobHandle.Complete();

			var readBufferPtr = (byte*)readBuffer;

			var srcNamesPtr = (FixedString32Bytes*)readBufferPtr;
			var dstNamesPtr = sharedSkeleton.boneNames.Ptr;
			UnsafeUtility.MemCpy(dstNamesPtr, srcNamesPtr, bonesCount * UnsafeUtility.SizeOf<FixedString32Bytes>());

			var srcParentIndicesPtr = (sbyte*)(srcNamesPtr + bonesCount);
			var dstParentIndicesPtr = sharedSkeleton.parentIndices.Ptr;
			UnsafeUtility.MemCpy(dstParentIndicesPtr, srcParentIndicesPtr, bonesCount * UnsafeUtility.SizeOf<sbyte>());

			UnsafeUtility.Free(readBuffer, Allocator.Temp);

			return true;
		}

		public static string ClipPath(in SerializableGuid guid)
		{
			return Path.Combine(Application.streamingAssetsPath, $"Clips/{guid}.b{ClipExtension}");
		}

		public static string SkeletonPath(in SerializableGuid guid)
		{
			return Path.Combine(Application.streamingAssetsPath, $"Skeletons/{guid}.b{SkeletonExtension}");
		}
	}
}
