using KVD.Puppeteer.Managers;
using UnityEditor;
using UnityEngine;

namespace KVD.Puppeteer.Editor
{
	[CustomEditor(typeof(TransitionsPreview))]
	public class TransitionsPreviewEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			var preview = (TransitionsPreview)target;
			var access = new TransitionsPreview.EditorAccess(preview);

			var puppet = preview.GetComponent<Puppet>();
			var puppetIndex = puppet?.Slot ?? unchecked((uint)-1);

			if (puppetIndex != unchecked((uint)-1) && access.Clip && GUILayout.Button("Start Transition"))
			{
				var clipId = ClipsManager.Instance.RegisterClip(access.Clip.guid);
				var stateId = StatesManager.Instance.AddClipState(puppetIndex, clipId);
				PuppeteerManager.Instance.TransitionsManager.StartTransition(puppetIndex, stateId, access.Duration);
			}
		}
	}
}
