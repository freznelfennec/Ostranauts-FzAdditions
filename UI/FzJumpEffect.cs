using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Jump drive charge-up. Rings of the contour's shape collapse inward onto it, faster and brighter
    //as the charge builds, while the contour itself starts to glow.
    public class FzJumpEffect : MonoBehaviour
    {
        private static readonly Color GlowColor = SpriteUtil.ColorFromHex("#9CC4FFFF");
        private static readonly string GlowShader = "Legacy Shaders/Particles/Additive";
        private static readonly int GlowTextureSize = 64;
        private static readonly float GlowFalloff = 2.5f;
        private static readonly float GlowCoreSize = 0.25f; //Share of the half width that burns white
        private static readonly int WaveCount = 5;
        private static readonly float ChargeDuration = 8f;
        private static readonly float FadeDuration = 0.6f; //Of an interrupted charge
        private static readonly float HoldDuration = FzJumpFlash.Duration; //At full brightness after the last wave lands, while the flash plays
        private static readonly float EmitterFadeDuration = 0.6f; //From the end of the flash
        private static readonly float FieldFadeDuration = 1.2f; //From the end of the flash, the field outlasts the emitters
        private static readonly float IntroDuration = 1.5f;
        private static readonly float WaveStartDistance = 5f; //Tiles out from the contour
        private static readonly float SlowWaveRate = 0.35f; //Rings reaching the contour per second, for each ring
        private static readonly float FastWaveRate = 1.5f;
        private static readonly float WaveWidth = 0.014f; //Fractions of the view's height
        private static readonly float ContourMinWidth = 0.05f;
        private static readonly float ContourMaxWidth = 0.09f;
        private static readonly float EmitterGlowSize = 8f; //Tiles across
        private static readonly float EmitterGlowLead = 0.5f; //Seconds the emitters start ahead of the rest
        private static readonly float EmitterFullShare = 0.75f; //Of the charge, by when the emitters are at full brightness, well before the last wave lands
        private static readonly int EmitterGlowRays = 128;
        private static readonly float RibbonSpacing = 0.25f; //Tiles between points. Even, a jump in spacing kinks the band.
        private static readonly int NormalSmoothing = 8; //Points averaged either side, the band is wider than the curve's small kinks

        private static FzJumpEffect instance;
        private static Material glowMaterial;
        private static Material emitterGlowMaterial;

        public static bool Playing => instance != null;

        public static bool Interrupted => instance != null && instance.interrupted;

        public static bool IsPlaying(Ship ship) => ship != null && instance != null && instance.ship == ship;

        //Until the last wave lands or the charge is interrupted, the effect plays on a while after
        public static bool IsCharging(Ship ship) => IsPlaying(ship) && !instance.interrupted && !instance.wavesLanded;

        //How far the charge has built, from 0 as it starts to 1 as the waves finish building
        public static float ChargeProgress => instance == null ? 0f : instance.wavesLanded ? 1f : Mathf.Clamp01(instance.elapsed / (EmitterGlowLead + ChargeDuration));

        //Plays the charge from the start, false if there is no contour to play it on
        //OnCharged runs as the last wave lands, false from it fades the effect out instead of flashing
        public static bool Play(Ship ship, Func<bool> onCharged = null)
        {
            Stop();

            FzOverlayCamera overlayCamera = FzOverlayCamera.Get();
            List<Vector2> curve = FieldContour.Resample(FieldContour.SmoothAdaptive(FieldContour.Calculate(ship)), RibbonSpacing);
            if (overlayCamera == null || curve.Count == 0) return false;

            GameObject effect = new GameObject("FzJumpEffect");
            effect.transform.SetParent(overlayCamera.transform, false);
            instance = effect.AddComponent<FzJumpEffect>();
            instance.onCharged = onCharged;
            instance.Init(overlayCamera, curve, FieldContour.GetEmitters(ship), ship);
            return true;
        }

        //Fades out from wherever the effect has reached instead of completing the charge
        public static void Interrupt()
        {
            if (instance != null) instance.interrupted = true;
        }

        //Removes the effect at once
        public static void Stop()
        {
            if (instance == null) return;
            Destroy(instance.gameObject);
            instance = null;
        }

        private Ship ship;
        private Func<bool> onCharged;
        private FzOverlayCamera overlayCamera;
        private FzRibbon contourRibbon;
        private FzRibbon[] waves;
        private GameObject emitterGlow;
        private Mesh emitterGlowMesh;
        private MeshRenderer emitterGlowRenderer;
        private MaterialPropertyBlock emitterGlowBlock;
        private float emitterGlowAlpha = -1f;
        private Vector2[] points;
        private Vector2[] normals;
        private float[] innerLimits;
        private float[] outerLimits;
        private float elapsed;
        private float wavePhase;
        private float fadeElapsed;
        private bool interrupted;
        private float interruptElapsed;
        private bool wavesLanded;
        private bool flashPlayed;
        private float waveTimeLeft;
        private float contourLevel;
        private readonly bool[] waveLanded = new bool[WaveCount];
        private readonly float[] waveProgress = new float[WaveCount];

        private void Init(FzOverlayCamera overlayCamera, List<Vector2> curve, List<FieldContour.Emitter> emitters, Ship ship)
        {
            this.overlayCamera = overlayCamera;
            this.ship = ship;
            points = curve.ToArray();
            normals = GetOutwardNormals(points);
            GetSideLimits(points, normals, out innerLimits, out outerLimits);

            //Not parented, the camera moves and mesh vertices are in world space
            contourRibbon = FzRibbon.Create("FzJumpContour", GetGlowMaterial(), points.Length);

            waves = new FzRibbon[WaveCount];
            for (int i = 0; i < WaveCount; i++) waves[i] = FzRibbon.Create("FzJumpWave" + i, GetGlowMaterial(), points.Length);

            //One mesh for every emitter, so it is one draw call however many there are
            emitterGlowMesh = CreateGlowFans(ship, emitters);
            emitterGlowBlock = new MaterialPropertyBlock();
            emitterGlow = new GameObject("FzJumpEmitterGlow", typeof(MeshFilter), typeof(MeshRenderer));
            emitterGlow.layer = FzOverlayCamera.OverlayLayer;
            emitterGlow.GetComponent<MeshFilter>().sharedMesh = emitterGlowMesh;
            emitterGlowRenderer = emitterGlow.GetComponent<MeshRenderer>();
            emitterGlowRenderer.sharedMaterial = GetEmitterGlowMaterial();
            SetEmitterGlowAlpha(0f);
        }

        //Additive so overlapping lines build up like light. The colour lives in the texture, which runs across the
        //line's width: white at the center, fading through the glow colour to nothing at the edges.
        private static Material GetGlowMaterial()
        {
            if (glowMaterial != null) return glowMaterial;

            Texture2D texture = new Texture2D(1, GlowTextureSize, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < GlowTextureSize; y++)
            {
                texture.SetPixel(0, y, GetGlowPixel(Mathf.Abs(2f * y / (GlowTextureSize - 1f) - 1f)));
            }
            texture.Apply();

            glowMaterial = CreateGlowMaterial(texture);
            return glowMaterial;
        }

        //Edge is 0 at the glow's center and 1 where it has faded out
        private static Color GetGlowPixel(float edge)
        {
            Color color = Color.Lerp(Color.white, GlowColor, Mathf.Clamp01(edge / GlowCoreSize));
            color.a = Mathf.Pow(1f - edge, GlowFalloff);
            return color;
        }

        private static Material CreateGlowMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find(GlowShader);
            if (shader == null)
            {
                FzAdditions.Logger.LogError("Could not find shader " + GlowShader);
                return null;
            }

            Material material = new Material(shader);
            material.mainTexture = texture;
            return material;
        }

        //The shader doubles its tint, so a tint of 0.5 leaves the texture as it is. A property block fades the whole mesh in one call.
        private void SetEmitterGlowAlpha(float alpha)
        {
            if (alpha == emitterGlowAlpha) return;
            emitterGlowAlpha = alpha;
            emitterGlowBlock.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, alpha * 0.5f));
            emitterGlowRenderer.SetPropertyBlock(emitterGlowBlock);
        }

        //A disc around the center of each emitter's exterior tile, out to the glow's edge or the first wall on each ray.
        //The texture is laid on as if the disc were whole, so a cut only removes the glow beyond the wall.
        private static Mesh CreateGlowFans(Ship ship, List<FieldContour.Emitter> emitters)
        {
            float radius = EmitterGlowSize / 2f;
            int vertexCount = EmitterGlowRays + 1;

            Vector3[] vertices = new Vector3[vertexCount * emitters.Count];
            Vector2[] uvs = new Vector2[vertices.Length];
            Color32[] colors = new Color32[vertices.Length];
            int[] triangles = new int[EmitterGlowRays * 3 * emitters.Count];

            for (int e = 0; e < emitters.Count; e++)
            {
                Vector2 origin = emitters[e].OutsideTileCenter;
                int first = e * vertexCount;
                vertices[first] = origin;
                uvs[first] = new Vector2(0.5f, 0.5f);

                for (int i = 0; i < EmitterGlowRays; i++)
                {
                    float angle = Mathf.PI * 2f * i / EmitterGlowRays;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float reach = GetRayReach(ship, origin, direction, radius);

                    vertices[first + i + 1] = origin + direction * reach;
                    uvs[first + i + 1] = new Vector2(0.5f, 0.5f) + direction * (reach / radius / 2f);

                    int triangle = (e * EmitterGlowRays + i) * 3;
                    triangles[triangle] = first;
                    triangles[triangle + 1] = first + i + 1;
                    triangles[triangle + 2] = first + (i + 1) % EmitterGlowRays + 1;
                }
            }
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);

            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            return mesh;
        }

        //Walks the ray one tile at a time, so each tile it crosses is only looked up once
        private static float GetRayReach(Ship ship, Vector2 origin, Vector2 direction, float radius)
        {
            float cellX = Mathf.Floor(origin.x - ship.vShipPos.x + 0.5f);
            float cellY = Mathf.Floor(origin.y - ship.vShipPos.y + 0.5f);
            float stepX = direction.x >= 0f ? 1f : -1f;
            float stepY = direction.y >= 0f ? 1f : -1f;
            float deltaX = direction.x != 0f ? Mathf.Abs(1f / direction.x) : float.PositiveInfinity;
            float deltaY = direction.y != 0f ? Mathf.Abs(1f / direction.y) : float.PositiveInfinity;
            float nextX = direction.x != 0f ? (ship.vShipPos.x + cellX + 0.5f * stepX - origin.x) / direction.x : float.PositiveInfinity;
            float nextY = direction.y != 0f ? (ship.vShipPos.y + cellY + 0.5f * stepY - origin.y) / direction.y : float.PositiveInfinity;

            while (true)
            {
                float distance = Mathf.Min(nextX, nextY);
                if (distance >= radius) return radius;

                if (nextX < nextY)
                {
                    cellX += stepX;
                    nextX += deltaX;
                }
                else
                {
                    cellY += stepY;
                    nextY += deltaY;
                }

                Tile tile = ship.GetTileAtWorldCoords1(ship.vShipPos.x + cellX, ship.vShipPos.y + cellY, bAllowDocked: true);
                if (tile != null && tile.IsWall) return distance;
            }
        }

        //The same glow as the lines, fading out in every direction from the center
        private static Material GetEmitterGlowMaterial()
        {
            if (emitterGlowMaterial != null) return emitterGlowMaterial;

            Texture2D texture = new Texture2D(GlowTextureSize, GlowTextureSize, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < GlowTextureSize; y++)
            {
                for (int x = 0; x < GlowTextureSize; x++)
                {
                    Vector2 position = new Vector2(2f * x / (GlowTextureSize - 1f) - 1f, 2f * y / (GlowTextureSize - 1f) - 1f);
                    texture.SetPixel(x, y, GetGlowPixel(Mathf.Clamp01(position.magnitude)));
                }
            }
            texture.Apply();

            emitterGlowMaterial = CreateGlowMaterial(texture);
            return emitterGlowMaterial;
        }

        private static Vector2[] GetOutwardNormals(Vector2[] points)
        {
            //The sign of the area says which way round the points go, and so which side is outside
            float area = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 next = points[(i + 1) % points.Length];
                area += points[i].x * next.y - next.x * points[i].y;
            }
            float side = area >= 0f ? 1f : -1f;

            Vector2[] raw = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 tangent = points[(i + 1) % points.Length] - points[(i + points.Length - 1) % points.Length];
                raw[i] = new Vector2(tangent.y, -tangent.x).normalized * side;
            }

            Vector2[] normals = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 sum = Vector2.zero;
                for (int offset = -NormalSmoothing; offset <= NormalSmoothing; offset++)
                {
                    sum += raw[((i + offset) % points.Length + points.Length) % points.Length];
                }
                normals[i] = sum.normalized;
            }
            return normals;
        }

        //How far from each point the band can reach to one side before it meets another part of the loop. That is the
        //radius of the largest circle touching the loop there on that side, and each other point sets a limit on it.
        //Points within the normals' smoothing are skipped, a smoothed normal isn't square to the curve, so they would
        //read as a tight bend.
        private static void GetSideLimits(Vector2[] points, Vector2[] normals, out float[] inner, out float[] outer)
        {
            int count = points.Length;
            inner = new float[count];
            outer = new float[count];
            for (int i = 0; i < count; i++)
            {
                float innerLimit = float.MaxValue;
                float outerLimit = float.MaxValue;
                for (int j = 0; j < count; j++)
                {
                    int apart = Mathf.Abs(i - j);
                    if (Mathf.Min(apart, count - apart) <= NormalSmoothing) continue;

                    Vector2 offset = points[j] - points[i];
                    float along = Vector2.Dot(offset, normals[i]);
                    if (along > 0f) outerLimit = Mathf.Min(outerLimit, offset.sqrMagnitude / (2f * along));
                    else if (along < 0f) innerLimit = Mathf.Min(innerLimit, offset.sqrMagnitude / (-2f * along));
                }
                inner[i] = innerLimit;
                outer[i] = outerLimit;
            }

            RelaxLimits(points, inner);
            RelaxLimits(points, outer);
        }

        //Moving along the loop can't change the limit faster than the distance moved, this keeps the edge free of notches
        private static void RelaxLimits(Vector2[] points, float[] limits)
        {
            int count = points.Length;
            for (int i = 1; i < count * 2; i++)
            {
                int point = i % count;
                int last = (i - 1) % count;
                limits[point] = Mathf.Min(limits[point], limits[last] + (points[point] - points[last]).magnitude);
            }
            for (int i = count * 2 - 2; i >= 0; i--)
            {
                int point = i % count;
                int last = (i + 1) % count;
                limits[point] = Mathf.Min(limits[point], limits[last] + (points[point] - points[last]).magnitude);
            }
        }

        private void LateUpdate()
        {
            float deltaTime = CrewSim.TimeElapsedScaled();
            elapsed += deltaTime;

            if (interrupted)
            {
                interruptElapsed += deltaTime;
                if (interruptElapsed >= FadeDuration)
                {
                    Stop();
                    return;
                }
            }

            //Once charged no new waves start, the contour holds after the last one lands and then fades
            if (wavesLanded)
            {
                fadeElapsed += deltaTime;

                //The jump itself, as soon as the last wave lands on a charge that was not interrupted
                if (!flashPlayed && !interrupted)
                {
                    if (onCharged == null || onCharged())
                    {
                        flashPlayed = true;
                        FzJumpFlash.Play(points);
                    }
                    else
                    {
                        interrupted = true;
                    }
                }

                if (fadeElapsed >= HoldDuration + Mathf.Max(FieldFadeDuration, EmitterFadeDuration))
                {
                    Stop();
                    return;
                }
            }

            float mainElapsed = Mathf.Max(elapsed - EmitterGlowLead, 0f);
            float charge = Mathf.Clamp01(mainElapsed / ChargeDuration);
            float interruptFade = 1f - Mathf.Clamp01(interruptElapsed / FadeDuration);
            float fade = (1f - Mathf.Clamp01((fadeElapsed - HoldDuration) / FieldFadeDuration)) * interruptFade;
            if (mainElapsed > 0f) wavePhase += deltaTime * Mathf.Lerp(SlowWaveRate, FastWaveRate, charge * charge);

            //Keeps climbing past the end of the charge, so full brightness lands with the last wave.
            //Before then the time left is a good guess, a wave takes at most this long at full speed.
            if (wavesLanded) contourLevel = 1f;
            else if (charge < 1f) contourLevel = mainElapsed / (ChargeDuration + 1f / FastWaveRate);
            else if (waveTimeLeft > 0f) contourLevel += (1f - contourLevel) * Mathf.Min(deltaTime / waveTimeLeft, 1f);

            float contourWidth = Mathf.Lerp(ContourMinWidth, ContourMaxWidth, contourLevel * contourLevel);
            float contourAlpha = contourLevel * fade;
            contourRibbon.SetShape(points, normals, innerLimits, outerLimits, 0f, overlayCamera.ScreenToWorld(contourWidth));
            contourRibbon.SetAlpha(contourAlpha);

            //The emitters lead the charge up, and once the flash is over go out ahead of the field
            float emitterRise = Mathf.Clamp01(elapsed / (EmitterGlowLead + ChargeDuration * EmitterFullShare));
            float emitterFade = 1f - Mathf.Clamp01((fadeElapsed - HoldDuration) / EmitterFadeDuration);
            SetEmitterGlowAlpha(emitterRise * emitterFade * interruptFade);
            float intro = Mathf.Clamp01(mainElapsed / IntroDuration);
            float waveAlpha = Mathf.Lerp(0.25f, 1f, charge) * intro * interruptFade;
            bool anyActive = false;
            for (int i = 0; i < WaveCount; i++) anyActive |= UpdateWave(i, waveAlpha, charge >= 1f);
            wavesLanded = !anyActive;

            waveTimeLeft = 0f;
            for (int i = 0; i < WaveCount; i++)
            {
                if (!waveLanded[i]) waveTimeLeft = Mathf.Max(waveTimeLeft, (1f - waveProgress[i]) / FastWaveRate);
            }
        }

        //Returns whether the wave is still travelling
        private bool UpdateWave(int index, float waveAlpha, bool charged)
        {
            if (waveLanded[index]) return false;
            //Starts fast and settles onto the contour
            float progress = Mathf.Repeat(wavePhase + (float)index / WaveCount, 1f);
            if (charged && progress < waveProgress[index])
            {
                waveLanded[index] = true;
                waves[index].SetAlpha(0f);
                return false;
            }
            waveProgress[index] = progress;
            float remaining = 1f - progress;
            float distance = WaveStartDistance * remaining * remaining;

            //Gone by the time it arrives, the blending is additive so a visible wave would pulse the whole contour
            waves[index].SetShape(points, normals, innerLimits, outerLimits, distance, overlayCamera.ScreenToWorld(WaveWidth));
            waves[index].SetAlpha(waveAlpha * Mathf.Sin(progress * Mathf.PI));
            return true;
        }

        private void OnDestroy()
        {
            if (emitterGlow != null) Destroy(emitterGlow);
            if (emitterGlowMesh != null) Destroy(emitterGlowMesh);
            if (contourRibbon != null) Destroy(contourRibbon.gameObject);
            foreach (FzRibbon wave in waves)
            {
                if (wave != null) Destroy(wave.gameObject);
            }
            if (instance == this) instance = null;
        }
    }
}
