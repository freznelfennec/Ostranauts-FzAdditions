using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //GameRenderer composites the world through several hidden cameras and applies lighting,
    //so world lines get a camera of their own that follows the main one and draws afterwards.
    public class FzOverlayCamera : MonoBehaviour
    {
        public static readonly int OverlayLayer = 31;
        private static readonly string LineShader = "Sprites/Default";

        private static FzOverlayCamera instance;

        //Null if there is no main camera to follow
        public static FzOverlayCamera Get()
        {
            if (instance != null) return instance;

            Camera main = CrewSim.objInstance?.camMain;
            if (main == null) return null;

            GameObject overlay = new GameObject("FzOverlayCamera", typeof(Camera));
            overlay.transform.SetParent(main.transform, false);
            instance = overlay.AddComponent<FzOverlayCamera>();
            instance.Init(main);
            return instance;
        }

        public Camera Main { get; private set; }

        private Camera overlayCamera;

        //World units a screen fraction covers at the current zoom, so lines hold their size at any zoom
        public float ScreenToWorld(float fraction)
        {
            return Main.orthographicSize * 2f * fraction;
        }

        public LineRenderer CreateLine(Transform parent, string name)
        {
            GameObject lineObject = new GameObject(name, typeof(LineRenderer));
            lineObject.layer = OverlayLayer;
            lineObject.transform.SetParent(parent, false);

            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.numCornerVertices = 2;
            line.positionCount = 0;

            Shader shader = Shader.Find(LineShader);
            if (shader != null) line.material = new Material(shader); else FzAdditions.Logger.LogError("Could not find shader " + LineShader);
            return line;
        }

        private void Init(Camera main)
        {
            Main = main;
            main.cullingMask &= ~(1 << OverlayLayer);

            overlayCamera = GetComponent<Camera>();
            overlayCamera.orthographic = true;
            overlayCamera.clearFlags = CameraClearFlags.Depth;
            overlayCamera.cullingMask = 1 << OverlayLayer;
            overlayCamera.depth = main.depth + 1f;
            overlayCamera.nearClipPlane = main.nearClipPlane;
            overlayCamera.farClipPlane = main.farClipPlane;
            overlayCamera.orthographicSize = main.orthographicSize;
        }

        private void LateUpdate()
        {
            if (Main != null) overlayCamera.orthographicSize = Main.orthographicSize;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
