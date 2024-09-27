using System.Text;
using KVD.Utils.DataStructures;
using Unity.Collections;
using UnityEngine;

using static KandraRenderer.KandraRendererManager;

namespace KandraRenderer
{
    public unsafe class BonesManager
    {
        const int SkinBonesCapacity = KandraRendererManager.SkinBonesCapacity;
        const int MaxRenderers = KandraRendererManager.RenderersCapacity;

        static readonly int BonesId = Shader.PropertyToID("_Bones");
        static readonly int SkinningBonesDataId = Shader.PropertyToID("_SkinningBonesData");
        static readonly int SkinBonesId = Shader.PropertyToID("_SkinBones");
        static readonly int BonesCountId = Shader.PropertyToID("bonesCount");

        readonly ComputeShader _skinningComputeShader;
        readonly ComputeShader _prepareBonesShader;
        readonly int _prepareBonesKernel;
        readonly int _skinningKernel;
        readonly uint _xGroupSize;

        GraphicsBuffer _skinningBonesDataBuffer;
        GraphicsBuffer _skinBonesBuffer;

        UnsafeArray<RegisteredRenderer> _registeredRenderers;
        MemoryBookkeeper _memoryRegions;

        bool _frameInFlight;

        uint BonesCount => _memoryRegions.LastBinStart;

        public void OnGUI(StringBuilder sb, ref double used, ref double total)
        {
            sb.AppendLine(nameof(BonesManager));

            LogBuffer(sb, _skinningBonesDataBuffer, "SkinningBonesDataBuffer", BonesCount,  ref used, ref total);
            LogBuffer(sb, _skinBonesBuffer, "SkinBonesBuffer", BonesCount, ref used, ref total);
        }

        public BonesManager(ComputeShader skinningShader, ComputeShader prepareBonesShader) {
            _prepareBonesShader = prepareBonesShader;
            _prepareBonesKernel = _prepareBonesShader.FindKernel("CSPrepareBones");
            _prepareBonesShader.GetKernelThreadGroupSizes(_prepareBonesKernel, out _xGroupSize, out _, out _);

            _skinningComputeShader = skinningShader;
            _skinningKernel = _skinningComputeShader.FindKernel("CSSkinning");

            _skinningBonesDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SkinBonesCapacity, sizeof(SkinningBoneData));
            _skinBonesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SkinBonesCapacity, sizeof(Bone));

            _registeredRenderers = new UnsafeArray<RegisteredRenderer>(MaxRenderers, Allocator.Persistent);
            _memoryRegions = new MemoryBookkeeper("Skin bones", SkinBonesCapacity, MaxRenderers/3, Allocator.Persistent);

            EnsureBuffers();
        }

        public void Dispose() {
            _skinningBonesDataBuffer?.Dispose();
            _skinBonesBuffer?.Dispose();

            if (_registeredRenderers.IsCreated) {
                _registeredRenderers.Dispose();
                _memoryRegions.Dispose();
            }
        }

        public uint Register(uint slot, ushort[] boneIndices, MemoryBookkeeper.MemoryRegion rigMemory, MemoryBookkeeper.MemoryRegion bindPosesMemory) {
            Debug.Assert(bindPosesMemory.length == boneIndices.Length);

            _memoryRegions.Take((uint)boneIndices.Length, out var rendererBonesRegion);

            _registeredRenderers[slot] = new RegisteredRenderer
            {
                memory = rendererBonesRegion
            };

            UpdateBonesDataBuffer(boneIndices, rigMemory, bindPosesMemory, rendererBonesRegion);
            return rendererBonesRegion.start;
        }

        public void Unregister(uint slot) {
            var renderer = _registeredRenderers[slot];
            _memoryRegions.Return(renderer.memory);
        }

        public void RigChanged(uint slot, ushort[] bones, MemoryBookkeeper.MemoryRegion rigRegion, MemoryBookkeeper.MemoryRegion meshRegionBindPosesMemory) {
            var renderer = _registeredRenderers[slot];
            UpdateBonesDataBuffer(bones, rigRegion, meshRegionBindPosesMemory, renderer.memory);
        }

        public void EnsureBuffers() {
            _prepareBonesShader.SetBuffer(_prepareBonesKernel, SkinningBonesDataId, _skinningBonesDataBuffer);
            _prepareBonesShader.SetBuffer(_prepareBonesKernel, SkinBonesId, _skinBonesBuffer);
            _prepareBonesShader.SetInt(BonesCountId, (int)BonesCount);

            _skinningComputeShader.SetBuffer(_skinningKernel, BonesId, _skinBonesBuffer);
        }

        public void RunComputeShader()
        {
            var bonesCount = BonesCount;
            if (bonesCount > 0)
            {
                _prepareBonesShader.Dispatch(_prepareBonesKernel, Mathf.CeilToInt((float)BonesCount / _xGroupSize), 1, 1);
            }
        }

        void UpdateBonesDataBuffer(ushort[] boneIndices, MemoryBookkeeper.MemoryRegion rigMemory, MemoryBookkeeper.MemoryRegion bindPosesMemory, MemoryBookkeeper.MemoryRegion rendererBonesRegion)
        {
            var bonesData = new NativeArray<SkinningBoneData>(boneIndices.Length, Allocator.Temp);
            for (var i = 0; i < boneIndices.Length; i++)
            {
                bonesData[i] = new SkinningBoneData
                {
                    boneIndex = rigMemory.start + boneIndices[i],
                    bindPoseIndex = (uint)(bindPosesMemory.start + i)
                };
            }

            _skinningBonesDataBuffer.SetData(bonesData, 0, (int)rendererBonesRegion.start, bonesData.Length);
            bonesData.Dispose();
        }

        struct SkinningBoneData
        {
            public uint boneIndex;
            public uint bindPoseIndex;
        }

        struct RegisteredRenderer
        {
            public MemoryBookkeeper.MemoryRegion memory;
        }
    }
}