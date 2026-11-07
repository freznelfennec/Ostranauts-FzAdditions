using Freznel.FzAdditions.Util;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Freznel.FzAdditions.UI
{
    //Draws SiteFog: a solid quad over every tile of a fogged station that isn't revealed, on the overlay camera
    //(FzOverlayCamera), which draws after the game's lighting, so nothing under it shows: not the station's walls,
    //items or people. One object made when the plugin loads; it casts sight every frame it's due (SiteFog.Tick), and
    //rebuilds a station's mesh only when what's revealed changes or the station moves.
    public class FzSiteFogView : MonoBehaviour
    {
        private static readonly Color FogColor = new Color(0.015f, 0.018f, 0.025f, 1f);
        private const float Lift = 2f; //World units nearer the camera than the station's tiles

        private class View
        {
            public GameObject Object;
            public Mesh Mesh;
            public int Version = -1;
            public Vector3 Anchor;
        }

        private static FzSiteFogView instance;
        private static Material material;
        private readonly Dictionary<Ship, View> views = new Dictionary<Ship, View>();

        public static void Create()
        {
            if (instance != null) return;
            var obj = new GameObject("FzSiteFogView");
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<FzSiteFogView>();
        }

        //Runs on EmptyScene
        public static void Clear()
        {
            if (instance == null) return;
            foreach (View view in instance.views.Values) Drop(view);
            instance.views.Clear();
        }

        private void LateUpdate()
        {
            if (CrewSim.system == null || FzOverlayCamera.Get() == null) return;
            try
            {
                SiteFog.Tick();
                var live = new HashSet<Ship>();
                foreach (Ship ship in CrewSim.system.GetAllLoadedShips())
                {
                    SiteFog.State state = SiteFog.Get(ship);
                    if (state == null || state.Cleared) continue;
                    live.Add(ship);
                    if (!views.TryGetValue(ship, out View view)) views[ship] = view = Make(ship);
                    Vector3 anchor = ship.aTiles.Count > 0 && ship.aTiles[0] != null ? ship.aTiles[0].tf.position : Vector3.zero;
                    if (view.Version != state.Version || view.Anchor != anchor)
                    {
                        Build(view, state);
                        view.Version = state.Version;
                        view.Anchor = anchor;
                    }
                }
                foreach (Ship gone in new List<Ship>(views.Keys))
                {
                    if (live.Contains(gone)) continue;
                    Drop(views[gone]);
                    views.Remove(gone);
                }
            }
            catch (System.Exception ex)
            {
                FzAdditions.Logger.LogError("Drawing site fog failed: " + ex);
                enabled = false;
            }
        }

        private static View Make(Ship ship)
        {
            if (material == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                material = new Material(shader) { color = FogColor };
            }
            var obj = new GameObject("FzSiteFog " + ship.strRegID) { layer = FzOverlayCamera.OverlayLayer };
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return new View { Object = obj, Mesh = mesh };
        }

        //A quad over every tile of the station that isn't revealed
        private static void Build(View view, SiteFog.State state)
        {
            Ship ship = state.Ship;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < ship.aTiles.Count; i++)
            {
                Tile tile = ship.aTiles[i];
                if (tile == null || !tile.IsShipTileOrSub || state.Revealed[i]) continue;
                Vector3 p = tile.tf.position;
                float z = p.z - Lift;
                int v = vertices.Count;
                //A touch over half a tile each way, so neighbouring quads leave no seams
                vertices.Add(new Vector3(p.x - 0.51f, p.y - 0.51f, z));
                vertices.Add(new Vector3(p.x - 0.51f, p.y + 0.51f, z));
                vertices.Add(new Vector3(p.x + 0.51f, p.y + 0.51f, z));
                vertices.Add(new Vector3(p.x + 0.51f, p.y - 0.51f, z));
                triangles.Add(v);
                triangles.Add(v + 1);
                triangles.Add(v + 2);
                triangles.Add(v);
                triangles.Add(v + 2);
                triangles.Add(v + 3);
            }
            view.Mesh.Clear();
            view.Mesh.SetVertices(vertices);
            view.Mesh.SetTriangles(triangles, 0);
            view.Mesh.RecalculateBounds();
        }

        private static void Drop(View view)
        {
            if (view.Object != null) Destroy(view.Object);
            if (view.Mesh != null) Destroy(view.Mesh);
        }
    }
}
