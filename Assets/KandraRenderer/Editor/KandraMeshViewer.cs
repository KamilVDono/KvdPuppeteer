using System;
using UnityEditor;
using UnityEngine;

namespace KandraRenderer.Editor
{
    public class KandraMeshViewer : EditorWindow
    {
        KandraMesh _mesh;
        private uint vertexId;

        [MenuItem("Kandra/Mesh viewer")]
        private static void ShowWindow()
        {
            var window = GetWindow<KandraMeshViewer>();
            window.titleContent = new GUIContent("Mesh viewer");
            window.Show();
        }

        private void OnGUI()
        {
            _mesh = (KandraMesh)EditorGUILayout.ObjectField("Mesh", _mesh, typeof(KandraMesh), false);
            if (_mesh == null)
            {
                return;
            }

            _mesh.AssignMeshData(KandraRendererManager.Instance.StreamingManager.LoadMeshData(_mesh));
            vertexId = (uint)EditorGUILayout.IntSlider("Vertex id", (int)vertexId, 0, _mesh.vertexCount - 1);
            EditorGUILayout.LabelField("Vertex position", _mesh.vertices[vertexId].position.ToString());
            EditorGUILayout.LabelField("Vertex normal", _mesh.vertices[vertexId].normal.ToString());
            EditorGUILayout.LabelField("Vertex tangent", _mesh.vertices[vertexId].tangent.ToString());
            EditorGUILayout.LabelField("Vertex uv", _mesh.additionalData[vertexId].uv.ToString());
            var boneWeights = _mesh.boneWeights[vertexId];
            EditorGUILayout.LabelField("Bone weights", boneWeights.ToString());
        }
    }
}