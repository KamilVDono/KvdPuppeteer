using AclUnity;
using KVD.Utils.DataStructures;
using Unity.Mathematics;

namespace KVD.Puppeteer.Data
{
	public struct Skeleton
	{
		public UnsafeArray<Qvvs> localBones;
		public UnsafeArray<float3x4> localToWorlds;

		public bool IsCreated => localBones.IsCreated;

		public void Dispose()
		{
			localBones.Dispose();
			localToWorlds.Dispose();
		}
	}
}
