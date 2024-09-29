using KVD.Utils.DataStructures;
using Unity.Mathematics;

namespace KVD.Puppeteer.Data
{
	public struct BlendTreeData
	{
		// TODO: Clips and clipPositions are constant for BlendTreeAsset, so we can share them and just create blends per instance
		public Type type;
		public UnsafeArray<ushort> clips;
		public UnsafeArray<float2> clipPositions;
		public UnsafeArray<float> blends;

		public void Dispose()
		{
			clips.Dispose();
			clipPositions.Dispose();
			blends.Dispose();
		}

		public enum Type : byte
		{
			Space1D,
			Space2DTriangulated,
			Space2DCartesianGradiantBand,
			Space2DPolarGradiantBand
		}
	}
}
