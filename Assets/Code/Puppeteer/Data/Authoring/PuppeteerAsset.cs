using KVD.Utils.DataStructures;
using UnityEngine;

namespace KVD.Puppeteer.Data.Authoring
{
	public class PuppeteerAsset<T> : ScriptableObject where T : unmanaged
	{
		public SerializableGuid guid;

		public override string ToString()
		{
			return guid.ToString();
		}

		public static implicit operator SerializableGuid(PuppeteerAsset<T> asset) => asset.guid;
	}
}
