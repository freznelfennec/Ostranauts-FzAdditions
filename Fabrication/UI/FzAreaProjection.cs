using System.Collections.Generic;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //A machine's working area, projected on the floor as slanted hazard stripes running round a band just inside the
    //area's edge, with the inside left clear. It's drawn as an item, with the game's item quad and an item material, so
    //it sorts by depth like items do: between floors and the items and people standing on them. Being an item, it's lit
    //by the room like one.
    //
    //Each stripe is solid geometry, so there's nothing see-through: the item shader showed space through transparent
    //pixels. The gaps between stripes are just empty. The mesh is rebuilt each frame with the stripes moved along the
    //band's middle line, so they run round the area with the same lean along every side, into a solid square at each
    //corner and out again on the next side.
    public class FzAreaProjection : MonoBehaviour
    {
        public enum Look { Idle, Working, Blocked, Preview }

        private const string ItemQuadPrefab = "prefabQuad";
        private const string StripeImage = "fabrication/FzAreaStripe"; //Followed by the look's name, for its colour
        private const float Depth = 0.025f; //As an item's fZScale: floors are 0.01 and 0.02, loose items 0.2 and up
        private const float BandWidth = 0.25f; //Tiles, 4 game pixels
        private const float StripePeriod = 0.5f; //Tiles along the band from one stripe to the next, near enough to fit the loop
        //Each stripe is half its spacing wide along the band, and leans forward by the band's width as it goes in

        public static FzAreaProjection Create(string name)
        {
            GameObject obj = DataHandler.GetMesh(ItemQuadPrefab);
            obj.name = name;
            DontDestroyOnLoad(obj);
            MeshFilter filter = obj.GetComponent<MeshFilter>() ?? obj.AddComponent<MeshFilter>();

            FzAreaProjection projection = obj.AddComponent<FzAreaProjection>();
            projection.renderer = obj.GetComponent<Renderer>();
            projection.mesh = new Mesh();
            projection.mesh.MarkDynamic();
            filter.sharedMesh = projection.mesh;
            projection.look = (Look)(-1);
            projection.SetLook(Look.Idle);
            return projection;
        }

        //Item materials are cached by image and shared, so each look's is made once. Its PNG gives the game an image to
        //build an item material from, and the look's colour, its most opaque pixel. The texture is swapped for a solid
        //one in that colour, as the stripes are geometry.
        private static readonly Dictionary<Look, Texture2D> solid = new Dictionary<Look, Texture2D>();

        private Material MaterialFor(Look look)
        {
            Material material = DataHandler.GetMaterial(renderer, StripeImage + look);
            if (!solid.TryGetValue(look, out Texture2D texture) || texture == null)
            {
                texture = Solid(StripeColour(material.mainTexture as Texture2D), look == Look.Preview);
                solid[look] = texture;
            }
            if (material.mainTexture != texture) material.mainTexture = texture;
            return material;
        }

        private static Color StripeColour(Texture2D source)
        {
            Color stripe = Color.white;
            if (source == null || !source.isReadable)
            {
                FzAdditions.Logger.LogWarning("Working area stripe image isn't readable, its stripes are drawn white");
                return stripe;
            }
            float best = -1f;
            foreach (Color pixel in source.GetPixels())
            {
                if (pixel.a <= best) continue;
                best = pixel.a;
                stripe = pixel;
            }
            return stripe;
        }

        //Fully opaque, as anything see-through shows space. The preview is dimmed instead of faded.
        private static Texture2D Solid(Color colour, bool dim)
        {
            Color shown = dim ? new Color(colour.r * 0.6f, colour.g * 0.6f, colour.b * 0.6f, 1f) : new Color(colour.r, colour.g, colour.b, 1f);
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "FzAreaStripeSolid" };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = shown;
            texture.SetPixels(pixels);
            texture.Apply(false);
            DontDestroyOnLoad(texture);
            return texture;
        }

        private Renderer renderer;
        private Mesh mesh;
        private Rect shape;
        private Look look;
        private float phase = float.NaN;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        //The area in its machine's frame, in tiles: x along the machine's right, y along its up
        public void SetArea(Rect area)
        {
            if (area == shape) return;
            shape = area;
            phase = float.NaN; //Rebuild on the next Animate
        }

        public void SetLook(Look next)
        {
            if (next == look) return;
            look = next;
            renderer.sharedMaterial = MaterialFor(next);
        }

        public void SetVisible(bool visible)
        {
            if (renderer.enabled != visible) renderer.enabled = visible;
        }

        //Follows its machine, which moves and turns with its ship. The band's mesh is in the machine's frame.
        public void Follow(Transform machine)
        {
            Vector3 position = machine.position;
            transform.position = new Vector3(position.x, position.y, -Depth * 4f);
            transform.rotation = Quaternion.Euler(0f, 0f, machine.rotation.eulerAngles.z);
            transform.localScale = Vector3.one;
        }

        //Moves the stripes round the band at that many tiles a second, clockwise, and rebuilds the mesh when they've
        //moved. They're placed from the clock, so they run smoothly at any frame rate.
        public void Animate(float tilesPerSecond)
        {
            float next = Time.unscaledTime * tilesPerSecond;
            if (next == phase) return;
            phase = next;
            Build(next);
        }

        //A straight run of the band along one side, between the corner squares
        private struct Side
        {
            public float Start, Length; //Along the band's middle line, from the top left going clockwise
            public Vector2 Origin;      //Its outer start
            public Vector2 Along, Inward;

            //A point by distance along the middle line and depth in from the outer edge, so every stripe on a run has
            //the same lean
            public Vector2 At(float s, float t) => Origin + Along * (s - Start) + Inward * t;
        }

        //Stripes are laid by distance along the band's middle line, which runs on round the corners without a break,
        //and drawn on the straight runs. Each corner square is solid; the stripes run into it on one side and out of it
        //on the next, at the pace they'd take to go round, so they flow round the area. The spacing is stretched a
        //little so a whole number of stripes fits round, so there's no seam where the loop closes.
        private void Build(float travelled)
        {
            vertices.Clear();
            uvs.Clear();
            triangles.Clear();
            Rect area = shape;
            float band = Mathf.Min(BandWidth, area.width / 2f, area.height / 2f);
            Vector2[] outer = { new Vector2(area.xMin, area.yMax), new Vector2(area.xMax, area.yMax), new Vector2(area.xMax, area.yMin), new Vector2(area.xMin, area.yMin) };
            Vector2[] directions = { Vector2.right, Vector2.down, Vector2.left, Vector2.up };
            Vector2[] inwards = { Vector2.down, Vector2.left, Vector2.up, Vector2.right };
            float cornerLength = Mathf.PI / 4f * band; //A quarter circle through the band's middle, the stripes' way round

            var sides = new List<Side>();
            float loop = 0f;
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                float run = Mathf.Max(0f, Vector2.Distance(outer[i], outer[next]) - band * 2f);
                sides.Add(new Side { Start = loop, Length = run, Origin = outer[i] + directions[i] * band, Along = directions[i], Inward = inwards[i] });
                loop += run + cornerLength;

                //The solid corner square at the end of this run
                Vector2 corner = outer[next];
                AddShape(new List<Vector2> { corner, corner - directions[i] * band, corner - directions[i] * band + inwards[i] * band, corner + inwards[i] * band });
            }
            if (loop <= 0f) return;

            int count = Mathf.Max(1, Mathf.RoundToInt(loop / StripePeriod));
            float period = loop / count, width = period / 2f;
            float offset = Mathf.Repeat(travelled, period);
            for (int k = 0; k < count; k++)
            {
                float at = offset + k * period;
                //Corners of the stripe by distance along the band and depth in, leaning forward as it goes in
                var stripe = new List<Vector2> { new Vector2(at, 0f), new Vector2(at + width, 0f), new Vector2(at + width + band, band), new Vector2(at + band, band) };
                foreach (Side side in sides)
                {
                    //Each stripe near the loop's end is also tried a loop back, so pieces past the end wrap to the start
                    AddPiece(stripe, side, band, 0f);
                    AddPiece(stripe, side, band, -loop);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        //The part of a stripe, moved along the band by shift, that lies on one side
        private void AddPiece(List<Vector2> stripe, Side side, float band, float shift)
        {
            var piece = new List<Vector2>(stripe.Count);
            foreach (Vector2 point in stripe) piece.Add(new Vector2(point.x + shift, point.y));
            piece = Clip(piece, side.Start, true);
            piece = Clip(piece, side.Start + side.Length, false);
            if (piece.Count < 3) return;
            var placed = new List<Vector2>(piece.Count);
            foreach (Vector2 point in piece) placed.Add(side.At(point.x, point.y));
            AddShape(placed);
        }

        //A convex shape in the band's frame, as a fan of triangles
        private void AddShape(List<Vector2> points)
        {
            int first = vertices.Count;
            foreach (Vector2 point in points)
            {
                vertices.Add(point);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }
            for (int i = 1; i < points.Count - 1; i++)
            {
                //Both ways round, so the shader's culling can't hide it
                triangles.Add(first); triangles.Add(first + i); triangles.Add(first + i + 1);
                triangles.Add(first); triangles.Add(first + i + 1); triangles.Add(first + i);
            }
        }

        //Keeps the part of a convex shape on one side of a line across the band: s at least limit, or at most it
        private static List<Vector2> Clip(List<Vector2> shape, float limit, bool keepAbove)
        {
            var kept = new List<Vector2>();
            for (int i = 0; i < shape.Count; i++)
            {
                Vector2 a = shape[i], b = shape[(i + 1) % shape.Count];
                bool aIn = keepAbove ? a.x >= limit : a.x <= limit;
                bool bIn = keepAbove ? b.x >= limit : b.x <= limit;
                if (aIn) kept.Add(a);
                if (aIn != bIn) kept.Add(Vector2.Lerp(a, b, (limit - a.x) / (b.x - a.x)));
            }
            return kept;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
