using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;

namespace AclUnity
{
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct Qvvs
    {
        [FieldOffset(00)] public quaternion rotation; // All qvvs operations assume this value is normalized
        [FieldOffset(16)] public float3 position;
        [FieldOffset(28)] public int userDefined; // User-define-able, can be used for floating origin or something
        [FieldOffset(32)] public float3 stretch;
        [FieldOffset(44)] public float scale;

        public static readonly Qvvs identity = new Qvvs
        {
            position = float3.zero,
            rotation = quaternion.identity,
            scale = 1f,
            stretch = 1f,
            userDefined = 0
        };

        public Qvvs(float3 position, quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
            scale = 1f;
            stretch = 1f;
            userDefined = 0;
        }

        public Qvvs(float3 position, quaternion rotation, float3 unityScale)
        {
            this.position = position;
            this.rotation = rotation;
            scale = 1f;
            stretch = unityScale;
            userDefined = 0;
        }

        public Qvvs(float3 position, quaternion rotation, float scale, float3 stretch, int userDefined = 0)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
            this.stretch = stretch;
            this.userDefined = userDefined;
        }

        public Qvvs(RigidTransform rigidTransform)
        {
            position = rigidTransform.pos;
            rotation = rigidTransform.rot;
            scale = 1f;
            stretch = 1f;
            userDefined = 0;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct ClipHeader
    {
        public enum ClipType : byte
        {
            Skeleton,
            SkeletonWithUniformScales,
            Scalars
        }

        [FieldOffset(0)] public ClipType clipType;
        [FieldOffset(2)] public short trackCount;
        [FieldOffset(4)] public float sampleRate;
        [FieldOffset(8)] public float duration;
        [FieldOffset(12)] public uint offsetToUniformScalesStartInBytes;

        public static unsafe ClipHeader Read(void* compressedClip)
        {
            Decompression.CheckCompressedClipIsValid(compressedClip);
            return *(ClipHeader*)compressedClip;
        }
    }

    public struct SemanticVersion
    {
        public short major;
        public short minor;
        public short patch;
        public bool isPreview;

        public bool IsValid => major > 0 || minor > 0 || patch > 0;
        public bool IsUnrecognized => major == -1 && minor == -1 && patch == -1;
    }

    [BurstCompile]
    public static class AclUnityCommon
    {
        public static SemanticVersion GetVersion()
        {
            int version = 0;
            if (X86.Avx2.IsAvx2Supported)
                version = AVX.getVersion();
            else
            {
                //UnityEngine.Debug.Log("Fetched without AVX");
                version = NoExtensions.getVersion();
            }

            if (version == -1)
                return new SemanticVersion { major = -1, minor = -1, patch = -1 };

            short patch = (short)(version & 0x3ff);
            short minor = (short)((version >> 10) & 0x3ff);
            short major = (short)((version >> 20) & 0x3ff);
            bool isPreview = patch > 500;
            patch = isPreview ? (short)(patch-500) : patch;
            return new SemanticVersion { major = major, minor = minor, patch = patch, isPreview = isPreview };
        }

        public static SemanticVersion GetUnityVersion()
        {
            int version = 0;
            if (X86.Avx2.IsAvx2Supported)
                version = AVX.getVersion();
            else
            {
                //UnityEngine.Debug.Log("Fetched without AVX");
                version = NoExtensions.getVersion();
            }

            if (version == -1)
                return new SemanticVersion { major = -1, minor = -1, patch = -1 };

            short patch = (short)(version & 0x3ff);
            short minor = (short)((version >> 10) & 0x3ff);
            short major = (short)((version >> 20) & 0x3ff);
            bool isPreview = patch > 500;
            patch = isPreview ? (short)(patch-500) : patch;
            return new SemanticVersion { major = major, minor = minor, patch = patch, isPreview = isPreview };
        }

        public static string GetPluginName()
        {
            if (X86.Avx2.IsAvx2Supported)
                return dllNameAVX;
            return dllName;
        }

        internal const string dllName = "AclUnity";
        internal const string dllNameAVX = "AclUnity_AVX";

        static class NoExtensions
        {
            [DllImport(dllName)]
            public static extern int getVersion();
            [DllImport(dllName)]
            public static extern int getUnityVersion();
        }

        static class AVX
        {
            [DllImport(dllNameAVX)]
            public static extern int getVersion();
            [DllImport(dllNameAVX)]
            public static extern int getUnityVersion();
        }
    }
}

