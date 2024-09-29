using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KVD.Utils;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

namespace KandraRenderer {
    public class KandraRendererManager {
#if UNITY_EDITOR
        const int DebugMultiplier = 4;
#else
        const int DebugMultiplier = 4;
#endif
        public const int RigBonesCapacity = 75_000 * DebugMultiplier;
        public const int RenderersCapacity = 10_000 * DebugMultiplier;
        public const int UniqueMeshesCapacity = 25 * DebugMultiplier;
        public const int UniqueVerticesCapacity = UniqueMeshesCapacity * 2_600 * DebugMultiplier;
        public const int UniqueBindposesCapacity = UniqueMeshesCapacity * 10 * DebugMultiplier;
        public const int SkinBonesCapacity = 75_000 * DebugMultiplier;
        public const int BlendshapesCapacity = 2_500 * DebugMultiplier;
        public const int BlendshapesDeltasCapacity = 1_000_000 * DebugMultiplier;
        public const int SkinnedVerticesCapacity = 9_750_000 * DebugMultiplier;

        static readonly ProfilerMarker RegisterRendererMarker = new ProfilerMarker("KandraRendererManager.RegisterRenderer");
        static readonly ProfilerMarker UnregisterRendererMarker = new ProfilerMarker("KandraRendererManager.UnregisterRenderer");

        public static KandraRendererManager Instance { get; private set; }

        public RigManager RigManager { get; private set; }
        public MeshManager MeshManager { get; private set; }
        public BonesManager BonesManager { get; private set; }
        public SkinningManager SkinningManager { get; private set; }
        public BlendshapesManager BlendshapesManager { get; private set; }
        public VisibilityCullingManager VisibilityCullingManager { get; private set; }
        public SkinnedBatchRenderGroup SkinnedBatchRenderGroup { get; private set; }
        public MeshBroker MeshBroker { get; private set; }
        public StreamingManager StreamingManager { get; private set; }

        UnsafeBitmask _takenSlots;
        KandraRenderer[] _renderers;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void Init()
        {
            Instance?.Dispose();

            Instance = new KandraRendererManager();
            Instance.InitReferences();

            PlayerLoopUtils.RegisterToPlayerLoopBegin<VisibilityCullingManager, EarlyUpdate>(Instance.OnEarlyUpdateBegin);
            PlayerLoopUtils.RegisterToPlayerLoopAfter<KandraRendererManager, PreLateUpdate, PreLateUpdate.DirectorDeferredEvaluate>(Instance.OnPreLateUpdateEnd);
            PlayerLoopUtils.RegisterToPlayerLoopBegin<KandraRendererManager, TimeUpdate>(Instance.OnTimeUpdateBegin);
            RenderPipelineManager.beginContextRendering += Instance.OnBeginRendering;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                {
                    Instance.EDITOR_ExitPlaymode();
                }
                if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
                {
                    Instance.Dispose();
                    Instance = null;
                }
            };

            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                Instance?.Dispose();
                Instance = null;
            };
#endif
        }

        KandraRendererManager() { }

        void InitReferences()
        {
            _takenSlots = new UnsafeBitmask(RenderersCapacity, Allocator.Persistent);
            _renderers = new KandraRenderer[RenderersCapacity];

            var skinningShader = Resources.Load<ComputeShader>("Skinning");
            var prepareBonesShader = Resources.Load<ComputeShader>("PrepareBones");

            RigManager = new RigManager(prepareBonesShader);
            MeshManager = new MeshManager(skinningShader, prepareBonesShader);
            BonesManager = new BonesManager(skinningShader, prepareBonesShader);
            BlendshapesManager = new BlendshapesManager(skinningShader);
            SkinningManager = new SkinningManager(skinningShader);
            VisibilityCullingManager = new VisibilityCullingManager();
            SkinnedBatchRenderGroup = new SkinnedBatchRenderGroup(_takenSlots, VisibilityCullingManager);
            MeshBroker = new MeshBroker();
            StreamingManager = new StreamingManager();
        }

        void Dispose()
        {
            PlayerLoopUtils.RemoveFromPlayerLoop<VisibilityCullingManager, EarlyUpdate>();
            PlayerLoopUtils.RemoveFromPlayerLoop<KandraRendererManager, PreLateUpdate>();
            PlayerLoopUtils.RemoveFromPlayerLoop<KandraRendererManager, TimeUpdate>();
            RenderPipelineManager.beginContextRendering -= OnBeginRendering;

            RigManager.Dispose();
            MeshManager.Dispose();
            BonesManager.Dispose();
            BlendshapesManager.Dispose();
            SkinningManager.Dispose();
            VisibilityCullingManager.Dispose();
            SkinnedBatchRenderGroup.Dispose();
            StreamingManager.Dispose();
            _takenSlots.Dispose();
            _renderers = null;
        }

        public int Register(KandraRenderer renderer)
        {
            var registerData = renderer.rendererData;
            RegisterRendererMarker.Begin();
            registerData.rig.virtualBones.EnsureInitialized();
            var slot = _takenSlots.FirstZero();
            if (slot == -1) {
                Debug.LogError("No more slots available for rendering for Kandra");
                return -1;
            }
            var uSlot = (uint)slot;
            _takenSlots.Up(uSlot);
            _renderers[slot] = renderer;

            var rigRegion = RigManager.RegisterRig(registerData.rig);
            var meshRegion = MeshManager.RegisterMesh(registerData.mesh);
            var bonesStartIndex = BonesManager.Register(uSlot, registerData.bones, rigRegion, meshRegion.bindPosesMemory);
            BlendshapesManager.Register(uSlot, registerData.blendshapeWeights, registerData.mesh);
            SkinningManager.Register(uSlot, meshRegion.verticesMemory, bonesStartIndex, out var instanceStartVertex);
            SkinnedBatchRenderGroup.Register(uSlot, registerData.RenderingMesh, registerData.materials, instanceStartVertex, meshRegion.verticesMemory.start);
            VisibilityCullingManager.Register(uSlot, registerData.rootBoneMatrix, ref registerData.rig.virtualBones.Skeleton.localToWorlds[registerData.rootbone], registerData.mesh.localBoundingSphere);
            registerData.mesh.DisposeMeshData();

            RegisterRendererMarker.End();

            return slot;
        }

        public void Unregister(int rendererHandle) {
            if (rendererHandle < 0) {
                Debug.LogError("Trying to unregister a renderer with a negative handle");
                return;
            }
            var uSlot = (uint)rendererHandle;
            var renderer = _renderers[rendererHandle];
            ref var registerData = ref renderer.rendererData;

            UnregisterRendererMarker.Begin();
            _takenSlots.Down(uSlot);

            RigManager.UnregisterRig(registerData.rig);
            MeshManager.UnregisterMesh(registerData.mesh);
            BonesManager.Unregister(uSlot);
            BlendshapesManager.Unregister(uSlot, registerData.mesh);
            SkinningManager.Unregister(uSlot);
            SkinnedBatchRenderGroup.Unregister(uSlot);
            VisibilityCullingManager.Unregister(uSlot);

            MeshBroker.ReleaseOriginalMesh(registerData.mesh);
            registerData.originalMesh = null;
            if (registerData.cullableMesh)
            {
                MeshBroker.ReleaseCullableMesh(registerData.mesh, registerData.cullableMesh);
                registerData.cullableMesh = null;
            }
            registerData.blendshapeWeights.Dispose();
            renderer.RenderingId = -1;

            _renderers[rendererHandle] = null;

            UnregisterRendererMarker.End();
        }

        public void RigChanged(KandraRig kandraRig, List<KandraRenderer> renderers) {
            // -- Update Rigmanager
            var rigRegion = RigManager.RigChanged(kandraRig);
            // Update BonesManager
            for (var i = 0; i < renderers.Count; i++) {
                ref var registerData = ref renderers[i].rendererData;
                var meshRegion = MeshManager.GetMeshMemory(registerData.mesh);
                BonesManager.RigChanged((uint)renderers[i].RenderingId, registerData.bones, rigRegion, meshRegion.bindPosesMemory);
            }
        }

        public void UpdateRenderingMesh(int renderingId, Mesh cullableMesh) {
            SkinnedBatchRenderGroup.UpdateCullableMesh(renderingId, cullableMesh);
        }

        void OnEarlyUpdateBegin() {
            RigManager.EnsureBuffers();
            BonesManager.EnsureBuffers();
            MeshManager.EnsureBuffers();
            SkinningManager.EnsureBuffers();
            BlendshapesManager.EnsureBuffers();
        }

        void OnTimeUpdateBegin() {
            StreamingManager.OnFrameEnd();
        }

        void OnPreLateUpdateEnd() {
            RigManager.CollectBoneMatrices();
            VisibilityCullingManager.CollectCullingData(_takenSlots);
            JobHandle.ScheduleBatchedJobs();
        }

        void OnBeginRendering(ScriptableRenderContext _, List<Camera> __) {
            RigManager.UnlockBuffer();
            BonesManager.RunComputeShader();
            BlendshapesManager.UpdateBlendshapes(_takenSlots);
            SkinningManager.RunSkinning();
            VisibilityCullingManager.collectCullingDataJobHandle.Complete();
        }

        public void BoundsAndRootBone(int renderingId, out float4 worldBoundingSphere, out float3x4 rootBoneMatrix) {
            var uSlot = (uint)renderingId;
            worldBoundingSphere.x = VisibilityCullingManager.xs[uSlot];
            worldBoundingSphere.y = VisibilityCullingManager.ys[uSlot];
            worldBoundingSphere.z = VisibilityCullingManager.zs[uSlot];
            worldBoundingSphere.w = VisibilityCullingManager.radii[uSlot];
            rootBoneMatrix = VisibilityCullingManager.rootBones[uSlot];
        }

        public bool IsVisible(int renderingId) {
            return SkinnedBatchRenderGroup.cameraSplitMaskVisibility[(uint)renderingId] != 0;
        }

#if UNITY_EDITOR
        void EDITOR_ExitPlaymode()
        {
            foreach (var slot in _takenSlots.EnumerateOnes())
            {
                var renderer = _renderers[slot];
                Unregister(renderer.RenderingId);
                renderer.EDITOR_Force_Uninitialized = true;
            }
        }
#endif

        public void OnGUI() {
            StringBuilder sb = new StringBuilder();

            var used = 0.0;
            var total = 0.0;

            sb.AppendLine("KandraRendererManager:");
            var takenSlots = (uint)(_takenSlots.LastOne()+1);
            RigManager.OnGUI(sb, ref used, ref total);
            MeshManager.OnGUI(sb, ref used, ref total);
            BonesManager.OnGUI(sb, ref used, ref total);
            BlendshapesManager.OnGUI(sb, takenSlots, ref used, ref total);
            SkinningManager.OnGUI(sb, takenSlots, ref used, ref total);
            SkinnedBatchRenderGroup.OnGUI(sb, takenSlots, ref used, ref total);

            sb.Append("Full ");
            sb.Append(HumanReadableBytes(used));
            sb.Append("/");
            sb.Append(HumanReadableBytes(total));
            var content = new GUIContent(sb.ToString());

            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            var height = labelStyle.CalcHeight(content, Screen.width);
            var rect = new Rect(5, Screen.height - height - 25, Screen.width, height);
            GUI.Label(rect, content, labelStyle);
        }

        public static string HumanReadableBytes(double byteCount) {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            int place = Convert.ToInt32(Math.Floor(Math.Log(byteCount, 1024)));
            double num = Math.Round(byteCount / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture) + suf[place];
        }

        public static void LogBuffer(StringBuilder sb, GraphicsBuffer buffer, string bufferName, uint elementsInUse,
            ref double used, ref double total) {
            var usedBytes = elementsInUse * buffer.stride;
            var totalBytes = buffer.count * buffer.stride;
            sb.AppendLine($"  * {bufferName}: {HumanReadableBytes(usedBytes)}/{HumanReadableBytes(totalBytes)}({usedBytes / (float)totalBytes:P})");
            used += usedBytes;
            total += totalBytes;
        }
    }
}