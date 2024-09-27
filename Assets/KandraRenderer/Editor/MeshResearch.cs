using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KandraRenderer.Editor
{
    public class MeshResearch : EditorWindow
    {
        Mesh _mesh;
        GUIContent _currentMeshInfo = new GUIContent("No mesh selected");
        GUIContent _currentBlendshapeInfo = new GUIContent("No mesh selected");
        GUIStyle _labelStyle;
        Vector2 _scrollPosition;

        int _selectedBlendShapeIndex = -1;
        private Vector3[] _verticesDelta;
        private Vector3[] _normalsDelta;
        private Vector3[] _tangentsDelta;

        private void OnEnable() {
            _labelStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
            };
        }

        private void OnGUI() {
            var newMesh = EditorGUILayout.ObjectField("Mesh", _mesh, typeof(Mesh), false) as Mesh;
            if (newMesh != _mesh) {
                _mesh = newMesh;
                _selectedBlendShapeIndex = -1;
                if (_mesh != null) {
                    CollectMeshInfo();
                } else {
                    _currentMeshInfo.text = "No mesh selected";
                    _currentBlendshapeInfo.text = string.Empty;
                }
            }

            if (_mesh != null) {
                DrawBlendshapeToInspect();
            }

            var width = position.width;
            var height = _labelStyle.CalcHeight(_currentMeshInfo, width);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.LabelField(_currentMeshInfo, _labelStyle, GUILayout.MinHeight(height));
            if (_selectedBlendShapeIndex != -1) {
                height = _labelStyle.CalcHeight(_currentBlendshapeInfo, width);
                EditorGUILayout.LabelField(_currentBlendshapeInfo, _labelStyle, GUILayout.MinHeight(height));
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawBlendshapeToInspect() {
            var blendshapesCount = _mesh.blendShapeCount;
            if (blendshapesCount > 0) {
                var newSelection = EditorGUILayout.IntSlider("Blendshape to inspect:", _selectedBlendShapeIndex, -1, _mesh.blendShapeCount - 1);
                if (newSelection != _selectedBlendShapeIndex) {
                    _selectedBlendShapeIndex = newSelection;
                    CollectMeshInfo();
                    CollectBlenshapeInspection();
                }
            } else {
                _selectedBlendShapeIndex = -1;
                _currentBlendshapeInfo = new GUIContent("No blendshapes");
            }
        }

        unsafe void CollectMeshInfo() {
            var sb = new StringBuilder();
            sb.Append("<b><i>Mesh: ");
            sb.Append(_mesh.name);
            sb.AppendLine("</i></b>");

            var vertexCount = (uint)_mesh.vertexCount;
            sb.Append("<b>Vertices:</b> ");
            sb.Append(vertexCount);
            sb.Append(" * ");

            var vertexStreamsCount = _mesh.vertexBufferCount;
            var vertexSize = 0u;
            for (var i = 0; i < vertexStreamsCount; i++) {
                vertexSize += (uint)_mesh.GetVertexBufferStride(i);
            }
            sb.Append(vertexSize);
            sb.Append(" [bytes] = ");

            var verticesSize = vertexCount * vertexSize;

            sb.Append(HumanReadableBytes(verticesSize));
            sb.AppendLine();

            var bonesCount = (uint)_mesh.bindposeCount;
            var attributesCount = _mesh.vertexAttributeCount;
            for (var i = 0; i < attributesCount; i++) {
                var attribute = _mesh.GetVertexAttribute(i);
                sb.Append("   <b>Attribute ");
                sb.Append(attribute.attribute.ToString());
                sb.Append(":</b> ");
                sb.Append(attribute.format.ToString());
                sb.Append("x");
                sb.Append(attribute.dimension);
                sb.Append(" = ");
                sb.Append(HumanReadableBytes(attribute.dimension * FormatSize(attribute.format)));
                sb.Append(" * ");
                var attributeCount = AttributeCount(attribute.attribute, vertexCount, bonesCount);
                sb.Append(attributeCount);
                sb.Append(" = ");
                sb.Append(HumanReadableBytes(attributeCount * attribute.dimension * FormatSize(attribute.format)));
                sb.Append(" in stream: ");
                sb.Append(attribute.stream);
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.Append("<b>Indices:</b> ");
            var submeshesCount = _mesh.subMeshCount;
            var indexCount = 0u;
            for (var i = 0; i < submeshesCount; i++) {
                indexCount += _mesh.GetIndexCount(i);
            }
            sb.Append(indexCount);
            sb.Append(" * ");

            var indexSize = _mesh.indexFormat == IndexFormat.UInt16 ? sizeof(ushort) : sizeof(uint);
            sb.Append(indexSize);
            sb.Append(" [bytes] = ");

            var indicesSize = indexCount * indexSize;
            sb.Append(HumanReadableBytes(indicesSize));
            sb.Append(" in ");
            sb.Append(submeshesCount);
            sb.AppendLine(" submeshes");
            sb.AppendLine();

            var bonesSize = 0u;
            if (bonesCount > 0) {
                sb.Append("<b>Bones:</b> ");
                sb.Append(bonesCount);
                sb.Append(" * ");
                sb.Append(sizeof(Matrix4x4));
                sb.Append(" [bytes] = ");
                bonesSize = bonesCount * (uint)sizeof(Matrix4x4);
                sb.Append(HumanReadableBytes(bonesSize));
                sb.AppendLine();
                sb.AppendLine();
            }

            var blendshapesCount = _mesh.blendShapeCount;
            var blendshapesSize = 0L;
            if (blendshapesCount > 0) {
                var blendshapeSize = sizeof(Vector3) + sizeof(Vector3) + sizeof(Vector3);
                blendshapesSize = blendshapesCount * blendshapeSize * vertexCount;

                if (_verticesDelta?.Length != vertexCount) {
                    _verticesDelta = new Vector3[vertexCount];
                    _normalsDelta = new Vector3[vertexCount];
                    _tangentsDelta = new Vector3[vertexCount];
                }

                var overallEmptyShapes = 0u;

                sb.Append("<b>Blend shapes:</b> ");
                sb.Append(blendshapesCount);
                sb.Append(" * ");
                sb.Append(blendshapeSize);
                sb.Append(" [bytes] * ");
                sb.Append(vertexCount);
                sb.Append(" = ");
                sb.Append(HumanReadableBytes(blendshapesSize));
                sb.AppendLine();
                for (var i = 0; i < blendshapesCount; i++) {
                    var blendShapeName = _mesh.GetBlendShapeName(i);
                    _mesh.GetBlendShapeFrameVertices(i, 0, _verticesDelta, _normalsDelta, _tangentsDelta);
                    var emptyShapes = 0u;
                    for (var j = 0; j < vertexCount; j++) {
                        if (_verticesDelta[j].sqrMagnitude < 0.001f) {
                            ++emptyShapes;
                        }
                    }

                    var fullShapes = vertexCount - emptyShapes;
                    overallEmptyShapes += emptyShapes;

                    sb.Append("   <b>");
                    if(_selectedBlendShapeIndex == i) {
                        sb.Append("<i>");
                    }
                    sb.Append(blendShapeName);
                    if(_selectedBlendShapeIndex == i) {
                        sb.Append("/<i>");
                    }
                    sb.Append(":</b> ");
                    sb.Append(HumanReadableBytes(blendshapeSize * vertexCount));
                    sb.Append(" uses: ");
                    sb.Append(fullShapes);
                    sb.Append('/');
                    sb.Append(vertexCount);
                    sb.Append(" (");
                    sb.Append((fullShapes * 100f / vertexCount).ToString("F2"));
                    sb.Append("%)");
                    sb.Append(" wasting: ");
                    sb.Append(HumanReadableBytes(blendshapeSize * emptyShapes));
                    sb.AppendLine();
                }

                sb.Append(" <b>* Overall empty shapes:</b> ");
                sb.Append(overallEmptyShapes);
                sb.Append('/');
                sb.Append(vertexCount * blendshapesCount);
                sb.Append(" (");
                sb.Append((overallEmptyShapes * 100f / (vertexCount * blendshapesCount)).ToString("F2"));
                sb.Append("%)");
                sb.Append(" wasting: ");
                sb.AppendLine(HumanReadableBytes(blendshapeSize * overallEmptyShapes));
                sb.AppendLine();
            }

            var totalSize = verticesSize + indicesSize + bonesSize + blendshapesSize;
            sb.Append("<b>Total size:</b> ");
            sb.Append(HumanReadableBytes(totalSize));

            _currentMeshInfo.text = sb.ToString();
        }

        void CollectBlenshapeInspection() {
            var vertexCount = _mesh.vertexCount;
            if (_verticesDelta?.Length != vertexCount) {
                _verticesDelta = new Vector3[vertexCount];
                _normalsDelta = new Vector3[vertexCount];
                _tangentsDelta = new Vector3[vertexCount];
            }
            _mesh.GetBlendShapeFrameVertices(_selectedBlendShapeIndex, 0, _verticesDelta, _normalsDelta, _tangentsDelta);
            var sb = new StringBuilder();
            sb.Append("<b><i>Blendshape: ");
            sb.Append(_mesh.GetBlendShapeName(_selectedBlendShapeIndex));
            sb.AppendLine("</i></b>");
            for (var i = 0; i < vertexCount; i++) {
                sb.Append("   <b>Vertex ");
                sb.Append(i);
                sb.Append(":</b> ");
                sb.Append(_verticesDelta[i]);
                sb.Append(" <b>Normal:</b> ");
                sb.Append(_normalsDelta[i]);
                sb.Append(" <b>Tangent:</b> ");
                sb.Append(_tangentsDelta[i]);
                sb.AppendLine();
            }

            _currentBlendshapeInfo.text = sb.ToString();
        }

        static string HumanReadableBytes(double byteCount) {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0) {
                return "0" + suf[0];
            }
            var place = Convert.ToInt32(Math.Floor(Math.Log(byteCount, 1024)));
            var num = Math.Round(byteCount / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture) + suf[place];
        }

        static uint FormatSize(VertexAttributeFormat format) {
            return format switch {
                VertexAttributeFormat.Float32 => sizeof(float),
                VertexAttributeFormat.Float16 => sizeof(ushort),
                VertexAttributeFormat.UNorm8 => sizeof(byte),
                VertexAttributeFormat.SNorm8 => sizeof(sbyte),
                VertexAttributeFormat.UNorm16 => sizeof(ushort),
                VertexAttributeFormat.SNorm16 => sizeof(short),
                VertexAttributeFormat.UInt8 => sizeof(byte),
                VertexAttributeFormat.SInt8 => sizeof(sbyte),
                VertexAttributeFormat.UInt16 => sizeof(ushort),
                VertexAttributeFormat.SInt16 => sizeof(short),
                VertexAttributeFormat.UInt32 => sizeof(uint),
                VertexAttributeFormat.SInt32 => sizeof(int),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
        }

        static uint AttributeCount(VertexAttribute attribute, uint vertexCount, uint bonesCount) {
            return attribute switch {
                VertexAttribute.Position => vertexCount,
                VertexAttribute.Normal => vertexCount,
                VertexAttribute.Tangent => vertexCount,
                VertexAttribute.Color => vertexCount,
                VertexAttribute.TexCoord0 => vertexCount,
                VertexAttribute.TexCoord1 => vertexCount,
                VertexAttribute.TexCoord2 => vertexCount,
                VertexAttribute.TexCoord3 => vertexCount,
                VertexAttribute.TexCoord4 => vertexCount,
                VertexAttribute.TexCoord5 => vertexCount,
                VertexAttribute.TexCoord6 => vertexCount,
                VertexAttribute.TexCoord7 => vertexCount,
                VertexAttribute.BlendWeight => bonesCount,
                VertexAttribute.BlendIndices => bonesCount,
                _ => throw new ArgumentOutOfRangeException(nameof(attribute), attribute, null)
            };
        }

        [MenuItem("Kandra/MeshResearch")]
        private static void ShowWindow()
        {
            var window = GetWindow<MeshResearch>();
            window.titleContent = new GUIContent("Mesh research");
            window.Show();
        }
    }
}