using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //The flash of the jump itself. Covers everything outside the field contour, leaving what the field holds untouched.
    public class FzJumpFlash : MonoBehaviour
    {
        private static readonly Color FlashColor = SpriteUtil.ColorFromHex("#B8CCE8FF");
        private static readonly string FlashShader = "Sprites/Default";
        public static readonly float Duration = 0.5f;
        private static readonly float RiseShare = 0.3f; //Share of the duration spent reaching full brightness
        private static readonly float FarDistance = 5000f; //Tiles from the contour's center, past any screen edge

        private static FzJumpFlash instance;

        public static bool Playing => instance != null;

        //Plays from the start, false if there is no contour to leave clear
        public static bool Play(Ship ship)
        {
            return Play(FieldContour.SmoothAdaptive(FieldContour.Calculate(ship)));
        }

        public static bool Play(IList<Vector2> curve)
        {
            Stop();

            if (curve == null || curve.Count == 0 || FzOverlayCamera.Get() == null) return false;

            Shader shader = Shader.Find(FlashShader);
            if (shader == null)
            {
                FzAdditions.Logger.LogError("Could not find shader " + FlashShader);
                return false;
            }

            GameObject flash = new GameObject("FzJumpFlash", typeof(MeshFilter), typeof(MeshRenderer));
            flash.layer = FzOverlayCamera.OverlayLayer;
            instance = flash.AddComponent<FzJumpFlash>();
            instance.Init(curve, shader);
            return true;
        }

        public static void Stop()
        {
            if (instance == null) return;
            Destroy(instance.gameObject);
            instance = null;
        }

        private Mesh mesh;
        private Material material;
        private float elapsed;

        private void Init(IList<Vector2> curve, Shader shader)
        {
            mesh = CreateMesh(curve);
            material = new Material(shader);
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterial = material;
            SetAlpha(0f);
        }

        //A closed band from each contour point straight out from the center, the contour's inside is simply not covered
        private static Mesh CreateMesh(IList<Vector2> curve)
        {
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in curve) center += point;
            center /= curve.Count;

            int count = curve.Count;
            Vector3[] vertices = new Vector3[count * 2];
            int[] triangles = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                vertices[i * 2] = curve[i];
                vertices[i * 2 + 1] = center + (curve[i] - center).normalized * FarDistance;

                triangles[i * 6] = i * 2;
                triangles[i * 6 + 1] = i * 2 + 1;
                triangles[i * 6 + 2] = next * 2;
                triangles[i * 6 + 3] = next * 2;
                triangles[i * 6 + 4] = i * 2 + 1;
                triangles[i * 6 + 5] = next * 2 + 1;
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            return mesh;
        }

        private void LateUpdate()
        {
            elapsed += FzJumpEffect.ShownTime(); //The same at any game speed, at 16x a game-time flash is gone in a frame or two
            float progress = elapsed / Duration;
            if (progress >= 1f)
            {
                Stop();
                return;
            }

            float alpha;
            if (progress < RiseShare)
            {
                alpha = progress / RiseShare;
            }
            else
            {
                float decay = 1f - (progress - RiseShare) / (1f - RiseShare);
                alpha = decay * decay;
            }
            SetAlpha(alpha);
        }

        private void SetAlpha(float alpha)
        {
            Color color = FlashColor;
            color.a = alpha;
            material.color = color;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
