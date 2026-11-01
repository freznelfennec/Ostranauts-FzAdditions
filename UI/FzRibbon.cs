using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //A closed glowing band around a loop of points. LineRenderer does not weld the end of a loop to its start,
    //which additive materials show as a seam, here the last section shares the first section's vertices.
    public class FzRibbon : MonoBehaviour
    {
        public static FzRibbon Create(string name, Material material, int pointCount)
        {
            GameObject ribbonObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            ribbonObject.layer = FzOverlayCamera.OverlayLayer;
            ribbonObject.GetComponent<MeshRenderer>().sharedMaterial = material;

            FzRibbon ribbon = ribbonObject.AddComponent<FzRibbon>();
            ribbon.Init(pointCount);
            return ribbon;
        }

        private const int VerticesAcross = 3;

        private Mesh mesh;
        private Vector3[] vertices;
        private Vector2[] uvs;
        private Color32[] colors;

        private void Init(int pointCount)
        {
            //Three vertices across at each point, outer edge, center and inner edge
            vertices = new Vector3[pointCount * VerticesAcross];
            uvs = new Vector2[pointCount * VerticesAcross];
            colors = new Color32[pointCount * VerticesAcross];

            int[] triangles = new int[pointCount * 12];
            for (int i = 0; i < pointCount; i++)
            {
                int next = (i + 1) % pointCount;
                for (int strip = 0; strip < VerticesAcross - 1; strip++)
                {
                    int first = (i * 2 + strip) * 6;
                    int vertex = i * VerticesAcross + strip;
                    int nextVertex = next * VerticesAcross + strip;
                    triangles[first] = vertex;
                    triangles[first + 1] = nextVertex;
                    triangles[first + 2] = vertex + 1;
                    triangles[first + 3] = nextVertex;
                    triangles[first + 4] = nextVertex + 1;
                    triangles[first + 5] = vertex + 1;
                }
            }

            mesh = new Mesh();
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        //Sets the band distance out from the points along their normals, all in world units. The band stops at
        //innerLimits and outerLimits, past those it would cross over itself on a tight bend. The texture is cut off
        //there rather than squeezed in, so the glow stays centered on the band.
        public void SetShape(Vector2[] points, Vector2[] normals, float[] innerLimits, float[] outerLimits, float distance, float width)
        {
            for (int i = 0; i < points.Length; i++)
            {
                for (int across = 0; across < VerticesAcross; across++)
                {
                    float wanted = distance + width * (0.5f - 0.5f * across);
                    float reach = Mathf.Clamp(wanted, -innerLimits[i], outerLimits[i]);
                    vertices[i * VerticesAcross + across] = points[i] + normals[i] * reach;

                    //The texture runs across the band, 1 on the outer edge and 0 on the inner
                    uvs[i * VerticesAcross + across] = new Vector2(0f, 0.5f + (reach - distance) / width);
                }
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
        }

        public void SetAlpha(float alpha)
        {
            Color32 color = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            mesh.colors32 = colors;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
