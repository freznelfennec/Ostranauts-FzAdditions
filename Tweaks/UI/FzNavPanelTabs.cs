using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI
{
    //A row of numbered tabs on the nav console's frame, one for each of the station's panels, then + to add a panel
    //and - to remove the one showing. It sits between the console's top two screws, which open its edit mode, in
    //ratios of the console worked out from where they are, so it follows the console's size. Ctrl+1 to 9 pick a panel.
    //The tabs grey out while the console is being rearranged, when panels can't change.
    public class FzNavPanelTabs : MonoBehaviour
    {
        private const string ObjectName = "FzNavPanelTabs";
        private const int Slots = NavPanels.MaxPanels + 2;
        private const float TabAspect = 1.4f; //A tab's width over its height
        private const float TabFill = 0.9f; //Of its slot's width, the rest is the gap to the next
        private const float ScrewGap = 0.5f; //Of a screw's width, kept clear either side of the row

        //Where the row goes if the console has no screws to place it by, in ratios of the console
        private static readonly Rect FallbackArea = Rect.MinMaxRect(0.05f, 0.96f, 0.95f, 0.99f);

        private static readonly Color Idle = new Color(0.16f, 0.18f, 0.21f, 0.95f);
        private static readonly Color Accent = new Color(0.36f, 0.76f, 0.9f, 1f);
        private static readonly Color TextColor = new Color(0.8f, 0.84f, 0.88f, 1f);
        private static readonly Color Locked = new Color(0.12f, 0.12f, 0.13f, 0.8f);

        private static readonly FieldInfo[] ScrewFields = new[] { "btnScrew01", "btnScrew02", "btnScrew03", "btnScrew04" }
            .Select(name => AccessTools.Field(typeof(GUIOrbitDraw), name)).Where(field => field != null).ToArray();

        private GUIOrbitDraw orbit;
        private RectTransform rect;
        private readonly List<FzAppButton> buttons = new List<FzAppButton>();
        private int shownCount = -1;
        private int shownSelected = -1;
        private bool shownLocked;
        private Vector2 laidOutFor;

        //Hidden on a console that isn't on a nav station, such as the PDA's
        public static void Show(GUIOrbitDraw orbit, bool shown)
        {
            Transform existing = orbit.transform.Find(ObjectName);
            if (!shown)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            FzNavPanelTabs tabs = existing != null ? existing.GetComponent<FzNavPanelTabs>() : Create(orbit);
            tabs.gameObject.SetActive(true);
            tabs.transform.SetAsLastSibling();
            tabs.shownCount = -1; //Built again for whichever station the console is on now
        }

        private static FzNavPanelTabs Create(GUIOrbitDraw orbit)
        {
            GameObject obj = new GameObject(ObjectName, typeof(RectTransform));
            obj.layer = orbit.gameObject.layer;
            obj.GetComponent<RectTransform>().SetParent(orbit.transform, false);
            FzNavPanelTabs tabs = obj.AddComponent<FzNavPanelTabs>();
            tabs.orbit = orbit;
            tabs.rect = obj.GetComponent<RectTransform>();
            return tabs;
        }

        private void Update()
        {
            CondOwner nav = orbit != null ? orbit.COSelf : null;
            if (nav == null) return;

            RectTransform parent = transform.parent as RectTransform;
            if (parent != null && parent.rect.size != laidOutFor) Layout(parent);

            int count = NavPanels.GetCount(nav);
            int selected = NavPanels.GetSelected(nav);
            bool locked = !NavPanels.CanChange(orbit);
            if (count != shownCount) Build(count);
            if (count != shownCount || selected != shownSelected || locked != shownLocked) Refresh(count, selected, locked);

            ReadShortcuts(locked);
        }

        private void Build(int count)
        {
            foreach (FzAppButton button in buttons) Destroy(button.gameObject);
            buttons.Clear();

            for (int i = 0; i < count; i++)
            {
                int index = i;
                AddButton("Tab" + i, (i + 1).ToString(), i).Pressed += () => NavPanels.Select(orbit, index);
            }
            AddButton("Add", "+", count).Pressed += () => NavPanels.Add(orbit);
            AddButton("Remove", "-", count + 1).Pressed += () => NavPanels.Remove(orbit);
            shownCount = count;
        }

        private FzAppButton AddButton(string name, string label, int slot)
        {
            GameObject obj = FzAppButton.Create(gameObject, name, label, TabFill / Slots, 1f, (float)slot / Slots, 0f, Idle, Accent, TextColor, toggle: false);
            FzAppButton button = obj.GetComponent<FzAppButton>();
            buttons.Add(button);
            return button;
        }

        //The panel showing is lit. While the console is rearranged nothing can be pressed.
        private void Refresh(int count, int selected, bool locked)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                FzAppButton button = buttons[i];
                bool usable = !locked && (i < count || (i == count && count < NavPanels.MaxPanels) || (i == count + 1 && count > 1));
                button.Enabled = usable;
                button.SetOnWithoutNotify(i == selected);
                if (!usable && i != selected) button.GetComponent<Image>().color = Locked;
            }
            shownSelected = selected;
            shownLocked = locked;
        }

        //Not while typing, such as in the console's note
        private void ReadShortcuts(bool locked)
        {
            Keyboard keyboard = Keyboard.current;
            if (locked || keyboard == null || !keyboard.ctrlKey.isPressed || orbit.NoteShowing) return;

            GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focused != null && (focused.GetComponent<TMP_InputField>() != null || focused.GetComponent<InputField>() != null)) return;

            for (int i = 0; i < NavPanels.MaxPanels; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) NavPanels.Select(orbit, i);
            }
        }

        //From the console's top two screws, as ratios of the console. Tabs keep their shape as the console resizes.
        private void Layout(RectTransform parent)
        {
            Rect area = parent.rect;
            laidOutFor = area.size;
            if (area.width <= 0f || area.height <= 0f) return;

            List<Rect> screws = ScrewFields
                .Select(field => field.GetValue(orbit) as Button)
                .Where(screw => screw != null && screw.gameObject.activeInHierarchy)
                .Select(screw => Normalise(parent, screw.transform as RectTransform))
                .ToList();

            Rect row = FallbackArea;
            if (screws.Count >= 2)
            {
                List<Rect> top = screws.OrderByDescending(screw => screw.center.y).Take(2).OrderBy(screw => screw.center.x).ToList();
                row = Rect.MinMaxRect(
                    top[0].xMax + top[0].width * ScrewGap,
                    Mathf.Min(top[0].yMin, top[1].yMin),
                    top[1].xMin - top[1].width * ScrewGap,
                    Mathf.Max(top[0].yMax, top[1].yMax));
            }

            float slot = row.height * area.height * TabAspect / area.width;
            float width = Mathf.Min(row.width, slot * Slots);
            rect.anchorMin = new Vector2(row.xMin, row.yMin);
            rect.anchorMax = new Vector2(row.xMin + width, row.yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Rect Normalise(RectTransform parent, RectTransform child)
        {
            Vector3[] corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector2 min = parent.InverseTransformPoint(corners[0]);
            Vector2 max = parent.InverseTransformPoint(corners[2]);
            Rect area = parent.rect;
            return Rect.MinMaxRect((min.x - area.xMin) / area.width, (min.y - area.yMin) / area.height, (max.x - area.xMin) / area.width, (max.y - area.yMin) / area.height);
        }
    }
}
