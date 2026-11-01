using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Draws the jump field contour over the ship
    public class FzFieldOverlay : MonoBehaviour
    {
        private static readonly Color LineColor = SpriteUtil.ColorFromHex("#40E0FFFF");
        private static readonly float LineWidth = 0.006f; //Fraction of the view's height
        private static readonly float UpdateInterval = 0.25f;

        private static FzFieldOverlay instance;

        public static bool Visible => instance != null;

        public static int CornerCount => instance != null ? instance.cornerCount : 0;

        //Returns whether the overlay is now showing
        public static bool Toggle()
        {
            if (instance != null)
            {
                Destroy(instance.gameObject);
                instance = null;
                return false;
            }

            FzOverlayCamera overlayCamera = FzOverlayCamera.Get();
            if (overlayCamera == null) return false;

            GameObject overlay = new GameObject("FzFieldOverlay");
            overlay.transform.SetParent(overlayCamera.transform, false);
            instance = overlay.AddComponent<FzFieldOverlay>();
            instance.Init(overlayCamera);
            return true;
        }

        private FzOverlayCamera overlayCamera;
        private LineRenderer line;
        private float updateTimer;
        private int cornerCount;

        private void Init(FzOverlayCamera overlayCamera)
        {
            this.overlayCamera = overlayCamera;

            line = overlayCamera.CreateLine(transform, "Line");
            line.startColor = LineColor;
            line.endColor = LineColor;

            UpdateContour();
        }

        private void LateUpdate()
        {
            line.widthMultiplier = overlayCamera.ScreenToWorld(LineWidth);

            //An empty line is never visible, so keep checking for it until there is a contour to show
            bool visible = overlayCamera.Main.isActiveAndEnabled && (line.isVisible || line.positionCount == 0);
            if (!visible) return;

            updateTimer -= Time.unscaledDeltaTime;
            if (updateTimer <= 0f) UpdateContour();
        }

        private void UpdateContour()
        {
            updateTimer = UpdateInterval;

            List<Vector2> corners = FieldContour.Calculate(CrewSim.shipCurrentLoaded);
            cornerCount = corners.Count;

            List<Vector2> curve = FieldContour.Smooth(corners);
            line.positionCount = curve.Count;
            line.SetPositions(curve.Select(point => (Vector3)point).ToArray());
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
