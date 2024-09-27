using KVD.Utils.Maths;
using Unity.Mathematics;

namespace KandraRenderer
{
    public struct Bone
    {
        public float3x4 boneTransform;

        public Bone(float3x4 boneTransform)
        {
            this.boneTransform = boneTransform;
        }

        public Bone(float4x4 boneTransform)
        {
            this.boneTransform = boneTransform.orthonormal();
        }
    }
}