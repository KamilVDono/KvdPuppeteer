using UnityEngine;

namespace KandraRenderer
{
	public class MemoryDisplay : MonoBehaviour
	{
		void OnGUI()
		{
			KandraRendererManager.Instance.OnGUI();
		}
	}
}
