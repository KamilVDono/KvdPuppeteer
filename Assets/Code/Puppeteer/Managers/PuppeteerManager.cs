using KVD.Utils;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine.PlayerLoop;

namespace KVD.Puppeteer.Managers
{
	public class PuppeteerManager
	{
		const int PreAllocPuppets = 1024;
		static readonly ProfilerMarker RunSamplingJobMarker = new ProfilerMarker("PuppeteerManager.RunSamplingJob");
		static readonly ProfilerMarker CompleteJobMarker = new ProfilerMarker("PuppeteerManager.CompleteJob");

		StreamingManager _streamingManager;
		WorldOffsetsManager _worldOffsetsManager;
		ClipsManager _clipsManager;
		SkeletonsManager _skeletonsManager;
		BlendTreesManager _blendTreesManager;
		SamplingManager _samplingManager;
		StatesManager _statesManager;
		TransitionsManager _transitionsManager;

		JobHandle _jobHandle;

		public JobHandle AnimationsJobHandle => _jobHandle;

		public static PuppeteerManager Instance { get; private set; }

		public WorldOffsetsManager WorldOffsetsManager => _worldOffsetsManager;
		public ClipsManager ClipsManager => _clipsManager;
		public SamplingManager SamplingManager => _samplingManager;
		public TransitionsManager TransitionsManager => _transitionsManager;

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
#else
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
		static void Initialize()
		{
			Instance = new PuppeteerManager();
		}

		public PuppeteerManager()
		{
			_streamingManager = new StreamingManager();
			_worldOffsetsManager = new WorldOffsetsManager(PreAllocPuppets);
			_clipsManager = new ClipsManager();
			_skeletonsManager = new SkeletonsManager(PreAllocPuppets);
			_blendTreesManager = new BlendTreesManager(PreAllocPuppets);
			_samplingManager = new SamplingManager(PreAllocPuppets);
			_statesManager = new StatesManager(PreAllocPuppets);
			_transitionsManager = new TransitionsManager();

			PlayerLoopUtils.RegisterToPlayerLoopAfter<PuppeteerManager, Update, Update.ScriptRunBehaviourUpdate>(RunAnimationSamples);
			PlayerLoopUtils.RegisterToPlayerLoopAfter<PuppeteerManager, PreLateUpdate, PreLateUpdate.DirectorUpdateAnimationEnd>(CompleteJobs);
		}

		public uint RegisterPuppet(VirtualBones bones)
		{
			return _samplingManager.RegisterPuppet(bones);
		}

		public void UnregisterPuppet(uint slot)
		{
			_transitionsManager.UnregisterPuppet(slot);
			_statesManager.UnregisterPuppet(slot);
			_samplingManager.UnregisterPuppet(slot);
		}

		void RunAnimationSamples()
		{
			RunSamplingJobMarker.Begin();
			// Chain

			var collectWorldOffsetsJob = _worldOffsetsManager.CollectWorldOffsets(default);
			_transitionsManager.RunTransitions();
			// Run blending
			var evaluateBlendTreesHandle = _blendTreesManager.RunBlendTrees(default);
			var fillJob = _statesManager.FillSamplingData(evaluateBlendTreesHandle);
			var animationsHandle = _samplingManager.RunAnimationSamples(fillJob);
			var skeletonDependencies = JobHandle.CombineDependencies(collectWorldOffsetsJob, animationsHandle);
			_jobHandle = _skeletonsManager.RunSyncTransformsJob(skeletonDependencies);

			RunSamplingJobMarker.End();
		}

		void CompleteJobs()
		{
			CompleteJobMarker.Begin();
			_jobHandle.Complete();
			CompleteJobMarker.End();
		}
	}
}
