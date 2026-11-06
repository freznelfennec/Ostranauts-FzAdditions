using Freznel.FzAdditions.Util;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    //A toggle switch under a flip-up safety cover, like the torch drive module's THRUST LIMITER (GUISafetyToggle). Its
    //look follows that module's layout from a dump of its prefab: caution stripes behind a base plate, the switch over
    //the plate's lower part, and the cover over all of it, which flips up to a flap over the top half when opened.
    //Clicking the closed cover opens it, clicking the flap closes it again. The switch can only be flipped while the
    //cover is open, and closing the cover leaves the switch as it is, as the module's does.
    public class FzCoveredToggle : FzUIElement
    {
        private static readonly string SpritePrefab = "GUIShip/GUIOrbitDraw/NavModTorchDrive";
        private static readonly string BaseSprite = "GUISafetyToggleBase";
        private static readonly string SwitchOffSprite = "GUIToggleSwitchOff";
        private static readonly string OpenSprite = "GUISafetyToggleOpen";
        private static readonly string ClosedSprite = "GUISafetyToggleClosed";
        private static readonly string SourceToggle = "chkTorchSafety"; //Its switch holds the on sprite
        private static readonly string SourceCaution = "bmpCautionTorch"; //Holds the caution stripe texture
        private static readonly string SwitchDownAudio = "ShipUIBtnNSMinusIn";
        private static readonly string SwitchUpAudio = "ShipUIBtnNSMinusOut";
        private static readonly string OpenAudio = "ShipUIBtnNSToggleOpen";
        private static readonly string CloseAudio = "ShipUIBtnNSToggleClose";

        //The module's layout, as fractions of this control, which spans the caution stripes and the switch together.
        //The stripes are 0.11 by 0.27 of the module and the switch's base 0.08 by 0.29, centred on the stripes. (The
        //dump's anchors put it 0.005 left of centre, which is their two-decimal rounding.)
        private static readonly Rect CautionRect = new Rect(0f, 0f, 1f, 0.93f);
        private static readonly Rect BaseRect = new Rect(0.135f, 0f, 0.73f, 1f);

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(FzCoveredToggle));
            root.layer = LayerMask.NameToLayer("UI");
            if (parent != null) root.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            root.GetComponent<FzCoveredToggle>().SetTransform(width, height, offsetX, offsetY);

            GameObject prefab = Resources.Load<GameObject>(SpritePrefab);
            Transform sourceToggle = UnityUtil.FindDescendant(prefab?.transform, SourceToggle);

            //Caution stripes, behind everything
            RawImage sourceCaution = UnityUtil.FindDescendant<RawImage>(prefab?.transform, SourceCaution);
            GameObject caution = Layer(root, "Caution", CautionRect, typeof(RawImage));
            RawImage stripes = caution.GetComponent<RawImage>();
            stripes.raycastTarget = false;
            if (sourceCaution != null)
            {
                stripes.texture = sourceCaution.texture;
                stripes.uvRect = sourceCaution.uvRect;
                stripes.color = sourceCaution.color;
            }
            else
            {
                caution.SetActive(false);
                FzAdditions.Logger.LogError($"Could not find {SourceCaution} in prefab {SpritePrefab}");
            }

            GameObject plate = Layer(root, "Base", BaseRect, typeof(Image));
            Image plateImage = plate.GetComponent<Image>();
            plateImage.sprite = SpriteUtil.FindSprite(SpritePrefab, BaseSprite);
            plateImage.raycastTarget = false;

            //The switch, over the plate's lower part, swapping to its on sprite while on as GUIToggleSwap does
            GameObject switchObj = Layer(plate, "Switch", new Rect(0.01f, -0.07f, 1f, 0.77f), typeof(Toggle));
            GameObject switchBg = Layer(switchObj, "Background", new Rect(0f, 0f, 1f, 1f), typeof(Image));
            Image switchImage = switchBg.GetComponent<Image>();
            switchImage.sprite = SpriteUtil.FindSprite(SpritePrefab, SwitchOffSprite);
            Toggle toggle = switchObj.GetComponent<Toggle>();
            toggle.targetGraphic = switchImage;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.isOn = false;
            GUIAudioBtn switchAudio = switchObj.AddComponent<GUIAudioBtn>();
            switchAudio.strAudioEmitterDown = SwitchDownAudio;
            switchAudio.strAudioEmitterUp = SwitchUpAudio;

            FzCoveredToggle control = root.GetComponent<FzCoveredToggle>();
            GUIToggleSwap sourceSwap = sourceToggle != null ? sourceToggle.GetComponentInChildren<GUIToggleSwap>(true) : null;
            control.onSprite = sourceSwap != null ? sourceSwap.selectedSprite : null;
            if (control.onSprite == null) FzAdditions.Logger.LogError($"Could not find the switch's on sprite in {SourceToggle} of prefab {SpritePrefab}");

            control.flap = Cover(plate, "CoverOpen", new Rect(0.04f, 0.5f, 0.92f, 0.5f), OpenSprite);
            control.cover = Cover(plate, "CoverClosed", new Rect(0f, 0f, 1f, 1f), ClosedSprite);
            control.SetClosedVisual(true);
            return root;
        }

        private static GameObject Layer(GameObject parent, string name, Rect rect, Type graphic)
        {
            GameObject layer = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), graphic);
            layer.layer = LayerMask.NameToLayer("UI");
            RectTransform tf = layer.GetComponent<RectTransform>();
            tf.SetParent(parent.GetComponent<RectTransform>(), false);
            tf.anchorMin = rect.min;
            tf.anchorMax = rect.max;
            tf.offsetMin = tf.offsetMax = Vector2.zero;
            return layer;
        }

        private static CanvasGroup Cover(GameObject parent, string name, Rect rect, string sprite)
        {
            GameObject cover = Layer(parent, name, rect, typeof(Image));
            Image image = cover.GetComponent<Image>();
            image.sprite = SpriteUtil.FindSprite(SpritePrefab, sprite);
            Button button = cover.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.961f, 0.961f, 0.961f),
                pressedColor = new Color(0.784f, 0.784f, 0.784f),
                selectedColor = new Color(0.961f, 0.961f, 0.961f),
                disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.502f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f
            };
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return cover.AddComponent<CanvasGroup>();
        }

        //Raised when the switch is flipped by hand, not when it's set from code
        public event Action<bool> OnChanged;
        //Raised when the cover is opened or closed by hand
        public event Action<bool> OnCoverChanged;

        [SerializeField]
        private Sprite onSprite;
        [SerializeField]
        private CanvasGroup cover;
        [SerializeField]
        private CanvasGroup flap;
        private Toggle toggle;
        private Image switchImage;
        private bool wired;

        public bool On => Toggle != null && Toggle.isOn;
        public bool Closed => cover != null && cover.alpha > 0f;

        private Toggle Toggle
        {
            get
            {
                if (toggle == null) toggle = GetComponentInChildren<Toggle>(true);
                return toggle;
            }
        }

        private void Awake() => Wire();

        //Copies of the prefab lose listeners added in code, so they're added once each copy wakes
        private void Wire()
        {
            if (wired || Toggle == null) return;
            wired = true;
            switchImage = Toggle.targetGraphic as Image;
            Toggle.onValueChanged.AddListener(on =>
            {
                ShowSwitch(on);
                OnChanged?.Invoke(on);
            });
            cover.GetComponent<Button>().onClick.AddListener(() => SetClosed(false, true));
            flap.GetComponent<Button>().onClick.AddListener(() => SetClosed(true, true));
        }

        public void SetOnWithoutNotify(bool on)
        {
            Wire();
            Toggle.SetIsOnWithoutNotify(on);
            ShowSwitch(on);
        }

        public void SetClosedWithoutNotify(bool closed)
        {
            Wire();
            SetClosedVisual(closed);
        }

        private void SetClosed(bool closed, bool byHand)
        {
            if (closed == Closed) return;
            AudioManager.am?.PlayAudioEmitter(closed ? CloseAudio : OpenAudio, false);
            SetClosedVisual(closed);
            if (byHand) OnCoverChanged?.Invoke(closed);
        }

        //The closed cover sits over the switch and takes its clicks, the open flap only covers the top
        private void SetClosedVisual(bool closed)
        {
            SetGroup(cover, closed);
            SetGroup(flap, !closed);
        }

        private static void SetGroup(CanvasGroup group, bool show)
        {
            if (group == null) return;
            group.alpha = show ? 1f : 0f;
            group.interactable = show;
            group.blocksRaycasts = show;
        }

        private void ShowSwitch(bool on)
        {
            if (switchImage == null) switchImage = Toggle?.targetGraphic as Image;
            if (switchImage != null) switchImage.overrideSprite = on ? onSprite : null;
        }
    }
}
