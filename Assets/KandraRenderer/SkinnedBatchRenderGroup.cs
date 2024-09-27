using System;
using System.Text;
using KVD.Utils.DataStructures;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

using static KandraRenderer.KandraRendererManager;

namespace KandraRenderer {
    public unsafe class SkinnedBatchRenderGroup {
        const int MaxRenderers = KandraRendererManager.RenderersCapacity;

        static readonly int UintInstanceDataSize = sizeof(InstanceData) / sizeof(uint);
        static readonly int InstancesDataStart = (sizeof(PackedMatrix) / sizeof(uint)) * 2;
        static readonly ProfilerMarker CameraPerformCullingMarker = new ProfilerMarker("SkinnedBatchRenderGroup.CameraPerformCulling");
        static readonly ProfilerMarker LightPerformCullingMarker = new ProfilerMarker("SkinnedBatchRenderGroup.LightPerformCulling");

        static readonly BatchFilterSettings FilteringSettings = new BatchFilterSettings() {
            layer = 3,
            renderingLayerMask = 257,
            rendererPriority = 0,
            motionMode = MotionVectorGenerationMode.Camera,
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true,
            staticShadowCaster = true,
            allDepthSorted = false
        };

        public UnsafeArray<ushort> cameraSplitMaskVisibility;
        public UnsafeArray<ushort> lightsSplitMaskVisibility;
        public UnsafeArray<ushort> lightsAggregatedSplitMaskVisibility;

        VisibilityCullingManager _visibilityCullingManager;

        BatchRendererGroup _brg;
        GraphicsBuffer _instanceBuffer;
        BatchID _batchID;

        ForFrameValue<JobHandle> _performCullingJobHandle;

        UnsafeBitmask _takenSlots;

        UnsafeArray<Renderer> _registeredRenderers;
        UnsafeHashMap<Renderer, RendererInstancesData> _rendererInstancesData;
        UnsafeHashMap<Renderer, Renderer> _rendererAllocationsTracker;

        public void OnGUI(StringBuilder sb, uint takenSlots, ref double used, ref double total) {
            sb.AppendLine(nameof(SkinnedBatchRenderGroup));

            var usedBytes = takenSlots * sizeof(InstanceData) + 2 * sizeof(PackedMatrix);
            var totalBytes = _instanceBuffer.count * _instanceBuffer.stride;
            sb.AppendLine($"  * InstanceBuffer: {HumanReadableBytes(usedBytes)}/{HumanReadableBytes(totalBytes)}({usedBytes / (float)totalBytes:P})");
            used += usedBytes;
            total += totalBytes;
        }

        public SkinnedBatchRenderGroup(UnsafeBitmask takenSlots, VisibilityCullingManager visibilityCullingManager) {
            _visibilityCullingManager = visibilityCullingManager;

            _brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            _brg.SetGlobalBounds(new Bounds(Vector3.zero, Vector3.one*1000));
            _brg.SetEnabledViewTypes(new[] { BatchCullingViewType.Camera, BatchCullingViewType.Light });

            var matricesUintSize = InstancesDataStart;
            var instancesUintSize = (sizeof(InstanceData) / sizeof(uint)) * MaxRenderers;
            var instancesBufferSize = matricesUintSize + instancesUintSize;
            _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, instancesBufferSize, sizeof(uint));

            var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
            var offset = 0;
            metadata[0] = CreateMetadataValue(Shader.PropertyToID("unity_ObjectToWorld"), offset, false);
            offset += sizeof(PackedMatrix);
            metadata[1] = CreateMetadataValue(Shader.PropertyToID("unity_WorldToObject"), offset, false);
            offset += sizeof(PackedMatrix);
            metadata[2] = CreateMetadataValue(Shader.PropertyToID("_InstanceData"), offset, true);

            _batchID = _brg.AddBatch(metadata, _instanceBuffer.bufferHandle);

            metadata.Dispose();

            var matrix        = Matrix4x4.identity; // transform.localToWorldMatrix;
            var inverse       = matrix.inverse;
            var packed        = new PackedMatrix(matrix);
            var packedInverse = new PackedMatrix(inverse);

            _instanceBuffer.SetData(new[] { packed, packedInverse });

            _registeredRenderers = new UnsafeArray<Renderer>(MaxRenderers, Allocator.Persistent);
            _rendererInstancesData = new UnsafeHashMap<Renderer, RendererInstancesData>(MaxRenderers, Allocator.Persistent);
            _rendererAllocationsTracker = new UnsafeHashMap<Renderer, Renderer>(MaxRenderers, Allocator.Persistent);

            cameraSplitMaskVisibility = new UnsafeArray<ushort>(MaxRenderers, Allocator.Persistent);
            lightsSplitMaskVisibility = new UnsafeArray<ushort>(MaxRenderers, Allocator.Persistent);
            lightsAggregatedSplitMaskVisibility = new UnsafeArray<ushort>(MaxRenderers, Allocator.Persistent);

            _takenSlots = takenSlots;
        }

        public void Dispose() {
            _brg.Dispose();
            _instanceBuffer.Dispose();
            for (var i = 0u; i < _registeredRenderers.Length; i++) {
                if (_registeredRenderers[i].materialIds.IsCreated) {
                    _registeredRenderers[i].materialIds.Dispose();
                }
            }
            _registeredRenderers.Dispose();
            foreach (var kvp in _rendererInstancesData) {
                kvp.Value.Dispose();
            }
            _rendererInstancesData.Dispose();
            foreach (var kvp in _rendererAllocationsTracker) {
                kvp.Value.materialIds.Dispose();
            }
            _rendererAllocationsTracker.Dispose();
            cameraSplitMaskVisibility.Dispose();
            lightsSplitMaskVisibility.Dispose();
            lightsAggregatedSplitMaskVisibility.Dispose();
        }

        public void Register(uint slot, Mesh mesh, Material[] material, uint instanceStartVertex, uint sharedStartVertex) {
            var meshId = _brg.RegisterMesh(mesh);
            var materialIds = new UnsafeArray<BatchMaterialID>((uint)material.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (var i = 0u; i < material.Length; i++) {
                materialIds[i] = _brg.RegisterMaterial(material[i]);
            }
            var renderer = new Renderer(meshId, materialIds);

            if (AddRendererInstanceData(renderer, slot)) {
                var materialIdsCopy = new UnsafeArray<BatchMaterialID>(materialIds.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                UnsafeUtility.MemCpy(materialIdsCopy.Ptr, materialIds.Ptr, materialIds.Length * sizeof(BatchMaterialID));
                renderer = new Renderer(meshId, materialIdsCopy);
            }

            _registeredRenderers[slot] = renderer;

            var verticesStartArray = new NativeArray<InstanceData>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            verticesStartArray[0] = new InstanceData {
                instanceStartVertex = instanceStartVertex,
                sharedStartVertex = sharedStartVertex,
            };
            var uArray = verticesStartArray.Reinterpret<uint>(sizeof(InstanceData));
            _instanceBuffer.SetData(uArray, 0, (int)(InstancesDataStart + (slot * UintInstanceDataSize)), uArray.Length);
            verticesStartArray.Dispose();
        }

        public void Unregister(uint slot) {
            var renderer = _registeredRenderers[slot];
            if (renderer.meshId == BatchMeshID.Null) {
                Debug.LogError($"Kandra Renderer: Trying to unregister slot {slot} that is not registered");
                return;
            }

            RemoveRendererInstanceData(renderer, slot);

            var meshId = renderer.meshId;
            _brg.UnregisterMesh(meshId);

            var materials = renderer.materialIds;
            for (var i = 0u; i < materials.Length; i++) {
                _brg.UnregisterMaterial(materials[i]);
            }

            _registeredRenderers[slot] = default;
            materials.Dispose();
        }

        public void UpdateCullableMesh(int renderingId, Mesh cullableMesh) {
            var uSlot = (uint)renderingId;
            var renderer = _registeredRenderers[uSlot];
            if (renderer.meshId == BatchMeshID.Null) {
                Debug.LogError($"Kandra Renderer: Trying to update slot {uSlot} that is not registered");
                return;
            }

            RemoveRendererInstanceData(renderer, uSlot);

            _brg.UnregisterMesh(renderer.meshId);
            var newMeshId = _brg.RegisterMesh(cullableMesh);
            var materialIds = renderer.materialIds;
            renderer = new Renderer(newMeshId, materialIds);

            if (AddRendererInstanceData(renderer, uSlot)) {
                var materialIdsCopy = new UnsafeArray<BatchMaterialID>(materialIds.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                UnsafeUtility.MemCpy(materialIdsCopy.Ptr, materialIds.Ptr, materialIds.Length * sizeof(BatchMaterialID));
                renderer = new Renderer(newMeshId, materialIdsCopy);
            }
            _registeredRenderers[uSlot] = renderer;
        }

        JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext) {
            if ((cullingContext.cullingLayerMask & (1 << FilteringSettings.layer)) == 0) {
                return default;
            }
            if ((cullingContext.sceneCullingMask & FilteringSettings.sceneCullingMask) == 0) {
                return default;
            }

            var performCullingJobHandle = _performCullingJobHandle.Value;
            var takenLength = _takenSlots.LastOne()+1;

            if (cullingContext.viewType == BatchCullingViewType.Camera) {
                if (takenLength > 0) {
                    CameraPerformCullingMarker.Begin();
                    performCullingJobHandle = JobHandle.CombineDependencies(performCullingJobHandle, _visibilityCullingManager.collectCullingDataJobHandle);
#if UNITY_EDITOR
                    //if (UnityEditor.SceneView.currentDrawingSceneView == null)
#endif
                    {
                        performCullingJobHandle = new CameraFrustumJob
                        {
                            planes = CameraFrustumCullingPlanes(cullingContext.cullingPlanes), // Job will deallocate
                            xs = _visibilityCullingManager.xs,
                            ys = _visibilityCullingManager.ys,
                            zs = _visibilityCullingManager.zs,
                            radii = _visibilityCullingManager.radii,
                            splitMaskVisibility = cameraSplitMaskVisibility,
                        }.Schedule(takenLength, 256, performCullingJobHandle);
                    }

                    var rendererInstancesWithSplitData = new UnsafeParallelHashMap<RendererWithSplit, RendererInstancesData>(_rendererInstancesData.Capacity, Allocator.TempJob);
                    var parallelRendererInstancesData = _rendererInstancesData.GetKeyValueArrays(Allocator.TempJob);
                    performCullingJobHandle = new TriangeRenderersJob
                    {
                        splitMaskVisibility = cameraSplitMaskVisibility,
                        registeredInstancesRenderers = parallelRendererInstancesData,
                        instancesByRenderer = rendererInstancesWithSplitData.AsParallelWriter(),
                    }.Schedule(parallelRendererInstancesData.Length, 32, performCullingJobHandle);

                    var disposeJobHandle = parallelRendererInstancesData.Dispose(performCullingJobHandle);
                    var emitCommandsJobHandle = new EmitDrawCommandsJob
                    {
                        filteringSettings = FilteringSettings,
                        registeredInstancesRenderers = rendererInstancesWithSplitData,
                        batchID = _batchID,
                        drawCommandsOutput = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr(),
                    }.Schedule(performCullingJobHandle);

                    var disposeWithSplitsJobHandle = rendererInstancesWithSplitData.Dispose(emitCommandsJobHandle);
                    performCullingJobHandle = JobHandle.CombineDependencies(disposeJobHandle, disposeWithSplitsJobHandle);

                    CameraPerformCullingMarker.End();
                } else {
                    _visibilityCullingManager.collectCullingDataJobHandle.Complete();
                }
            } else if (cullingContext.viewType == BatchCullingViewType.Light) {
                if (takenLength > 0) {
                    LightPerformCullingMarker.Begin();

                    CullingUtils.LightCullingSetup(cullingContext, out var receiverSphereCuller, out var frustumPlanes,
                        out var frustumSplits, out var receivers, out var lightFacingFrustumPlanes);

                    performCullingJobHandle = new LightFrustumJob {
                        cullingPlanes = frustumPlanes, // Job will deallocate
                        frustumSplits = frustumSplits, // Job will deallocate
                        receiversPlanes = receivers, // Job will deallocate
                        lightFacingFrustumPlanes = lightFacingFrustumPlanes, // Job will deallocate
                        spheresSplitInfos = receiverSphereCuller.splitInfos, // Job will deallocate
                        worldToLightSpaceRotation = receiverSphereCuller.worldToLightSpaceRotation,
                        xs = _visibilityCullingManager.xs,
                        ys = _visibilityCullingManager.ys,
                        zs = _visibilityCullingManager.zs,
                        radii = _visibilityCullingManager.radii,
                        splitMaskVisibility = lightsSplitMaskVisibility,
                        aggregatedSplitMaskVisibility = lightsAggregatedSplitMaskVisibility,
                    }.Schedule(takenLength, 256, performCullingJobHandle);

                    var rendererInstancesWithSplitData = new UnsafeParallelHashMap<RendererWithSplit, RendererInstancesData>(_rendererInstancesData.Capacity, Allocator.TempJob);
                    var parallelRendererInstancesData = _rendererInstancesData.GetKeyValueArrays(Allocator.TempJob);
                    performCullingJobHandle = new TriangeRenderersJob
                    {
                        splitMaskVisibility = lightsSplitMaskVisibility,
                        registeredInstancesRenderers = parallelRendererInstancesData,
                        instancesByRenderer = rendererInstancesWithSplitData.AsParallelWriter(),
                    }.Schedule(parallelRendererInstancesData.Length, 16, performCullingJobHandle);

                    var disposeJobHandle = parallelRendererInstancesData.Dispose(performCullingJobHandle);
                    var emitCommandsJobHandle = new EmitDrawCommandsJob
                    {
                        filteringSettings = FilteringSettings,
                        registeredInstancesRenderers = rendererInstancesWithSplitData,
                        batchID = _batchID,
                        drawCommandsOutput = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr(),
                    }.Schedule(performCullingJobHandle);

                    var disposeWithSplitsJobHandle = rendererInstancesWithSplitData.Dispose(emitCommandsJobHandle);
                    performCullingJobHandle = JobHandle.CombineDependencies(disposeJobHandle, disposeWithSplitsJobHandle);

                    LightPerformCullingMarker.End();
                }
            }

            _performCullingJobHandle.Value = performCullingJobHandle;

            return performCullingJobHandle;
        }

        NativeArray<float4> CameraFrustumCullingPlanes(NativeArray<Plane> cullingPlanes) {
            const int PlanesCount = 6;
            var outputPlanes = new NativeArray<float4>(PlanesCount, Allocator.TempJob);
            for (int i = 0; i < PlanesCount; i++) {
                outputPlanes[i] = new float4(cullingPlanes[i].normal, cullingPlanes[i].distance);
            }
            return outputPlanes;
        }

        bool AddRendererInstanceData(in Renderer renderer, uint uSlot) {
            var freshAdd = false;
            if(_rendererInstancesData.TryGetValue(renderer, out var data)) {
                data.takenSlots.Add(uSlot);
                _rendererInstancesData[renderer] = data;
            } else {
                _rendererInstancesData.TryAdd(renderer, new RendererInstancesData(uSlot, Allocator.Persistent));
                _rendererAllocationsTracker.TryAdd(renderer, renderer);
                freshAdd = true;
            }

            if (!_rendererInstancesData.ContainsKey(renderer)) {
                Debug.LogError($"Add failed from renderer {renderer}");
            }

            return freshAdd;
        }

        void RemoveRendererInstanceData(in Renderer renderer, uint uSlot) {
            if (!_rendererInstancesData.TryGetValue(renderer, out var data)) {
                Debug.LogError($"Trying to remove renderer {renderer} that is not registered");
                return;
            }

            var index = data.takenSlots.IndexOf(uSlot);
            data.takenSlots.RemoveAt(index);
            if(data.RefCount == 0) {
                var materials = _rendererAllocationsTracker[renderer].materialIds;
                _rendererAllocationsTracker.Remove(renderer);

                data.Dispose();
                _rendererInstancesData.Remove(renderer);
                materials.Dispose();
            } else {
                _rendererInstancesData[renderer] = data;
            }
        }

        [BurstCompile]
        struct CameraFrustumJob : IJobParallelForBatch {
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<float4> planes;

            [ReadOnly] public UnsafeArray<float> xs;
            [ReadOnly] public UnsafeArray<float> ys;
            [ReadOnly] public UnsafeArray<float> zs;
            [ReadOnly] public UnsafeArray<float> radii;

            [WriteOnly] public UnsafeArray<ushort> splitMaskVisibility;

            public void Execute(int startIndex, int count) {
                var uStartIndex = (uint)startIndex;
                var uCount = (uint)count;

                var p0 = planes[0];
                var p1 = planes[1];
                var p2 = planes[2];
                var p3 = planes[3];
                var p4 = planes[4];
                var p5 = planes[5];

                for (var i = 0u; uCount - i >= 4; i += 4) {
                    var fullIndex = uStartIndex + i;
                    var simdXs = xs.ReinterpretLoad<float4>(fullIndex);
                    var simdYs = ys.ReinterpretLoad<float4>(fullIndex);
                    var simdZs = zs.ReinterpretLoad<float4>(fullIndex);
                    var simdRadii = radii.ReinterpretLoad<float4>(fullIndex);

                    bool4 frustumMask =
                        p0.x * simdXs + p0.y * simdYs + p0.z * simdZs + p0.w + simdRadii > 0.0f &
                        p1.x * simdXs + p1.y * simdYs + p1.z * simdZs + p1.w + simdRadii > 0.0f &
                        p2.x * simdXs + p2.y * simdYs + p2.z * simdZs + p2.w + simdRadii > 0.0f &
                        p3.x * simdXs + p3.y * simdYs + p3.z * simdZs + p3.w + simdRadii > 0.0f &
                        p4.x * simdXs + p4.y * simdYs + p4.z * simdZs + p4.w + simdRadii > 0.0f &
                        p5.x * simdXs + p5.y * simdYs + p5.z * simdZs + p5.w + simdRadii > 0.0f;

                    var bigSplits = math.select(uint4.zero, new uint4(1), frustumMask);
                    ushort4 splits = default;
                    splits.x = (ushort)bigSplits.x;
                    splits.y = (ushort)bigSplits.y;
                    splits.z = (ushort)bigSplits.z;
                    splits.w = (ushort)bigSplits.w;
                    splitMaskVisibility.ReinterpretStore(fullIndex, splits);
                }

                for (var i = uCount.SimdTrailing(); i < uCount; ++i) {
                    var fullIndex = uStartIndex + i;
                    var position = new float3(xs[fullIndex], ys[fullIndex], zs[fullIndex]);
                    var r = radii[fullIndex];
                    var frustumVisible =
                        math.dot(p0.xyz, position) + p0.w + r > 0.0f &
                        math.dot(p1.xyz, position) + p1.w + r > 0.0f &
                        math.dot(p2.xyz, position) + p2.w + r > 0.0f &
                        math.dot(p3.xyz, position) + p3.w + r > 0.0f &
                        math.dot(p4.xyz, position) + p4.w + r > 0.0f &
                        math.dot(p5.xyz, position) + p5.w + r > 0.0f;

                    splitMaskVisibility[fullIndex] = (ushort)math.select(0, 1, frustumVisible);
                }
            }
        }

        [BurstCompile]
        struct LightFrustumJob : IJobParallelForBatch {
            [DeallocateOnJobCompletion, ReadOnly] public NativeArray<float4> cullingPlanes;
            [DeallocateOnJobCompletion, ReadOnly] public NativeArray<int> frustumSplits;
            [DeallocateOnJobCompletion, ReadOnly] public NativeArray<float4> receiversPlanes;

            [DeallocateOnJobCompletion, ReadOnly] public NativeArray<float4> lightFacingFrustumPlanes;
            [DeallocateOnJobCompletion, ReadOnly] public NativeArray<SphereSplitInfo> spheresSplitInfos;
            [ReadOnly] public float3x3 worldToLightSpaceRotation;

            [ReadOnly] public UnsafeArray<float> xs;
            [ReadOnly] public UnsafeArray<float> ys;
            [ReadOnly] public UnsafeArray<float> zs;
            [ReadOnly] public UnsafeArray<float> radii;

            [WriteOnly] public UnsafeArray<ushort> splitMaskVisibility;
            public UnsafeArray<ushort> aggregatedSplitMaskVisibility;

            public void Execute(int startIndex, int count) {
                var uStartIndex = (uint)startIndex;
                var uCount = (uint)count;

                for (var i = 0u; uCount - i >= 4; i += 4) {
                    var fullIndex = uStartIndex + i;
                    var simdXs = xs.ReinterpretLoad<float4>(fullIndex);
                    var simdYs = ys.ReinterpretLoad<float4>(fullIndex);
                    var simdZs = zs.ReinterpretLoad<float4>(fullIndex);
                    var simdRadii = radii.ReinterpretLoad<float4>(fullIndex);

                    CullingUtils.LightSimdCulling(receiversPlanes, frustumSplits, cullingPlanes,
                        worldToLightSpaceRotation, spheresSplitInfos, lightFacingFrustumPlanes,
                        simdXs, simdYs, simdZs, simdRadii,
                        out var mask);

                    ushort4 splits = default;
                    splits.x = (ushort)mask.x;
                    splits.y = (ushort)mask.y;
                    splits.z = (ushort)mask.z;
                    splits.w = (ushort)mask.w;

                    splitMaskVisibility.ReinterpretStore(fullIndex, splits);

                    var aggregatedSplits = aggregatedSplitMaskVisibility.ReinterpretLoad<ushort4>(fullIndex);
                    aggregatedSplits |= splits;
                    aggregatedSplitMaskVisibility.ReinterpretStore(fullIndex, aggregatedSplits);
                }

                for (var i = uCount.SimdTrailing(); i < uCount; ++i) {
                    var fullIndex = uStartIndex + i;

                    var position = new float3(xs[fullIndex], ys[fullIndex], zs[fullIndex]);
                    var r = radii[fullIndex];

                    CullingUtils.LightCulling(receiversPlanes, frustumSplits, cullingPlanes,
                        worldToLightSpaceRotation, spheresSplitInfos, lightFacingFrustumPlanes,
                        position, r, out var mask);

                    splitMaskVisibility[fullIndex] = (ushort)mask;
                    aggregatedSplitMaskVisibility[fullIndex] |= (ushort)mask;
                }
            }
        }

        [BurstCompile]
        struct EmitDrawCommandsJob : IJob {
            public BatchFilterSettings filteringSettings;
            [ReadOnly] public UnsafeParallelHashMap<RendererWithSplit, RendererInstancesData> registeredInstancesRenderers;

            [ReadOnly] public BatchID batchID;

            [NativeDisableUnsafePtrRestriction] public BatchCullingOutputDrawCommands* drawCommandsOutput;

            public void Execute() {
                var renderers = registeredInstancesRenderers.GetKeyValueArrays(Allocator.Temp);
                var commandsCount = 0u;
                var instancesCount = 0u;
                for (var i = 0; i < renderers.Length; ++i) {
                    commandsCount += renderers.Keys[i].renderer.materialIds.Length;
                    instancesCount += (uint)renderers.Values[i].takenSlots.Length;
                }

                var drawCommands =  Malloc<BatchDrawCommand>(commandsCount);
                var visibleInstances = Malloc<int>(instancesCount);
                drawCommandsOutput->drawCommands = drawCommands;
                drawCommandsOutput->visibleInstances = visibleInstances;
                drawCommandsOutput->drawRanges = Malloc<BatchDrawRange>(1u);

                drawCommandsOutput->drawCommandPickingInstanceIDs = null;
                drawCommandsOutput->instanceSortingPositions = null;
                drawCommandsOutput->instanceSortingPositionFloatCount = 0;

                var drawCommandIndex = 0;
                var visibleInstancesIndex = 0;

                for (var i = 0; i < renderers.Length; ++i) {
                    var instancesStart = (uint)visibleInstancesIndex;

                    var instances = renderers.Values[i].takenSlots;
                    for (var j = 0; j < instances.Length; j++) {
                        var instance = (int)instances[j];
                        visibleInstances[visibleInstancesIndex] = instance;
                        ++visibleInstancesIndex;
                    }
                    renderers.Values[i].Dispose();

                    if(instancesStart == visibleInstancesIndex) {
                        continue;
                    }

                    var renderer = renderers.Keys[i];
                    var materials = renderer.renderer.materialIds;
                    for(var j = 0u; j < materials.Length; ++j) {
                        drawCommands[drawCommandIndex] = new BatchDrawCommand
                        {
                            visibleOffset = instancesStart,
                            visibleCount = (uint)(visibleInstancesIndex - instancesStart),
                            batchID = batchID,
                            materialID = materials[j],
                            meshID = renderer.renderer.meshId,
                            submeshIndex = (ushort)j,
                            splitVisibilityMask = renderer.splitMask,
                            flags = BatchDrawCommandFlags.None,
                            sortingPosition = 0,
                        };
                        ++drawCommandIndex;
                    }
                }

                drawCommandsOutput->drawCommandCount = drawCommandIndex;
                drawCommandsOutput->visibleInstanceCount = visibleInstancesIndex;

                drawCommandsOutput->drawRanges[0] = new BatchDrawRange
                {
                    drawCommandsBegin = 0,
                    drawCommandsCount = (uint)drawCommandIndex,
                    filterSettings = filteringSettings,
                };
                drawCommandsOutput->drawRangeCount = 1;

                renderers.Dispose();
            }
        }

        [BurstCompile]
        struct TriangeRenderersJob : IJobParallelFor {
            [ReadOnly] public UnsafeArray<ushort> splitMaskVisibility;
            [ReadOnly] public NativeKeyValueArrays<Renderer, RendererInstancesData> registeredInstancesRenderers;
            [WriteOnly] public UnsafeParallelHashMap<RendererWithSplit, RendererInstancesData>.ParallelWriter instancesByRenderer;

            public void Execute(int index) {
                var renderer = registeredInstancesRenderers.Keys[index];
                var instances =  registeredInstancesRenderers.Values[index].takenSlots;
                var forRenderer = new UnsafeHashMap<RendererWithSplit, RendererInstancesData>(8, Allocator.Temp);
                for (var i = 0; i < instances.Length; i++) {
                    var instance = instances[i];
                    var splitMask = splitMaskVisibility[instance];
                    if (splitMask == 0) {
                        continue;
                    }
                    var rendererWithSplit = new RendererWithSplit
                    {
                        renderer = renderer,
                        splitMask = splitMask,
                    };
                    if (!forRenderer.TryGetValue(rendererWithSplit, out var data)) {
                        data = new RendererInstancesData(instance, Allocator.Temp);
                    } else {
                        data.takenSlots.Add(instance);
                    }

                    forRenderer[rendererWithSplit] = data;
                }

                foreach (var kvp2 in forRenderer) {
                    instancesByRenderer.TryAdd(kvp2.Key, kvp2.Value);
                }

                forRenderer.Dispose();
            }
        }

        static MetadataValue CreateMetadataValue(int nameID, int gpuOffset, bool isPerInstance) {
            const uint kIsPerInstanceBit = 0x80000000;
            return new MetadataValue
            {
                NameID = nameID,
                Value  = (uint)gpuOffset | (isPerInstance ? (kIsPerInstanceBit) : 0),
            };
        }

        static T* Malloc<T>(uint count) where T : unmanaged {
            return (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<T>(), Allocator.TempJob);
        }

        readonly struct Renderer : IEquatable<Renderer> {
            public readonly BatchMeshID meshId;
            public readonly UnsafeArray<BatchMaterialID> materialIds;

            public Renderer(BatchMeshID meshId, UnsafeArray<BatchMaterialID> materialIds) {
                this.meshId = meshId;
                this.materialIds = materialIds;
            }

            public bool Equals(Renderer other) {
                if (!meshId.Equals(other.meshId)) {
                    return false;
                }
                if (materialIds.IsCreated != other.materialIds.IsCreated) {
                    return false;
                }
                if (materialIds.Length != other.materialIds.Length) {
                    return false;
                }
                for (var i = 0u; i < materialIds.Length; i++) {
                    if (!materialIds[i].Equals(other.materialIds[i])) {
                        return false;
                    }
                }
                return true;
            }

            public override bool Equals(object obj) {
                return obj is Renderer other && Equals(other);
            }

            public override int GetHashCode() {
                unchecked {
                    var hash = meshId.GetHashCode() * 397;
                    if (materialIds.IsCreated) {
                        for (var i = 0u; i < materialIds.Length; i++) {
                            hash = (hash * 397) ^ materialIds[i].GetHashCode();
                        }
                    }

                    return hash;
                }
            }

            public override string ToString() {
                var builder = new StringBuilder();
                foreach (var materialId in materialIds) {
                    builder.Append(materialId.value);
                    builder.Append(",");
                }
                builder.Length -= 1;
                var result = builder.ToString();
                builder.Length = 0;

                return $"[Renderer [{meshId.value}] with materials ({result})]";
            }
        }

        struct RendererWithSplit : IEquatable<RendererWithSplit> {
            public Renderer renderer;
            public ushort splitMask;

            public bool Equals(RendererWithSplit other) {
                if (other.splitMask != splitMask) {
                    return false;
                }
                return renderer.Equals(other.renderer);
            }

            public override bool Equals(object obj) {
                return obj is RendererWithSplit other && Equals(other);
            }

            public override int GetHashCode() {
                unchecked {
                    var hash = (splitMask * 397) ^ renderer.GetHashCode();
                    return hash;
                }
            }
        }

        struct RendererInstancesData {
            public UnsafeList<uint> takenSlots;

            public int RefCount => takenSlots.Length;

            public RendererInstancesData(uint slot, Allocator allocator) {
                takenSlots = new UnsafeList<uint>(4, allocator);
                takenSlots.Add(slot);
            }

            public void Dispose() {
                takenSlots.Dispose();
            }

            public RendererInstancesData Copy() {
                var takenCopy = new UnsafeList<uint>(takenSlots.Ptr, takenSlots.Length);
                return new RendererInstancesData
                {
                    takenSlots = takenCopy,
                };
            }
        }

        struct PackedMatrix {
            public float c0x;
            public float c0y;
            public float c0z;
            public float c1x;
            public float c1y;
            public float c1z;
            public float c2x;
            public float c2y;
            public float c2z;
            public float c3x;
            public float c3y;
            public float c3z;

            public PackedMatrix(Matrix4x4 m) {
                c0x = m.m00;
                c0y = m.m10;
                c0z = m.m20;
                c1x = m.m01;
                c1y = m.m11;
                c1z = m.m21;
                c2x = m.m02;
                c2y = m.m12;
                c2z = m.m22;
                c3x = m.m03;
                c3y = m.m13;
                c3z = m.m23;
            }
        }

        struct InstanceData {
            public uint instanceStartVertex;
            public uint sharedStartVertex;
        }
    }

    public static class SimdExt {
        public static uint SimdTrailing(this uint value) => (value >> 2) << 2;
        public static long SimdTrailing(this long value) => (value >> 2) << 2;
        public static int SimdTrailing(this int value) => (value >> 2) << 2;
    }
}