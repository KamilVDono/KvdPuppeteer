using System.Text;
using AclUnity;
using KVD.Puppeteer;
using KVD.Puppeteer.Managers;
using KVD.Utils.DataStructures;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using static KandraRenderer.KandraRendererManager;

namespace KandraRenderer {
    public unsafe class RigManager {
        const int RigBonesCapacity = KandraRendererManager.RigBonesCapacity;
        const int MaxRenderers = KandraRendererManager.RenderersCapacity;

        static readonly int InputBonesId = Shader.PropertyToID("_InputBones");

        readonly int _prepareBonesKernel;
        readonly ComputeShader _prepareBonesShader;

        GraphicsBuffer _inputBonesBuffer;
        JobHandle _readTransform;

        UnsafeArray<BonesData> _transfroms;
        UnsafeBitmask _takenSlots;

        MemoryBookkeeper _memoryRegions;
        UnsafeHashMap<int, RigData> _rigs;
        bool _frameInFlight;

        uint BonesCount => _memoryRegions.LastBinStart;

        public void OnGUI(StringBuilder sb, ref double used, ref double total) {
            sb.AppendLine(nameof(RigManager));
            LogBuffer(sb, _inputBonesBuffer, "InputBonesBuffer", BonesCount, ref used, ref total);
        }

        public RigManager(ComputeShader prepareBonesShader) {
            _prepareBonesShader = prepareBonesShader;
            _prepareBonesKernel = _prepareBonesShader.FindKernel("CSPrepareBones");

            _inputBonesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, (int)RigBonesCapacity, sizeof(Bone));

            _transfroms = new UnsafeArray<BonesData>(MaxRenderers, Allocator.Persistent);
            _takenSlots = new UnsafeBitmask(MaxRenderers, Allocator.Persistent);
            _memoryRegions = new MemoryBookkeeper("Bones rig", RigBonesCapacity, MaxRenderers/3, Allocator.Persistent);
            _rigs = new UnsafeHashMap<int, RigData>(MaxRenderers, Allocator.Persistent);

            EnsureBuffers();
        }

        public void Dispose() {
            _readTransform.Complete();
            _transfroms.Dispose();
            _takenSlots.Dispose();
            _inputBonesBuffer?.Dispose();
            _memoryRegions.Dispose();
            _rigs.Dispose();
        }

        public MemoryBookkeeper.MemoryRegion RegisterRig(KandraRig rig) {
            var hash = rig.GetHashCode();

            if (_rigs.TryGetValue(hash, out var data)) {
                data.refCount++;
                _rigs[hash] = data;
                return data.memory;
            }

            var bones = rig.virtualBones.Skeleton.localToWorlds;
            _memoryRegions.Take(bones.Length, out var region);

            var slot = _takenSlots.FirstZero();
            _transfroms[slot] = new BonesData()
            {
                inputTransforms = bones,
                startIndex = (int)region.start
            };
            _takenSlots.Up((uint)slot);

            data = new RigData
            {
                memory = region,
                refCount = 1,
                slot = (uint)slot
            };
            _rigs[hash] = data;

            return region;
        }

        public void UnregisterRig(KandraRig rig) {
            var hash = rig.GetHashCode();

            if (_rigs.TryGetValue(hash, out var data)) {
                data.refCount--;
                if (data.refCount == 0) {
                    _memoryRegions.Return(data.memory);
                    _rigs.Remove(hash);
                    _takenSlots.Down(data.slot);
                    _transfroms[data.slot] = default;
                } else {
                    _rigs[hash] = data;
                }
            } else {
                Debug.LogError("Trying to unregister a rig that was not registered.", rig);
            }
        }

        public MemoryBookkeeper.MemoryRegion RigChanged(KandraRig rig) {
            var hash = rig.GetHashCode();

            if (_rigs.TryGetValue(hash, out var oldData)) {
                _memoryRegions.Return(oldData.memory);
                _rigs.Remove(hash);

                var newRegion = RegisterRig(rig);

                var data = _rigs[hash];
                data.refCount = oldData.refCount;
                _rigs[hash] = data;

                return newRegion;
            } else {
                Debug.LogError("Trying to change a rig that was not registered.", rig);
                return default;
            }
        }

        public void EnsureBuffers() {
            _prepareBonesShader.SetBuffer(_prepareBonesKernel, InputBonesId, _inputBonesBuffer);
        }

        public void CollectBoneMatrices() {
            var bonesCount = BonesCount;
            if ((bonesCount > 0) & (!_frameInFlight))
            {
                var dependency = PuppeteerManager.Instance.AnimationsJobHandle;
                _readTransform = new ReadTransforms
                {
                    transfroms = _transfroms,
                    bonesBuffer = (Bone*)_inputBonesBuffer.LockBufferForWrite<Bone>(0, (int)bonesCount).GetUnsafePtr()
                }.Schedule(_takenSlots.LastOne()+1, 2, dependency);
                _frameInFlight = true;
            }
        }

        public void UnlockBuffer() {
            if (_frameInFlight) {
                _readTransform.Complete();
                _inputBonesBuffer.UnlockBufferAfterWrite<Bone>((int)BonesCount);
                _frameInFlight = false;
            }
        }

        struct RigData {
            public MemoryBookkeeper.MemoryRegion memory;
            public int refCount;
            public uint slot;
        }

        struct BonesData
        {
            public UnsafeArray<float3x4> inputTransforms;
            public int startIndex;
        }

        [BurstCompile]
        struct ReadTransforms : IJobParallelFor {
            public UnsafeArray<BonesData> transfroms;

            [NativeDisableUnsafePtrRestriction] public Bone* bonesBuffer;

            public void Execute(int index) {
                var transform = transfroms[index];
                if (!transform.inputTransforms.IsCreated)
                {
                    return;
                }

                var dst = bonesBuffer+transform.startIndex;
                var bytesCount = transform.inputTransforms.Length*UnsafeUtility.SizeOf<Bone>();
                UnsafeUtility.MemCpy(dst, transform.inputTransforms.Ptr, bytesCount);
            }
        }
    }
}