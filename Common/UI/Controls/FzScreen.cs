using UnityEngine;

namespace Freznel.FzAdditions.UI.Controls
{
    //Keeps the screen's border an even thickness, as a fraction of its shorter side
    public class FzScreen : MonoBehaviour
    {
        private static readonly float RimFraction = 0.02f;

        private void Start()
        {
            UpdateRim();
        }

        private void OnRectTransformDimensionsChange()
        {
            UpdateRim();
        }

        private void UpdateRim()
        {
            RectTransform background = transform.Find(Panel.ScreenBackgroundName) as RectTransform;
            Rect rect = GetComponent<RectTransform>().rect;
            if (background == null || rect.width <= 0f || rect.height <= 0f) return;

            float inset = Mathf.Min(rect.width, rect.height) * RimFraction;
            background.anchorMin = new Vector2(inset / rect.width, inset / rect.height);
            background.anchorMax = new Vector2(1f - inset / rect.width, 1f - inset / rect.height);
        }
    }
}
