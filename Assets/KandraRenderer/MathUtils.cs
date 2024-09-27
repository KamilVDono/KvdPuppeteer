using Unity.Mathematics;

namespace KandraRenderer {
    public static class MathUtils {
        const float StereographicScale = 1.7777f;

        public static uint EncodeNormalVectorSpheremap(float3 vector) {
            var p = math.sqrt(vector.z*8f+8f);

            var encodedFloatVector = vector.xy / p + 0.5f;

            var encodedHalfVector = math.f32tof16(encodedFloatVector);

            return encodedHalfVector.x | (encodedHalfVector.y << 16);
        }

        public static float3 DecodeNormalVectorSpheremap(uint encodedVector) {
            var halfX = encodedVector & 0xFFFF;
            var halfY = encodedVector >> 16;

            var encodedFloat = math.f16tof32(new uint2(halfX, halfY));

            var fenc = encodedFloat*4f-2f;

            var f = math.dot(fenc.xy, fenc.xy);
            var g = math.sqrt(1f-f/4f);
            var decoded = default(float3);
            decoded.xy = fenc.xy*g;
            decoded.z = 1f-f/2f;
            return decoded;
        }

        public static uint EncodeNormalVectorStereographic(float3 vector) {
            var enc = vector.xy / (vector.z+1f);
            enc /= StereographicScale;
            var encodedFloatVector = enc*0.5f+0.5f;

            var encodedHalfVector = math.f32tof16(encodedFloatVector);

            return encodedHalfVector.x | (encodedHalfVector.y << 16);
        }

        public static float3 DecodeNormalVectorStereographic(uint encodedVector) {
            var halfX = encodedVector & 0xFFFF;
            var halfY = encodedVector >> 16;

            var encodedFloat = new float4(math.f16tof32(new uint2(halfX, halfY)), 0, 0);

            var nn = encodedFloat.xyz * new float3(2*StereographicScale, 2*StereographicScale, 0) + new float3(-StereographicScale, -StereographicScale, 1);
            var g = 2.0f / math.dot(nn.xyz,nn.xyz);
            var n = default(float3);
            n.xy = g*nn.xy;
            n.z = g-1f;
            return n;
        }

        public static uint EncodeNormalVectorOctahedron(float3 vector) {
            var n = vector / (math.abs(vector.x) + math.abs(vector.y) + math.abs(vector.z));
            var encodedFloatVector = n.z >= 0.0f ? n.xy : OctWrap(n.xy);
            encodedFloatVector = encodedFloatVector * 0.5f + 0.5f;

            var encodedHalfVector = math.f32tof16(encodedFloatVector);

            return encodedHalfVector.x | (encodedHalfVector.y << 16);
        }

        public static float3 DecodeNormalVectorOctahedron(uint encodedVector) {
            var halfX = encodedVector & 0xFFFF;
            var halfY = encodedVector >> 16;

            var encodedFloat = math.f16tof32(new uint2(halfX, halfY));

            var f = encodedFloat * 2.0f - 1.0f;

            // https://twitter.com/Stubbesaurus/status/937994790553227264
            var n = new float3(f.x, f.y, 1.0f - math.abs(f.x) - math.abs(f.y));
            var t = math.saturate(-n.z);
            n.xy += math.select(t, -t, n.xy >= 0.0f);
            return math.normalize(n);
        }

        public static uint2 EncodeNormalAndTangent(float3 normal, float3 tangent) {
            var encodedNormal = EncodeNormalVectorOctahedron(normal);
            var encodedTangent = EncodeNormalVectorOctahedron(tangent);
            return new uint2(encodedNormal, encodedTangent);
        }

        public static void DecodeNormalAndTangent(uint2 normalAndTangent, out float3 normal, out float3 tangent) {
            normal = DecodeNormalVectorOctahedron(normalAndTangent.x);
            tangent = DecodeNormalVectorOctahedron(normalAndTangent.y);
        }

        static float2 OctWrap(float2 v) {
            return (1.0f - math.abs(v.yx)) * math.select(-1.0f, 1.0f, v.xy >= 0.0f);
        }
    }
}