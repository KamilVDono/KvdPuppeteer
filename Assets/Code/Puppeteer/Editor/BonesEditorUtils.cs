using System.Text;
using Unity.Mathematics;

namespace KVD.Puppeteer.Editor
{
	public static class BonesEditorUtils
	{
		public static void AppendPosition(this StringBuilder sb, float3 position)
		{
			sb.Append("P(");
			sb.Append(position.x.ToString("000.000"));
			sb.Append(", ");
			sb.Append(position.y.ToString("000.000"));
			sb.Append(", ");
			sb.Append(position.z.ToString("000.000"));
			sb.Append(")");
		}

		public static void AppendRotation(this StringBuilder sb, quaternion rotation)
		{
			var euler = math.degrees(math.Euler(rotation));
			sb.Append("R(");
			sb.Append(euler.x.ToString("000.0"));
			sb.Append(", ");
			sb.Append(euler.y.ToString("000.0"));
			sb.Append(", ");
			sb.Append(euler.z.ToString("000.0"));
			sb.Append(")[");
			sb.Append(rotation.value.x.ToString("0.000"));
			sb.Append(", ");
			sb.Append(rotation.value.y.ToString("0.000"));
			sb.Append(", ");
			sb.Append(rotation.value.z.ToString("0.000"));
			sb.Append(", ");
			sb.Append(rotation.value.w.ToString("0.000"));
			sb.Append("]");
		}

		public static void AppendScale(this StringBuilder sb, float3 scale)
		{
			sb.Append("S(");
			sb.Append(scale.x.ToString("0.000"));
			sb.Append(", ");
			sb.Append(scale.y.ToString("0.000"));
			sb.Append(", ");
			sb.Append(scale.z.ToString("0.000"));
			sb.Append(")");
		}
	}
}
