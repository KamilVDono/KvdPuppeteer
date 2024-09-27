using UnityEditor;

namespace KVD.Puppeteer.Editor
{
	[CustomEditor(typeof(Puppet))]
	public class PuppetEditor : UnityEditor.Editor
	{
		public override unsafe void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			var player = (Puppet)target;
			var access = new Puppet.EditorAccess(player);
		}
	}
}
