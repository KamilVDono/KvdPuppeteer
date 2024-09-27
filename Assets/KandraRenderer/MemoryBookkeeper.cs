using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace KandraRenderer {
    [BurstCompile]
    public unsafe struct MemoryBookkeeper {
        FixedString32Bytes _name;
        UnsafeList<MemoryRegion> _memoryRegions;

        public uint LastBinStart => _memoryRegions[^1].start;

        public MemoryBookkeeper(string name, uint capacity, int stateInitialCapacity, Allocator allocator) {
            _name = name;
            _memoryRegions = new UnsafeList<MemoryRegion>(stateInitialCapacity, allocator);
            _memoryRegions.Add(new MemoryRegion {start = 0, length = capacity});
        }

        public void Dispose() {
            _memoryRegions.Dispose();
        }

        public bool Take(uint requiredLength, out MemoryRegion takenRegion) {
            var regionIndex = FindMemoryRegionIndex(requiredLength);
            if(regionIndex == -1) {
                takenRegion = default;
                Debug.LogError($"Cannot allocate {requiredLength} bytes for {_name}");
                return false;
            }

            ref var region = ref _memoryRegions.Ptr[regionIndex];

            takenRegion = new MemoryRegion
            {
                start = region.start,
                length = requiredLength
            };
            region.start += requiredLength;
            region.length -= requiredLength;
            if (!region.IsValid) {
                _memoryRegions.RemoveAt(regionIndex);
            }

            return true;
        }

        public void Return(in MemoryRegion returnedRegion) {
            AddEmptyRegion(returnedRegion, ref _memoryRegions);
        }

        int FindMemoryRegionIndex(uint minSize) {
            for (var i = 0; i < _memoryRegions.Length; i++) {
                if (_memoryRegions.Ptr[i].length >= minSize) {
                    return i;
                }
            }

            return -1;
        }

        [BurstCompile]
        static void AddEmptyRegion(in MemoryRegion rendererMemory, ref UnsafeList<MemoryRegion> memoryRegions) {
            var added = false;
            for (var i = 0; !added & (i < memoryRegions.Length); i++) {
                ref var region = ref *(memoryRegions.Ptr + i);
                if (region.start >= rendererMemory.End) {
                    memoryRegions.InsertRange(i, 1);
                    memoryRegions.Ptr[i] = rendererMemory;
                    added = true;
                }
            }

            if (!added) {
                memoryRegions.Add(rendererMemory);
            }
            ConsolidateMemoryRegions(ref memoryRegions);
        }

        static void ConsolidateMemoryRegions(ref UnsafeList<MemoryRegion> memoryRegions) {
            for (var i = 0; i < memoryRegions.Length - 1; i++) {
                ref var region = ref *(memoryRegions.Ptr + i);
                ref var nextRegion = ref *(memoryRegions.Ptr + i + 1);
                if (region.End == nextRegion.start) {
                    region.length += nextRegion.length;
                    memoryRegions.RemoveAt(i + 1);
                    i--;
                }
            }
        }

        public struct MemoryRegion {
            public uint start;
            public uint length;

            public uint End => start + length;
            public bool IsValid => length > 0;
        }
    }
}