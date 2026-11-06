using Freznel.FzAdditions.UI.Controls;
using Ostranauts.UI.MegaToolTip.DataModules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI
{
    //Mega tooltip section of plain text lines. ModuleHost builds one from the prefab each time a CO is selected.
    public abstract class FzTextModule : ModuleBase
    {
        private static readonly string TextName = "Text";
        private static readonly string HeaderColor = "#FFD800";
        private static readonly float FontSize = 12f;
        private static readonly float PaddingLines = 1f; //Space under the text, in lines of its own size

        protected static GameObject CreatePrefab<T>(string name) where T : FzTextModule
        {
            //Inactive so the template never runs or shows, the copy is activated once it knows it has its CO
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.layer = LayerMask.NameToLayer("UI");
            prefab.AddComponent<RectTransform>();
            prefab.AddComponent<LayoutElement>();
            prefab.AddComponent<T>();
            DontDestroyOnLoad(prefab);

            TMP_Text text = Label.Create(prefab, TextName, "", 0.94f, 1f, 0.03f, 0f, FontSize, FontSize).GetComponent<TMP_Text>();
            text.enableAutoSizing = false;
            text.fontSize = FontSize;
            text.alignment = TextAlignmentOptions.TopLeft;

            return prefab;
        }

        protected CondOwner CO { get; private set; }
        private TMP_Text text;
        private LayoutElement layout;

        //Whether this section applies to the selected CO
        protected abstract bool Accepts(CondOwner co);

        protected abstract IEnumerable<string> GetLines();

        //Runs before the first lines are built, and before each refresh
        protected virtual void OnRefresh(bool first) { }

        public override void SetData(CondOwner co)
        {
            if (co == null || !Accepts(co))
            {
                _IsMarkedForDestroy = true;
                return;
            }

            CO = co;
            text = transform.Find(TextName).GetComponent<TMP_Text>();
            layout = GetComponent<LayoutElement>();
            gameObject.SetActive(true);

            OnRefresh(true);
            UpdateText();
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(transform.parent.GetComponent<RectTransform>());
        }

        protected override void OnUpdateUI()
        {
            if (CO == null) return;
            OnRefresh(false);
            UpdateText();
        }

        private void UpdateText()
        {
            text.text = string.Join("\n", GetLines());
            //The tooltip stacks sections by their actual height, the layout element alone is not enough. Its layout
            //works in canvas units, so the height is given that way, with padding in proportion to the text.
            float height = text.preferredHeight + text.fontSize * PaddingLines;
            layout.preferredHeight = height;
            GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        protected static string Header(string title)
        {
            return $"<color={HeaderColor}>{title}</color>";
        }
    }
}
