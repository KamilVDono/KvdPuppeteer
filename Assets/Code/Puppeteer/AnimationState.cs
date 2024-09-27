namespace KVD.Puppeteer
{
	public struct AnimationState
	{
		byte _header;
		public ushort data;
		public uint puppet;
		// TODO: Maybe half instead of float?
		public float blend;
		public float time;

		public AnimationType Type => (AnimationType)(_header & 0b00000001);

		public AnimationState(AnimationType type, ushort data, uint puppet)
		{
			_header = (byte)type;
			this.data = data;
			this.puppet = puppet;
			blend = 0;
			time = 0;
		}

		public enum AnimationType : byte
		{
			Clip = 0,
			BlendTree = 1,
		}
	}
}
