using System.Collections.Generic;
using AclUnity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Jobs;

namespace KVD.Puppeteer.Editor
{
	public unsafe class CreateClipWizard : ScriptableWizard
	{
		public Animator animator;
		public Transform root;
		public AnimationClip animation;

		[MenuItem("Tools/Create clip")]
		public static void CreateWizard()
		{
			DisplayWizard<CreateClipWizard>("Acl clip creator", "CREATE");
		}

		public void OnWizardCreate()
		{
			var parentIndices = new NativeList<short>(Allocator.Temp);
			parentIndices.Add(-1);
			var transformsCache = new List<Transform>();
			transformsCache.Add(animator.transform);

			var breadthQueue = new Queue<(Transform, int)>();

			breadthQueue.Enqueue((root, 0));
			while (breadthQueue.Count > 0)
			{
				var (bone, parentIndex) = breadthQueue.Dequeue();
				int currentIndex = parentIndices.Length;
				parentIndices.Add((short)parentIndex);
				transformsCache.Add(bone);

				for (int i = 0; i < bone.childCount; i++)
				{
					var child = bone.GetChild(i);
					breadthQueue.Enqueue((child, currentIndex));
				}
			}

			var transformAccessArray = new TransformAccessArray(transformsCache.ToArray());
			var requiredSamples = Mathf.CeilToInt(animation.frameRate*animation.length);
			var requiredTransforms = requiredSamples*transformAccessArray.length;

			var boneSamplesBuffer = new UnsafeList<Qvvs>(requiredTransforms, Allocator.Temp);
			boneSamplesBuffer.Length = requiredTransforms;

			SampleClip(ref boneSamplesBuffer, animator, animation, 0, transformAccessArray);

			var data = CreateACLData(animation.frameRate, boneSamplesBuffer, *parentIndices.GetUnsafeList());
			parentIndices.Dispose();
			boneSamplesBuffer.Dispose();

			var clip = CreateInstance<AclClip>();
			clip.aclData = data.ToArray();
			clip.name = animation.name;

			transformAccessArray.Dispose();

			AssetDatabase.CreateAsset(clip, $"Assets/{animation.name}.asset");
		}

		static void SampleClip(ref UnsafeList<Qvvs> boneTransforms, Animator animator, AnimationClip clip, int startIndex, TransformAccessArray transformAccessArray)
		{
			int requiredSamples = Mathf.CeilToInt(clip.frameRate*clip.length);

			var oldWrapMode = clip.wrapMode;
			clip.wrapMode = WrapMode.Clamp;

			float timestep = math.rcp(clip.frameRate);
			var job = new CaptureBoneSamplesJob
			{
				boneTransforms = boneTransforms,
				samplesPerBone = requiredSamples,
				currentSample = 0,
				startOffset = startIndex,
			};

			for (int i = 0; i < requiredSamples; i++)
			{
				clip.SampleAnimation(animator.gameObject, timestep*i);
				job.currentSample = i;
				job.RunReadOnlyByRef(transformAccessArray);
			}

			clip.wrapMode = oldWrapMode;
		}

		static unsafe NativeArray<byte> CreateACLData(float sampleRate, in UnsafeList<Qvvs> boneSamplesBuffer, UnsafeList<short> parentIndices)
		{
			var boneTransforms =
				CollectionHelper.ConvertExistingDataToNativeArray<AclUnity.Qvvs>(boneSamplesBuffer.Ptr,
					boneSamplesBuffer.Length, Allocator.None, true);

			// Step 1: Patch parent hierarchy for ACL
			var parents = new NativeArray<short>(parentIndices.Length, Allocator.Temp);
			for (short i = 0; i < parentIndices.Length; i++)
			{
				short index = parentIndices[i];
				if (index < 0)
					index = i;
				parents[i] = index;
			}

			// Step 2: Convert settings
			var aclSettings = new AclUnity.Compression.SkeletonCompressionSettings
			{
				compressionLevel = 100,
				maxDistanceError = 0.0001f,
				maxUniformScaleError = 0.00001f,
				sampledErrorDistanceFromBone = 0.03f
			};

			// Step 4: Compress
			var compressedClip = AclUnity.Compression.CompressSkeletonClip(parents, boneTransforms, sampleRate, aclSettings);

			// Step 5: Build blob clip
			var outputData = new NativeArray<byte>(compressedClip.sizeInBytes, Allocator.Persistent);
			compressedClip.CopyTo((byte*)outputData.GetUnsafePtr());

			// Step 6: Dispose ACL memory and safety
			compressedClip.Dispose();

			return outputData;
		}

		[BurstCompile]
		struct CaptureBoneSamplesJob : IJobParallelForTransform
		{
			public UnsafeList<Qvvs> boneTransforms;
			public int samplesPerBone;
			public int currentSample;
			public int startOffset;

			public void Execute(int index, TransformAccess transform)
			{
				int target = startOffset+index*samplesPerBone+currentSample;
				boneTransforms[target] = new Qvvs(transform.localPosition, transform.localRotation, 1f, transform.localScale);
			}
		}
	}
}