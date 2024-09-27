using UnityEditor;
using UnityEngine;

namespace KandraRenderer.Editor
{
    [CustomEditor(typeof(KandraRenderer))]
    public class KandraRendererEditor : UnityEditor.Editor
    {
        private bool _showBlendshapes;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var renderer = (KandraRenderer) target;
            var rendererData = renderer.rendererData;
            if (rendererData.blendshapeWeights.IsCreated && rendererData.mesh.blendshapesNames.Length > 0)
            {
                _showBlendshapes = EditorGUILayout.Foldout(_showBlendshapes, "Blendshapes");
                if (_showBlendshapes)
                {
                    ++EditorGUI.indentLevel;
                    for (var i = 0u; i < rendererData.blendshapeWeights.Length; i++)
                    {
                        var weight = rendererData.blendshapeWeights[i];
                        EditorGUI.BeginChangeCheck();
                        var newWeight = EditorGUILayout.Slider(rendererData.mesh.blendshapesNames[i], weight, 0, 1);
                        if (EditorGUI.EndChangeCheck())
                        {
                            rendererData.blendshapeWeights[i] = newWeight;
                            if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
                            {
                                SceneView.lastActiveSceneView.Repaint();
                            }
                        }
                    }
                    --EditorGUI.indentLevel;
                }
            }
        }
    }
}