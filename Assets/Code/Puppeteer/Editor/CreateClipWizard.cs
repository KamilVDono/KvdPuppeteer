using System;
using System.Collections.Generic;
using System.IO;
using AclUnity;
using KVD.Puppeteer.Data;
using KVD.Puppeteer.Data.Authoring;
using KVD.Puppeteer.Managers;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
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
		public AnimationClip[] animations;
		public SkeletonAsset skeletonAsset;

		[MenuItem("Tools/Create clip")]
		public static void CreateWizard()
		{
			DisplayWizard<CreateClipWizard>("Acl clip creator", "CREATE");
		}

		public static void CreateRelaxPoseClip(SkeletonAsset skeletonAsset, in UnsafeList<Qvvs> relaxPose, in SharedSkeleton sharedSkeleton)
		{
			var parentIndices = sharedSkeleton.parentIndices.ToType<sbyte, short, ConvertSbyteToShort>(new ConvertSbyteToShort(), Allocator.Temp);

			var data = CreateAclData(1f, relaxPose.AsUnsafeArray(), parentIndices);
			parentIndices.Dispose();

			var clipPath = StreamingManager.ClipPath(skeletonAsset);
			var clipDirectory = Directory.GetParent(clipPath);
			if (!clipDirectory.Exists)
			{
				clipDirectory.Create();
			}

			var stream = new FileStream(clipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, data.Length);
			stream.Write(new ReadOnlySpan<byte>(data.GetUnsafePtr(), data.Length));
			stream.Flush();
			stream.Close();
		}

		public void OnWizardCreate()
		{
			if (skeletonAsset == null)
			{
				Debug.LogError("No skeleton asset provided");
				return;
			}

			var skeletonIndex = SkeletonsManager.Instance.RegisterSkeleton(skeletonAsset);
			var sharedSkeletonIndex = SkeletonsManager.Instance.SharedSkeletonIndices[skeletonIndex];
			var sharedSkeleton = SkeletonsManager.Instance.SharedSkeletons[sharedSkeletonIndex];

			var (shadowBones, shadowSamplingRoot) = CreateShadowHierarchy(skeletonIndex);

			var transformAccessArray = new TransformAccessArray(shadowBones);
			foreach (var animation in animations)
			{
				ProcessAnimation(animation, shadowSamplingRoot.gameObject, transformAccessArray, sharedSkeleton.parentIndices);
			}
			transformAccessArray.Dispose();

			SkeletonsManager.Instance.UnregisterSkeleton(skeletonIndex);
			DestroyImmediate(shadowSamplingRoot.gameObject);
		}

		(Transform[] shadowBones, Transform shadowSamplingRoot) CreateShadowHierarchy(uint skeletonIndex)
		{
			var sharedSkeletonIndex = SkeletonsManager.Instance.SharedSkeletonIndices[skeletonIndex];
			var sharedSkeleton = SkeletonsManager.Instance.SharedSkeletons[sharedSkeletonIndex];
			var shadowBones = new Transform[sharedSkeleton.BonesCount];
			var skeleton = SkeletonsManager.Instance.Skeletons[skeletonIndex];
			var relaxClipIndex = SkeletonsManager.Instance.RelaxClipIndices[sharedSkeletonIndex];
			var relaxClip = ClipsManager.Instance.ClipData[relaxClipIndex];
			relaxClip.SampleFirst(skeleton, 0f, 1f);

			var boneNames = sharedSkeleton.boneNames;
			var parentIndices = sharedSkeleton.parentIndices;
			for (var i = 0u; i < boneNames.Length; i++)
			{
				var boneName = boneNames[i].ToString();
				var parentIndex = parentIndices[i];

				var boneGo = new GameObject(boneName);
				boneGo.hideFlags = HideFlags.HideAndDontSave;
				shadowBones[i] = boneGo.transform;
				if (parentIndex >= 0)
				{
					shadowBones[i].SetParent(shadowBones[parentIndex]);
				}
				shadowBones[i].localPosition = skeleton.localBones[i].position;
				shadowBones[i].localRotation = skeleton.localBones[i].rotation;
				shadowBones[i].localScale = skeleton.localBones[i].stretch;
			}

			var samplingRoot = new GameObject("SamplingRoot");
			samplingRoot.hideFlags = HideFlags.HideAndDontSave;
			shadowBones[0].SetParent(samplingRoot.transform);

			return (shadowBones, samplingRoot.transform);
		}

		void ProcessAnimation(AnimationClip animation, GameObject sampleRoot, TransformAccessArray transformAccessArray, UnsafeArray<sbyte> parentIndices)
		{
			var requiredSamples = Mathf.CeilToInt(animation.frameRate*animation.length);
			var requiredTransforms = requiredSamples*transformAccessArray.length;

			var boneSamplesBuffer = new UnsafeArray<Qvvs>((uint)requiredTransforms, Allocator.Temp);

			SampleClip(ref boneSamplesBuffer, sampleRoot, animation, 0, transformAccessArray);

			var shortParentIndices = parentIndices.ToType<sbyte, short, ConvertSbyteToShort>(new ConvertSbyteToShort(), Allocator.Temp);
			var data = CreateAclData(animation.frameRate, boneSamplesBuffer, shortParentIndices);

			boneSamplesBuffer.Dispose();
			shortParentIndices.Dispose();

			SaveClipData(animation, data);
		}

		static void SampleClip(ref UnsafeArray<Qvvs> boneTransforms, GameObject sampleRoot, AnimationClip clip, int startIndex, TransformAccessArray transformAccessArray)
		{
			var requiredSamples = Mathf.CeilToInt(clip.frameRate*clip.length);

			var oldWrapMode = clip.wrapMode;
			clip.wrapMode = WrapMode.Clamp;

			var timestep = math.rcp(clip.frameRate);
			var job = new CaptureBoneSamplesJob
			{
				boneTransforms = boneTransforms,
				samplesPerBone = requiredSamples,
				currentSample = 0,
				startOffset = startIndex,
			};

			for (var i = 0; i < requiredSamples; i++)
			{
				clip.SampleAnimation(sampleRoot, timestep*i);
				job.currentSample = i;
				job.RunReadOnlyByRef(transformAccessArray);
			}

			clip.wrapMode = oldWrapMode;
		}

		static NativeArray<byte> CreateAclData(float sampleRate, in UnsafeArray<Qvvs> boneSamplesBuffer, UnsafeArray<short> parentIndices)
		{
			var boneTransforms = boneSamplesBuffer.AsNativeArray();

			// Step 1: Patch parent hierarchy for ACL
			var parents = parentIndices.AsNativeArray();
			for (short i = 0; i < parentIndices.Length; i++)
			{
				var index = parentIndices[i]+1;
				if (index < 0)
				{
					index = i;
				}
				parents[i] = (short)index;
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

		static void SaveClipData(AnimationClip animation, NativeArray<byte> data)
		{
			var clip = CreateInstance<AnimationClipAsset>();
			clip.guid = SerializableGuid.NewGuid();
			clip.name = animation.name;

			var clipPath = StreamingManager.ClipPath(clip);
			var clipDirectory = Directory.GetParent(clipPath);
			if (!clipDirectory.Exists)
			{
				clipDirectory.Create();
			}

			var stream = new FileStream(clipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, data.Length);
			stream.Write(new ReadOnlySpan<byte>(data.GetUnsafePtr(), data.Length));
			stream.Flush();
			stream.Close();

			var originalClipPath = AssetDatabase.GetAssetPath(animation);
			var originalDirectory = Directory.GetParent(originalClipPath);
			var relativePath = "Assets/"+originalDirectory.FullName.Substring(Application.dataPath.Length+1);
			var clipAssetPath = Path.Combine(relativePath, $"{animation.name}_ppt.asset");
			AssetDatabase.CreateAsset(clip, clipAssetPath);
		}

		[BurstCompile]
		struct CaptureBoneSamplesJob : IJobParallelForTransform
		{
			public UnsafeArray<Qvvs> boneTransforms;
			public int samplesPerBone;
			public int currentSample;
			public int startOffset;

			public void Execute(int index, TransformAccess transform)
			{
				var target = startOffset+index*samplesPerBone+currentSample;
				boneTransforms[target] = new Qvvs(transform.localPosition, transform.localRotation, 1f, transform.localScale);
			}
		}

		struct ConvertSbyteToShort : NativeCollectionsExt.IConverter<sbyte, short>
		{
			public short Convert(sbyte value)
			{
				return value;
			}
		}
	}
}