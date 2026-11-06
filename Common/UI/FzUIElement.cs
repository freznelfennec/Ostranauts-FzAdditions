using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    public class FzUIElement : MonoBehaviour
    {
        [SerializeField]
        private float width;
        [SerializeField]
        private float height;
        [SerializeField]
        private float offsetX;
        [SerializeField]
        private float offsetY;

        public float Width {
            get => width;
            set
            {
                width = value;
                ApplyTransform();
            }
        }

        public float Height
        {
            get => height;
            set
            {
                height = value;
                ApplyTransform();
            }
        }

        public float OffsetX
        {
            get => offsetX;
            set
            {
                offsetX = value;
                ApplyTransform();
            }
        }

        public float OffsetY
        {
            get => offsetY;
            set
            {
                offsetY = value;
                ApplyTransform();
            }
        }

        public FzUIElement()
        {
            ApplyTransform();
        }

        public void SetTransform(float width, float height, float offsetX, float offsetY)
        {
            this.width = width;
            this.height = height;
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (gameObject == null) return;
            RectTransform rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null) return;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMin = new Vector2(offsetX, offsetY);
            rectTransform.anchorMax = new Vector2(offsetX + width, offsetY + height);
        }


    }
}
