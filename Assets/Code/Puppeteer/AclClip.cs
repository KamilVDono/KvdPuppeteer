using AclUnity;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace KVD.Puppeteer
{
	[CreateAssetMenu(fileName = "Acl clip", menuName = "Animations/Acl clip", order = 0)]
	[PreferBinarySerialization]
	public unsafe class AclClip : ScriptableObject
	{
		public byte[] aclData;

		public int TransformsCount
		{
			get
			{
				fixed (byte* byteDataPtr = aclData)
				{
					return ClipHeader.Read(byteDataPtr).trackCount;
				}
			}
		}

		public float Duration
		{
			get
			{
				fixed (byte* byteDataPtr = aclData)
				{
					return ClipHeader.Read(byteDataPtr).duration;
				}
			}
		}

		public void Sample(NativeArray<Qvvs> bonesBuffer, float time, float blending, bool isFirst)
		{
			fixed (byte* byteDataPtr = aclData)
			{
				if (isFirst)
				{
					Decompression.SamplePoseBlendedFirst(byteDataPtr, bonesBuffer, blending, time, Decompression.KeyframeInterpolationMode.Interpolate);
				}
				else
				{
					Decompression.SamplePoseBlendedAdd(byteDataPtr, bonesBuffer, blending, time, Decompression.KeyframeInterpolationMode.Interpolate);
				}
			}
		}

		public float LoopToClipTime(float time)
		{
			var clipDuration = Duration;
			var wrappedTime = math.fmod(time, clipDuration);
			wrappedTime += math.select(0f, clipDuration, wrappedTime < 0f);
			return wrappedTime;
		}
	}
}