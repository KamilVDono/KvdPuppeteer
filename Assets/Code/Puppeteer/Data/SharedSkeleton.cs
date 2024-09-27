using KVD.Utils.DataStructures;
using Unity.Collections;

namespace KVD.Puppeteer.Data
{
	public unsafe struct SharedSkeleton
	{
		public static int AoSStride => sizeof(FixedString32Bytes) + sizeof(sbyte);

		public UnsafeArray<FixedString32Bytes> boneNames;
		public UnsafeArray<sbyte> parentIndices;

		public bool IsCreated => boneNames.IsCreated;
		public uint BonesCount => boneNames.Length;

		public void Dispose()
		{
			boneNames.Dispose();
			parentIndices.Dispose();
		}
	}
}
