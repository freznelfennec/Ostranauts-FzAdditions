using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    //Row of small keys with an indicator bar, like the bottom edge of the Sensors module
    public class FzSoftKeyRow : FzUIElement
    {
        private static readonly string SpritePrefab = "GUIShip/GUIOrbitDraw/NavModSensorsMFD";
        private static readonly string AudioSource = "btn01";
        private static readonly string KeySprite = "GUIBtnWhite";
        private static readonly string IndicatorSprite = "GUI9Slice03";
        private static readonly string IndicatorName = "bmp";
        private static readonly Color KeyColor = new Color(0.212f, 0.227f, 0.239f);
        private static readonly Color IndicatorColor = new Color(0.729f, 0.729f, 0.729f);
        private static readonly float KeyFill = 0.9f; //Of each key's slot, the rest is the gap

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, int keys)
        {
            GameObject row = new GameObject(name, typeof(RectTransform), typeof(FzSoftKeyRow));
            row.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                row.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            row.GetComponent<FzSoftKeyRow>().SetTransform(width, height, offsetX, offsetY);

            float slot = 1f / keys;
            for (int i = 0; i < keys; i++)
            {
                CreateKey(row, i, slot);
            }

            return row;
        }

        private static void CreateKey(GameObject row, int index, float slot)
        {
            GameObject key = new GameObject("btn" + index, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(Button), typeof(FzUIElement));
            key.layer = row.layer;
            key.GetComponent<RectTransform>().SetParent(row.GetComponent<RectTransform>(), false);
            key.GetComponent<FzUIElement>().SetTransform(slot * KeyFill, 1f, slot * index + slot * (1f - KeyFill) / 2f, 0f);

            Image image = key.GetComponent<Image>();
            image.sprite = SpriteUtil.FindSprite(SpritePrefab, KeySprite);
            image.type = Image.Type.Sliced;
            image.color = KeyColor;

            Button button = key.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            //The same sounds as the Sensors keys
            GUIAudioBtn audio = key.AddComponent<GUIAudioBtn>();
            GUIAudioBtn source = UnityUtil.FindDescendant(Resources.Load<GameObject>(SpritePrefab)?.transform, AudioSource)?.GetComponent<GUIAudioBtn>();
            if (source != null)
            {
                audio.strAudioEmitterDown = source.strAudioEmitterDown;
                audio.strAudioEmitterUp = source.strAudioEmitterUp;
            }
            else
            {
                FzAdditions.Logger.LogError("Could not find key audio " + AudioSource + " in prefab " + SpritePrefab);
            }

            GameObject indicator = new GameObject(IndicatorName, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            indicator.layer = row.layer;
            indicator.GetComponent<RectTransform>().SetParent(key.GetComponent<RectTransform>(), false);
            indicator.GetComponent<FzUIElement>().SetTransform(0.7f, 0.65f, 0.15f, 0.2f);

            Image indicatorImage = indicator.GetComponent<Image>();
            indicatorImage.sprite = SpriteUtil.FindSprite(SpritePrefab, IndicatorSprite);
            indicatorImage.type = Image.Type.Sliced;
            indicatorImage.color = IndicatorColor;
            indicatorImage.raycastTarget = false;
        }

        private readonly List<Button> buttons = new List<Button>();

        public event Action<int> OnPressed;

        //Listeners are not copied when the prefab is instantiated
        private void Awake()
        {
            buttons.AddRange(GetComponentsInChildren<Button>(true));
            for (int i = 0; i < buttons.Count; i++)
            {
                int index = i;
                buttons[i].onClick.AddListener(() => OnPressed?.Invoke(index));
            }
        }
    }
}
