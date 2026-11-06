using Freznel.FzAdditions.Util;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    //A momentary key like those on the nav station's time/zoom module (STN, ATC, RESET), built to that module's layout
    //from its own sprites, font and sounds. Pressed fires when it goes down, Released when the pointer lets go or leaves
    //it, and Held says whether it's down now. Its Button swaps to the pressed sprite while it's held.
    public class FzHoldBtn : FzUIElement, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private static readonly string SpritePrefab = "GUIShip/GUIOrbitDraw/NavModTimeZoom";
        private static readonly string KeySprite = "GUIBtnRectGray";
        private static readonly string PressedSprite = "GUIBtnRectGrayIn";
        private static readonly string FontSource = "btnZoomStn"; //Its label holds the font
        private static readonly string DownAudio = "ShipUIBtnNSSTNIn";
        private static readonly string UpAudio = "ShipUIBtnNSSTNOut";
        private static readonly Color LabelColor = new Color(0.745f, 0.745f, 0.745f);
        private static readonly float LabelMinFontSize = 9f;
        private static readonly float LabelMaxFontSize = 11f;

        public static GameObject Create(GameObject parent, string name, string label, float width, float height, float offsetX, float offsetY)
        {
            GameObject btn = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(Button), typeof(FzHoldBtn));
            btn.layer = LayerMask.NameToLayer("UI");
            if (parent != null) btn.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            btn.GetComponent<FzHoldBtn>().SetTransform(width, height, offsetX, offsetY);

            Image image = btn.GetComponent<Image>();
            image.sprite = SpriteUtil.FindSprite(SpritePrefab, KeySprite);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            Button button = btn.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { pressedSprite = FindPressedSprite() };

            GUIAudioBtn audio = btn.AddComponent<GUIAudioBtn>();
            audio.strAudioEmitterDown = DownAudio;
            audio.strAudioEmitterUp = UpAudio;

            GameObject lbl = new GameObject("txt", typeof(CanvasRenderer), typeof(RectTransform), typeof(TextMeshProUGUI));
            lbl.layer = btn.layer;
            RectTransform lblRect = lbl.GetComponent<RectTransform>();
            lblRect.SetParent(btn.GetComponent<RectTransform>(), false);
            lblRect.anchorMin = new Vector2(0.15f, 0.18f);
            lblRect.anchorMax = new Vector2(0.82f, 0.81f);
            lblRect.offsetMin = lblRect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = lbl.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = FindFont();
            if (font != null) text.font = font;
            text.color = LabelColor;
            text.enableAutoSizing = true;
            text.fontSizeMin = LabelMinFontSize;
            text.fontSizeMax = LabelMaxFontSize;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.text = label;

            return btn;
        }

        //The pressed sprite is only on the module keys' buttons, not on any image
        private static Sprite pressedSprite;
        private static Sprite FindPressedSprite()
        {
            if (pressedSprite != null) return pressedSprite;
            GameObject prefab = Resources.Load<GameObject>(SpritePrefab);
            foreach (Button key in prefab != null ? prefab.GetComponentsInChildren<Button>(true) : Array.Empty<Button>())
            {
                if (key.spriteState.pressedSprite != null && key.spriteState.pressedSprite.name == PressedSprite) return pressedSprite = key.spriteState.pressedSprite;
            }
            FzAdditions.Logger.LogError($"Could not find sprite {PressedSprite} in prefab {SpritePrefab}");
            return null;
        }

        private static TMP_FontAsset font;
        private static TMP_FontAsset FindFont()
        {
            if (font != null) return font;
            Transform key = UnityUtil.FindDescendant(Resources.Load<GameObject>(SpritePrefab)?.transform, FontSource);
            font = key != null ? key.GetComponentInChildren<TMP_Text>(true)?.font : null;
            if (font == null) FzAdditions.Logger.LogError($"Could not find the key font in {FontSource} of prefab {SpritePrefab}");
            return font;
        }

        public event Action Pressed;
        public event Action Released;

        public bool Held { get; private set; }

        //How long it's been held, in real time
        public float HeldTime => Held ? Time.unscaledTime - downTime : 0f;

        private float downTime;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Held || eventData.button != PointerEventData.InputButton.Left) return;
            Held = true;
            downTime = Time.unscaledTime;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void OnPointerExit(PointerEventData eventData) => Release();

        //A panel closing mid-hold must not leave it running
        private void OnDisable() => Release();

        private void Release()
        {
            if (!Held) return;
            Held = false;
            Released?.Invoke();
        }
    }
}
