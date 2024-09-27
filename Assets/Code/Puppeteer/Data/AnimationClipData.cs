using AclUnity;
using KVD.Utils.DataStructures;

namespace KVD.Puppeteer.Data
{
	public unsafe struct AnimationClipData
	{
		public UnsafeArray<byte> data;

		public int TrackCount => ClipHeader.Read(data.Ptr).trackCount;

		public float Duration => ClipHeader.Read(data.Ptr).duration;

		public void Dispose()
		{
			data.Dispose();
		}
	}
}
