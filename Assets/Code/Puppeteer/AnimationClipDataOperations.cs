using AclUnity;
using KVD.Puppeteer.Data;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace KVD.Puppeteer
{
	public static class AnimationClipDataOperations
	{
		public static unsafe void Sample(this in AnimationClipData clip, NativeArray<Qvvs> bonesBuffer, float time, float blending, bool isFirst)
		{
			Sample(clip, (Qvvs*)bonesBuffer.GetUnsafePtr(), (uint)bonesBuffer.Length, time, blending, isFirst);
		}

		public static unsafe void SampleFirst(this in AnimationClipData clip, NativeArray<Qvvs> bonesBuffer, float time, float blending)
		{
			SampleFirst(clip, (Qvvs*)bonesBuffer.GetUnsafePtr(), (uint)bonesBuffer.Length, time, blending);
		}

		public static unsafe void SampleAdd(this in AnimationClipData clip, NativeArray<Qvvs> bonesBuffer, float time, float blending)
		{
			SampleAdd(clip, (Qvvs*)bonesBuffer.GetUnsafePtr(), (uint)bonesBuffer.Length, time, blending);
		}

		public static unsafe void Sample(this in AnimationClipData clip, in Skeleton skeleton, float time, float blending, bool isFirst)
		{
			Sample(clip, skeleton.localBones.Ptr, skeleton.localBones.Length, time, blending, isFirst);
		}

		public static unsafe void SampleFirst(this in AnimationClipData clip, in Skeleton skeleton, float time, float blending)
		{
			SampleFirst(clip, skeleton.localBones.Ptr, skeleton.localBones.Length, time, blending);
		}

		public static unsafe void SampleAdd(this in AnimationClipData clip, in Skeleton skeleton, float time, float blending)
		{
			SampleAdd(clip, skeleton.localBones.Ptr, skeleton.localBones.Length, time, blending);
		}

		public static unsafe void Sample(this in AnimationClipData clip, Qvvs* bonesBuffer, uint bonesBufferCount, float time, float blending, bool isFirst)
		{
			if (isFirst)
			{
				SampleFirst(clip, bonesBuffer, bonesBufferCount, time, blending);
			}
			else
			{
				SampleAdd(clip, bonesBuffer, bonesBufferCount, time, blending);
			}
		}

		public static unsafe void SampleFirst(this in AnimationClipData clip, Qvvs* bonesBuffer, uint bonesBufferCount, float time, float blending)
		{
			Decompression.SamplePoseBlendedFirst(clip.data.Ptr, bonesBuffer, (int)bonesBufferCount, blending, time, Decompression.KeyframeInterpolationMode.Interpolate);
		}

		public static unsafe void SampleAdd(this in AnimationClipData clip, Qvvs* bonesBuffer, uint bonesBufferCount, float time, float blending)
		{
			Decompression.SamplePoseBlendedAdd(clip.data.Ptr, bonesBuffer, (int)bonesBufferCount, blending, time, Decompression.KeyframeInterpolationMode.Interpolate);
		}

		public static float LoopToClipTime(this in AnimationClipData clip, float time)
		{
			var clipDuration = clip.Duration;
			var wrappedTime = math.fmod(time, clipDuration);
			wrappedTime += math.select(0f, clipDuration, wrappedTime < 0f);
			return wrappedTime;
		}
	}
}
