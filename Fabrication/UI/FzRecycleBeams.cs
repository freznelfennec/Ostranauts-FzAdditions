using Freznel.FzAdditions.Util;
using System.Collections.Generic;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //The recycling effect.
    //
    //- The item is cut away from its far side in towards the recycler, square to the way the recycler faces. Its own renderer is hidden and a copy of its quad, with its material,
    //  is drawn in its place cut off at the scan line, so it keeps its look and lighting while it disappears, with a
    //  glowing line along the cut.
    //- Two laser beams from the recycler's back corners sweep back and forth across the cut in opposite directions.
    //- Matter streams back along each beam to the recycler: motes in the colours of the materials and reserves the item
    //
    //It animates on its own clock, which only runs while the game isn't paused, at real speed, so it freezes with the
    //game and doesn't race under time acceleration. Beams, cut line and motes are on the overlay camera.
    public class FzRecycleBeams : MonoBehaviour
    {
        private const string GlowShader = "Legacy Shaders/Particles/Additive";
        private const string MoteShader = "Sprites/Default";
        private static readonly Color GlowColor = new Color(1f, 0.32f, 0.12f, 0.45f);
        private static readonly Color CoreColor = new Color(1f, 0.92f, 0.75f, 1f);
        private const float GlowWidth = 0.12f, CoreWidth = 0.035f, SpotWidth = 0.26f, CutWidth = 0.05f; //Tiles
        private const float SweepSpeed = 3.5f; //Radians a second
        private const float SweepReach = 0.45f; //Of the item's width, either side of its middle
        private static readonly Vector2[] EmitterPoints = { new Vector2(-1.2f, 0.9f), new Vector2(1.2f, 0.9f) }; //In the recycler's frame

        private const int MotesPerBeam = 60;
        private const float MoteTravel = 0.55f; //Seconds from the cut to the recycler
        private const float MoteSize = 0.07f; //Tiles
        private const float MoteWobble = 0.06f; //Tiles either side of the beam

        public static FzRecycleBeams Create(string name)
        {
            GameObject obj = new GameObject(name);
            DontDestroyOnLoad(obj);
            FzRecycleBeams beams = obj.AddComponent<FzRecycleBeams>();
            beams.Init();
            return beams;
        }

        private LineRenderer[] glows, cores, spots;
        private LineRenderer cut;
        private Mesh moteMesh;
        private MeshRenderer moteRenderer;
        private readonly List<Vector3> moteVertices = new List<Vector3>();
        private readonly List<Color> moteColours = new List<Color>();
        private readonly List<int> moteTriangles = new List<int>();
        private float clock; //Seconds of unpaused play since the effect began
        private float lastFrame = -1f;

        private struct Mote { public float Born; public float Offset; public Color Colour; }
        private readonly Mote[,] motes = new Mote[2, MotesPerBeam];
        private bool motesSeeded;

        private void Init()
        {
            glows = new LineRenderer[2];
            cores = new LineRenderer[2];
            spots = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                glows[i] = Line("Glow" + i, GlowColor, GlowWidth, 0);
                cores[i] = Line("Core" + i, CoreColor, CoreWidth, 2);
                spots[i] = Line("Spot" + i, GlowColor, SpotWidth, 3);
            }
            cut = Line("Cut", CoreColor, CutWidth, 1);

            GameObject moteObj = new GameObject("Motes", typeof(MeshFilter), typeof(MeshRenderer));
            moteObj.layer = FzOverlayCamera.OverlayLayer;
            moteObj.transform.SetParent(transform, false);
            moteMesh = new Mesh();
            moteMesh.MarkDynamic();
            moteObj.GetComponent<MeshFilter>().sharedMesh = moteMesh;
            moteRenderer = moteObj.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find(MoteShader);
            if (shader != null) moteRenderer.sharedMaterial = new Material(shader);
            moteRenderer.sortingOrder = 4;
            SetVisible(false);
        }

        private LineRenderer Line(string name, Color color, float width, int order)
        {
            GameObject obj = new GameObject(name, typeof(LineRenderer));
            obj.layer = FzOverlayCamera.OverlayLayer;
            obj.transform.SetParent(transform, false);
            LineRenderer line = obj.GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width;
            line.sortingOrder = order;
            Shader shader = Shader.Find(GlowShader);
            if (shader != null) line.material = new Material(shader);
            else FzAdditions.Logger.LogError("Could not find shader " + GlowShader);
            return line;
        }

        public void SetVisible(bool visible)
        {
            foreach (LineRenderer line in glows) line.enabled = visible;
            foreach (LineRenderer line in cores) line.enabled = visible;
            foreach (LineRenderer line in spots) line.enabled = visible;
            cut.enabled = visible;
            moteRenderer.enabled = visible;
        }

        //Runs the effect's clock while the game plays, once a frame however many recyclers ask
        private void Tick()
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (!CrewSim.Paused) clock += Time.unscaledDeltaTime;
        }

        //Draws the effect on an item the given share of the way through its run. working is false while the run is
        //paused: the item stays cut as far as it got, without beams.
        //
        //A fabricator draws it the other way round (building): the item grows from its near side out, matter flows out
        //along the beams to it, in the colours of materials, a recipe's when given. With grow false, building onto a
        //stack, the item is shown whole and the beams play over its middle.
        public void Show(CondOwner machine, CondOwner item, float share, bool working, bool building = false, bool grow = true, IDictionary<string, int> materials = null)
        {
            Tick();
            share = Mathf.Clamp01(share);
            if (machine?.tf == null || item?.tf == null)
            {
                SetVisible(false);
                return;
            }
            ScanAxis(machine, item, out int axis, out float far);
            //How much of the item is cut away: built, it grows from the near side as far as the share
            float cutAway = building ? 1f - share : share;
            if (!building || grow) ItemCut.Apply(item, cutAway, axis, far);
            if (!working || FzOverlayCamera.Get() == null)
            {
                SetVisible(false);
                return;
            }
            SetVisible(true);

            //The cut, square to the way the recycler faces, working from the item's far side in towards the recycler
            Transform itemTf = item.tf;
            float cutAt = !building || grow ? far * (0.5f - cutAway) : 0f;
            Vector2 Local(float across) => itemTf.TransformPoint(axis == 0 ? new Vector3(cutAt, across, 0f) : new Vector3(across, cutAt, 0f));
            Aim(cut, Local(-0.5f), Local(0.5f), CutWidth * (0.8f + 0.2f * Mathf.PerlinNoise(clock * 11f, 0.7f)));
            cut.enabled = !building || grow;

            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(clock * 9f, 0.3f);
            Transform tf = machine.tf;
            List<(Color Colour, float Weight)> palette = Palette(item, materials);
            for (int i = 0; i < 2; i++)
            {
                Vector2 from = (Vector2)tf.position + (Vector2)tf.right * EmitterPoints[i].x + (Vector2)tf.up * EmitterPoints[i].y;
                float across = Mathf.Sin(clock * SweepSpeed + i * Mathf.PI) * SweepReach;
                Vector2 to = Local(across);
                Aim(glows[i], from, to, GlowWidth * flicker);
                Aim(cores[i], from, to, CoreWidth);
                Aim(spots[i], to, to + Vector2.right * 0.001f, SpotWidth * flicker);
                UpdateMotes(i, from, to, palette, building);
            }
            DrawMotes();
        }

        //Which of the item's own axes points away from the recycler (the way the recycler's area reaches), and which
        //way along it: the item's far side. Both turn in quarter turns, so the recycler's up lines up with one of them.
        private static void ScanAxis(CondOwner machine, CondOwner item, out int axis, out float far)
        {
            Vector3 away = item.tf.InverseTransformDirection(machine.tf.up);
            if (Mathf.Abs(away.x) > Mathf.Abs(away.y)) { axis = 0; far = Mathf.Sign(away.x); }
            else { axis = 1; far = Mathf.Sign(away.y); }
            if (far == 0f) far = 1f;
        }

        private static void Aim(LineRenderer line, Vector2 from, Vector2 to, float width)
        {
            line.SetPosition(0, new Vector3(from.x, from.y, 0f));
            line.SetPosition(1, new Vector3(to.x, to.y, 0f));
            line.startWidth = line.endWidth = width;
        }

        //Each mote is born at the cut where the beam meets it, takes the colour of one of the item's materials, and rides the beam back
        //to the recycler, then is born again. They're spread out in time so the beam carries a steady stream. Building,
        //they ride the other way, out from the machine to the cut.
        private void UpdateMotes(int beam, Vector2 emitter, Vector2 target, List<(Color Colour, float Weight)> palette, bool outwards)
        {
            if (!motesSeeded)
            {
                for (int b = 0; b < 2; b++)
                    for (int m = 0; m < MotesPerBeam; m++)
                        motes[b, m] = new Mote { Born = clock - MoteTravel * m / MotesPerBeam, Offset = Random.Range(-1f, 1f), Colour = CoreColor };
                motesSeeded = true;
            }

            Vector2 along = emitter - target;
            Vector2 side = new Vector2(-along.y, along.x).normalized;
            for (int m = 0; m < MotesPerBeam; m++)
            {
                Mote mote = motes[beam, m];
                float age = (clock - mote.Born) / MoteTravel;
                if (age >= 1f || age < 0f)
                {
                    mote.Born = clock;
                    mote.Offset = Random.Range(-1f, 1f);
                    mote.Colour = Pick(palette);
                    age = 0f;
                    motes[beam, m] = mote;
                }
                //Eases in toward the recycler, wobbling less as it's drawn in
                float t = outwards ? (1f - age) * (1f - age) : age * age;
                Vector2 at = target + along * t + side * (mote.Offset * MoteWobble * (1f - t) * Mathf.Sin(age * 12f + m));
                float size = MoteSize * (1f - 0.6f * t);
                Color colour = mote.Colour;
                colour.a *= 1f - 0.5f * t;
                AddMote(at, size, colour);
            }
        }

        //The colours of what the item breaks down into, weighted by how much of each: its materials, and the reserves
        //it holds by kg in units, at a unit to 0.01 kg as materials are. Its yield doesn't change during its run, so
        //it's worked out once for each item.
        private string paletteFor;
        private List<(Color Colour, float Weight)> palette = new List<(Color Colour, float Weight)>();

        private List<(Color Colour, float Weight)> Palette(CondOwner item, IDictionary<string, int> materials)
        {
            if (item.strID == paletteFor) return palette;
            paletteFor = item.strID;
            palette = new List<(Color Colour, float Weight)>();
            if (materials != null)
            {
                foreach (var material in materials)
                {
                    if (material.Value > 0) palette.Add((Util.FabData.GetColor(material.Key), material.Value));
                }
                return palette;
            }
            foreach (var material in Util.Recycler.Breakdown(item) ?? new Dictionary<string, double>())
            {
                if (material.Value <= 0) continue;
                Color colour = Util.FabData.Materials.TryGetValue(material.Key, out Util.JsonFabMaterial def) ? SpriteUtil.ColorFromHex(def.strColor) : CoreColor;
                palette.Add((colour, (float)material.Value));
            }
            foreach (var held in Util.Recycler.ReservesIn(item))
            {
                Util.Reserves.Reserve reserve = Util.Reserves.Get(held.Key);
                if (reserve != null && held.Value > 0) palette.Add((SpriteUtil.ColorFromHex(reserve.Color), (float)(held.Value / Util.Silo.UnitMass)));
            }
            return palette;
        }

        //One of the palette's colours, at random by weight, or the beam's own colour with nothing to go on
        private static Color Pick(List<(Color Colour, float Weight)> palette)
        {
            float total = 0f;
            foreach (var entry in palette) total += entry.Weight;
            if (total <= 0f) return CoreColor;
            float roll = Random.Range(0f, total);
            foreach (var entry in palette)
            {
                roll -= entry.Weight;
                if (roll <= 0f) return entry.Colour;
            }
            return palette[palette.Count - 1].Colour;
        }

        private void AddMote(Vector2 at, float size, Color colour)
        {
            int first = moteVertices.Count;
            float half = size / 2f;
            moteVertices.Add(new Vector3(at.x - half, at.y - half, 0f));
            moteVertices.Add(new Vector3(at.x - half, at.y + half, 0f));
            moteVertices.Add(new Vector3(at.x + half, at.y + half, 0f));
            moteVertices.Add(new Vector3(at.x + half, at.y - half, 0f));
            for (int i = 0; i < 4; i++) moteColours.Add(colour);
            moteTriangles.Add(first); moteTriangles.Add(first + 1); moteTriangles.Add(first + 2);
            moteTriangles.Add(first); moteTriangles.Add(first + 2); moteTriangles.Add(first + 3);
        }

        private void DrawMotes()
        {
            moteMesh.Clear();
            moteMesh.SetVertices(moteVertices);
            moteMesh.SetColors(moteColours);
            moteMesh.SetTriangles(moteTriangles, 0);
            moteMesh.RecalculateBounds();
            moteVertices.Clear();
            moteColours.Clear();
            moteTriangles.Clear();
        }

        public void Hide()
        {
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (moteMesh != null) Destroy(moteMesh);
        }
    }

    //An item being recycled, drawn cut away from the top down. Its own renderer is hidden and a copy of its quad mesh,
    //with its material and property block, is drawn as a child of it, cut off at the scan line. Being a child, the copy
    //moves and turns with the item and goes when it does.
    public class ItemCut : MonoBehaviour
    {
        private const string CopyName = "FzRecycleCut";

        //axis is the item's own axis (0 x, 1 y) the cut works along, and far which end of it it starts from
        public static void Apply(CondOwner item, float share, int axis, float far)
        {
            Renderer original = item?.Item?.rend;
            if (original == null) return;
            Transform existing = item.tf.Find(CopyName);
            ItemCut cut = existing != null ? existing.GetComponent<ItemCut>() : Create(item, original);
            if (cut != null) cut.Cut(share, axis, far);
        }

        private static ItemCut Create(CondOwner item, Renderer original)
        {
            MeshFilter source = original.GetComponent<MeshFilter>();
            GameObject copy = new GameObject(CopyName, typeof(MeshFilter), typeof(MeshRenderer));
            copy.layer = original.gameObject.layer;
            copy.transform.SetParent(item.tf, false);
            ItemCut cut = copy.AddComponent<ItemCut>();
            cut.original = original;
            cut.copyRenderer = copy.GetComponent<MeshRenderer>();
            cut.mesh = new Mesh();
            cut.mesh.MarkDynamic();
            copy.GetComponent<MeshFilter>().sharedMesh = cut.mesh;
            cut.block = new MaterialPropertyBlock();

            //The item's quad: its corners and their texture coordinates, a unit square if it can't be read
            Mesh quad = source != null ? source.sharedMesh : null;
            if (quad != null && quad.isReadable && quad.vertexCount > 0)
            {
                Bounds bounds = quad.bounds;
                cut.min = bounds.min;
                cut.max = bounds.max;
                Vector2[] uv = quad.uv;
                Vector3[] vertices = quad.vertices;
                cut.uvMin = new Vector2(float.MaxValue, float.MaxValue);
                cut.uvMax = new Vector2(float.MinValue, float.MinValue);
                for (int i = 0; i < uv.Length && i < vertices.Length; i++)
                {
                    cut.uvMin = Vector2.Min(cut.uvMin, uv[i]);
                    cut.uvMax = Vector2.Max(cut.uvMax, uv[i]);
                }
                if (uv.Length == 0) { cut.uvMin = Vector2.zero; cut.uvMax = Vector2.one; }
            }
            else
            {
                cut.min = new Vector3(-0.5f, -0.5f, 0f);
                cut.max = new Vector3(0.5f, 0.5f, 0f);
                cut.uvMin = Vector2.zero;
                cut.uvMax = Vector2.one;
            }
            cut.lastShare = -1f;
            return cut;
        }

        private Renderer original;
        private MeshRenderer copyRenderer;
        private Mesh mesh;
        private MaterialPropertyBlock block;
        private Vector3 min, max;
        private Vector2 uvMin, uvMax;
        private float lastShare;
        private int lastAxis = -1;
        private float lastFar;

        //What's left of the item: from its near side, towards the recycler, up to the cut. The game may show its
        //renderer again, so it's hidden each time.
        private void Cut(float share, int axis, float far)
        {
            if (original == null) return;
            original.enabled = false;
            copyRenderer.sharedMaterial = original.sharedMaterial;
            original.GetPropertyBlock(block);
            copyRenderer.SetPropertyBlock(block);
            if (Mathf.Approximately(share, lastShare) && axis == lastAxis && far == lastFar) return;
            lastShare = share;
            lastAxis = axis;
            lastFar = far;

            //The kept rectangle in the quad's own space, cut back along the axis from the far end
            Vector2 lo = new Vector2(min.x, min.y), hi = new Vector2(max.x, max.y);
            float farEdge = far > 0f ? hi[axis] : lo[axis], nearEdge = far > 0f ? lo[axis] : hi[axis];
            float cutAt = Mathf.Lerp(farEdge, nearEdge, share);
            if (far > 0f) hi[axis] = cutAt; else lo[axis] = cutAt;

            mesh.Clear();
            if (hi[axis] - lo[axis] <= 1e-5f) return;
            Vector2 Uv(float x, float y) => new Vector2(
                Mathf.Lerp(uvMin.x, uvMax.x, Mathf.InverseLerp(min.x, max.x, x)),
                Mathf.Lerp(uvMin.y, uvMax.y, Mathf.InverseLerp(min.y, max.y, y)));
            mesh.vertices = new[] { new Vector3(lo.x, lo.y, min.z), new Vector3(lo.x, hi.y, min.z), new Vector3(hi.x, hi.y, min.z), new Vector3(hi.x, lo.y, min.z) };
            mesh.uv = new[] { Uv(lo.x, lo.y), Uv(lo.x, hi.y), Uv(hi.x, hi.y), Uv(hi.x, lo.y) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
        }

        //Takes the cut off an item, showing it whole again, as when a fabricator finishes it
        public static void Remove(CondOwner item)
        {
            Transform existing = item?.tf != null ? item.tf.Find(CopyName) : null;
            if (existing != null) Destroy(existing.gameObject);
        }

        //A run that ends with the item still here, which only a dropped run could, gives it back its own look
        private void OnDestroy()
        {
            if (original != null) original.enabled = true;
            if (mesh != null) Destroy(mesh);
        }
    }
}
