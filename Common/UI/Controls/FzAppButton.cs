using Freznel.FzAdditions.Util;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    //A flat touchscreen button for app-style screens (a PDA app, not a ship panel's hardware key): a rounded tile that
    //lights in its accent colour when on or pressed. A toggle keeps its state and raises OnChanged; otherwise it raises
    //Pressed on each click.
    public class FzAppButton : FzUIElement, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        private static readonly string SpritePrefab = "GUIShip/GUIOrbitDraw/NavModMooringControl";
        private static readonly string TileSprite = "Rounded Corner Rect POT 9-Sliced";

        public static GameObject Create(GameObject parent, string name, string label, float width, float height, float x, float y, Color idle, Color accent, Color text, bool toggle)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.layer = parent.layer;
            obj.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            FzAppButton button = obj.AddComponent<FzAppButton>();
            button.SetTransform(width, height, x, y);

            Image image = obj.GetComponent<Image>();
            image.sprite = SpriteUtil.FindSprite(SpritePrefab, TileSprite);
            image.type = Image.Type.Sliced;
            image.color = idle;

            GameObject lbl = Label.Create(obj, "Label", label, 0.9f, 0.7f, 0.05f, 0.15f, 7f, 16f);
            TMP_Text t = lbl.GetComponent<TMP_Text>();
            t.color = text;

            button.idle = idle;
            button.accent = accent;
            button.textColor = text;
            button.toggle = toggle;
            return obj;
        }

        private Color idle, accent, textColor;
        private bool toggle, on, down;

        public event Action Pressed;
        public event Action<bool> OnChanged;

        public bool Enabled { get; set; } = true;

        public bool On
        {
            get => on;
            set
            {
                if (on == value) return;
                on = value;
                Show();
                OnChanged?.Invoke(value);
            }
        }

        public void SetOnWithoutNotify(bool value)
        {
            on = value;
            Show();
        }

        //A tile sprite of the app's own, such as the PDA's rounded buttons
        public void SetSprite(Sprite sprite)
        {
            Image image = GetComponent<Image>();
            if (image == null || sprite == null) return;
            image.sprite = sprite;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }

        public void SetLabel(string text)
        {
            TMP_Text t = GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = text;
        }

        private void Show()
        {
            Image image = GetComponent<Image>();
            if (image == null) return;
            bool lit = on || down;
            image.color = lit ? accent : idle;
            TMP_Text t = GetComponentInChildren<TMP_Text>();
            if (t != null) t.color = lit ? Color.Lerp(idle, Color.black, 0.6f) : textColor;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Enabled || eventData.button != PointerEventData.InputButton.Left) return;
            down = true;
            Show();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            down = false;
            Show();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            down = false;
            Show();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Enabled || eventData.button != PointerEventData.InputButton.Left) return;
            if (toggle) On = !on;
            else Pressed?.Invoke();
        }
    }
}
