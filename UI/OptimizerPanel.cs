using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using PavonisInteractive.TerraInvicta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ShipOptimizerConfig.UI
{
    // Adds a "Ship Stats | Optimizer Config" tab strip to the designer's ship performance panel and builds the
    // Optimizer Config page from clones of the designer's own widgets, so it matches the game's look.
    [HarmonyPatch(typeof(FleetsScreenController), nameof(FleetsScreenController.ShowShipDesigner))]
    static class OptimizerPanel
    {
        const string ActiveTabSprite = "ui/UI_ActiveTab";

        class Row
        {
            public ResourceLimit entry;
            public Slider slider;
            public TMP_InputField input;
            public Toggle noLimit;
            public CanvasGroup valueGroup;
        }

        static GameObject statsContent;
        static GameObject configContent;
        static GameObject headerText;
        static GameObject tabStrip;
        static Image statsTabImage, configTabImage;
        static Sprite inactiveTabSprite, activeTabSprite;
        static TMP_Text messageText;
        static readonly List<Row> rows = new List<Row>();
        static bool showingConfig;
        static bool updatingControls;

        static void Postfix(FleetsScreenController __instance)
        {
            if (!Main.enabled)
                return;
            try
            {
                if (configContent == null)
                    Build(__instance);
                if (configContent != null)
                {
                    SetModActive(true);
                    Refresh();
                    ShowTab(showingConfig);
                }
            }
            catch (Exception e)
            {
                Main.Error("Failed to build the Optimizer Config tab: " + e);
            }
        }

        // Shows the stats or the config page; also called when the mod is toggled off
        public static void ShowTab(bool config)
        {
            if (configContent == null)
                return;
            showingConfig = config;
            statsContent.SetActive(!config);
            configContent.SetActive(config);
            statsTabImage.sprite = config ? inactiveTabSprite : activeTabSprite;
            configTabImage.sprite = config ? activeTabSprite : inactiveTabSprite;
            if (config)
                Refresh();
        }

        // Disabling the mod in UMM puts the designer back to its original layout
        public static void SetModActive(bool active)
        {
            if (configContent == null)
                return;
            if (!active)
                ShowTab(false);
            tabStrip.SetActive(active);
            headerText.SetActive(!active);
        }

        public static void ShowMessage(string message)
        {
            if (messageText == null)
                return;
            messageText.text = message ?? "";
            messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        // Re-reads stockpiles and the config into the controls
        public static void Refresh()
        {
            updatingControls = true;
            try
            {
                foreach (var row in rows)
                {
                    var stockpile = OptimizerConfig.Stockpile(row.entry.resource);
                    row.slider.maxValue = Mathf.Max(stockpile, 1f);
                    var limit = OptimizerConfig.DisplayedLimit(row.entry);
                    row.slider.SetValueWithoutNotify(Mathf.Min(limit, row.slider.maxValue));
                    row.input.SetTextWithoutNotify(FormatValue(limit));
                    row.noLimit.SetIsOnWithoutNotify(row.entry.noLimit);
                    ApplyNoLimitState(row);
                }
            }
            finally
            {
                updatingControls = false;
            }
        }

        static void Build(FleetsScreenController fsc)
        {
            // Ship Performance Data Container
            //   Header Container / Ship Data Header   <- designerShipDataHeader
            //   Ship Data Container                   <- the stats page
            var header = fsc.designerShipDataHeader.transform;
            var headerContainer = header.parent;
            var perfPanel = headerContainer.parent;
            var statsContainer = perfPanel.Find("Ship Data Container");
            var designerRoot = fsc.ShipDesignerCanvas.transform;
            var tabTemplate = designerRoot.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.name == "Drive" && b.transform.parent.name == "ShipModulesTabs");
            var inputTemplate = designerRoot.GetComponentsInChildren<TMP_InputField>(true)
                .FirstOrDefault(i => i.name == "DesignerInputText");
            if (statsContainer == null || tabTemplate == null || inputTemplate == null)
            {
                Main.Error($"Ship designer layout not recognised (stats={statsContainer != null}, tab={tabTemplate != null}, input={inputTemplate != null}); Optimizer Config tab disabled");
                return;
            }

            statsContent = statsContainer.gameObject;
            headerText = header.gameObject;
            BuildTabs(headerContainer, tabTemplate.gameObject, fsc.designerShipDataHeader.text);
            configContent = BuildConfigPage(fsc, perfPanel, statsContainer as RectTransform, inputTemplate.gameObject);
            Main.Log("Optimizer Config tab added to the ship designer");
        }

        static void BuildTabs(Transform headerContainer, GameObject tabTemplate, string statsLabel)
        {
            headerText.SetActive(false);
            var strip = tabStrip = NewUIObject("Optimizer Tabs", headerContainer);
            Stretch(strip);
            var layout = strip.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;

            var statsTab = CloneWidget(tabTemplate, strip.transform, "Ship Stats Tab");
            var configTab = CloneWidget(tabTemplate, strip.transform, "Optimizer Config Tab");
            SetLabel(statsTab, string.IsNullOrWhiteSpace(statsLabel) ? "Ship Stats" : statsLabel);
            SetLabel(configTab, "Optimizer Config");
            statsTab.GetComponent<Button>().onClick.AddListener(() => ShowTab(false));
            configTab.GetComponent<Button>().onClick.AddListener(() => ShowTab(true));

            statsTabImage = statsTab.GetComponent<Image>();
            configTabImage = configTab.GetComponent<Image>();
            inactiveTabSprite = statsTabImage.sprite;
            activeTabSprite = TIUtilities.assetLoader.LoadAsset<Sprite>(ActiveTabSprite);
            if (activeTabSprite == null)
                activeTabSprite = inactiveTabSprite;
        }

        static GameObject BuildConfigPage(FleetsScreenController fsc, Transform perfPanel, RectTransform statsContainer, GameObject inputTemplate)
        {
            // Same footprint and background as the stats page
            var page = NewUIObject("Optimizer Config Container", perfPanel);
            var rt = (RectTransform)page.transform;
            rt.anchorMin = statsContainer.anchorMin;
            rt.anchorMax = statsContainer.anchorMax;
            rt.pivot = statsContainer.pivot;
            rt.anchoredPosition = statsContainer.anchoredPosition;
            rt.sizeDelta = statsContainer.sizeDelta;
            page.transform.SetSiblingIndex(statsContainer.GetSiblingIndex() + 1);
            var background = statsContainer.GetComponent<Image>();
            if (background != null)
                CopyImage(background, page.AddComponent<Image>());

            var layout = page.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var textTemplate = fsc.designerWetMassTabText.gameObject;
            var intro = CloneText(textTemplate, page.transform, "Intro",
                "Untick No limit to cap how much of a resource Auto Design may spend on one ship. A limit of 0 keeps parts that use it out of the design entirely.");
            intro.textWrappingMode = TextWrappingModes.Normal;
            Layout(intro.gameObject, preferredHeight: 64);

            var sliderTemplate = FindSliderTemplate();
            var toggleTemplate = fsc.ShowObsoletePartsToggle.gameObject;
            foreach (var entry in OptimizerConfig.All)
                rows.Add(BuildRow(page.transform, entry, textTemplate, sliderTemplate, inputTemplate, toggleTemplate));

            // Footer: reset button + message line (used by stage 2 to report unsolvable limits)
            var footer = NewUIObject("Footer", page.transform);
            var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.childAlignment = TextAnchor.MiddleRight;
            footerLayout.childControlWidth = footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = footerLayout.childForceExpandHeight = false;
            Layout(footer, preferredHeight: 30);
            var reset = CloneWidget(fsc.designerResetDesignButtonText.transform.parent.gameObject, footer.transform, "Reset Limits Button");
            SetLabel(reset, "Clear all limits");
            Layout(reset, preferredWidth: 180, preferredHeight: 28);
            var resetButton = reset.GetComponent<Button>();
            resetButton.interactable = true;
            resetButton.onClick.AddListener(() =>
            {
                OptimizerConfig.ClearAll();
                ShowMessage(null);
                Refresh();
                SaveSoon();
            });

            messageText = CloneText(textTemplate, page.transform, "Message", "");
            messageText.textWrappingMode = TextWrappingModes.Normal;
            messageText.color = TIUtilities.UIRedTextColor;
            Layout(messageText.gameObject, preferredHeight: 40);
            messageText.gameObject.SetActive(false);

            page.AddComponent<SaveOnIdle>();
            page.SetActive(false);
            return page;
        }

        static Row BuildRow(Transform page, ResourceLimit entry, GameObject textTemplate, GameObject sliderTemplate,
            GameObject inputTemplate, GameObject toggleTemplate)
        {
            var rowObject = NewUIObject(entry.resource + " Row", page);
            var layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Layout(rowObject, preferredHeight: 28);

            var icon = NewUIObject("Icon", rowObject.transform).AddComponent<Image>();
            icon.preserveAspect = true;
            GameControl.assetLoader.LoadAssetForImageAssignment(TIUtilities.PathResourceIcon(entry.resource), icon);
            Layout(icon.gameObject, preferredWidth: 22, preferredHeight: 22);

            var label = CloneText(textTemplate, rowObject.transform, "Label", TIUtilities.GetResourceString(entry.resource));
            Layout(label.gameObject, preferredWidth: 112, preferredHeight: 24);

            // Slider and number box fade out together while the resource has no limit
            var valueObject = NewUIObject("Value", rowObject.transform);
            var valueLayout = valueObject.AddComponent<HorizontalLayoutGroup>();
            valueLayout.spacing = 6;
            valueLayout.childAlignment = TextAnchor.MiddleLeft;
            valueLayout.childControlWidth = valueLayout.childControlHeight = true;
            valueLayout.childForceExpandWidth = valueLayout.childForceExpandHeight = false;
            Layout(valueObject, flexibleWidth: 1, preferredHeight: 24);
            var valueGroup = valueObject.AddComponent<CanvasGroup>();

            var slider = CreateSlider(sliderTemplate, valueObject.transform);
            Layout(slider.gameObject, flexibleWidth: 1, preferredHeight: 18);

            var box = NewUIObject("Amount Box", valueObject.transform);
            Layout(box, preferredWidth: 72, preferredHeight: 22);
            var inputBackground = inputTemplate.transform.parent.Find("Input Background")?.GetComponent<Image>();
            if (inputBackground != null)
                CopyImage(inputBackground, box.AddComponent<Image>());
            var input = CloneWidget(inputTemplate, box.transform, "Amount").GetComponent<TMP_InputField>();
            Stretch(input.gameObject);
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.characterLimit = 10;
            input.interactable = true;

            var noLimit = CloneWidget(toggleTemplate, rowObject.transform, "No Limit").GetComponent<Toggle>();
            noLimit.group = null;
            noLimit.interactable = true;
            SetLabel(noLimit.gameObject, "No limit");
            Layout(noLimit.gameObject, preferredWidth: 92, preferredHeight: 24);

            var row = new Row { entry = entry, slider = slider, input = input, noLimit = noLimit, valueGroup = valueGroup };

            slider.onValueChanged.AddListener(value =>
            {
                if (updatingControls)
                    return;
                SetLimit(entry, value);
                input.SetTextWithoutNotify(FormatValue(value));
            });
            input.onSelect.AddListener(_ => TIInputManager.BlockKeybindings());
            input.onDeselect.AddListener(_ => TIInputManager.RestoreKeybindings());
            input.onEndEdit.AddListener(text =>
            {
                if (updatingControls)
                    return;
                // Typed values may exceed the stockpile; the slider just pins at its max
                var value = TryParseValue(text, out var parsed) ? Mathf.Max(0f, parsed) : OptimizerConfig.DisplayedLimit(entry);
                SetLimit(entry, value);
                updatingControls = true;
                slider.SetValueWithoutNotify(Mathf.Min(value, slider.maxValue));
                input.SetTextWithoutNotify(FormatValue(value));
                updatingControls = false;
            });
            noLimit.onValueChanged.AddListener(isOn =>
            {
                if (updatingControls)
                    return;
                entry.noLimit = isOn;
                // Unticking starts the limit from what the controls show (the stockpile if never set)
                if (!isOn && entry.limit < 0f)
                    entry.limit = OptimizerConfig.DisplayedLimit(entry);
                ShowMessage(null);
                ApplyNoLimitState(row);
                SaveSoon();
            });
            return row;
        }

        static void SetLimit(ResourceLimit entry, float value)
        {
            ShowMessage(null);
            entry.limit = value;
            SaveSoon();
        }

        static void ApplyNoLimitState(Row row)
        {
            row.slider.interactable = !row.entry.noLimit;
            row.input.interactable = !row.entry.noLimit;
            row.valueGroup.alpha = row.entry.noLimit ? 0.4f : 1f;
        }

        static Slider CreateSlider(GameObject template, Transform parent)
        {
            Slider slider;
            if (template != null)
            {
                slider = CloneWidget(template, parent, "Limit Slider").GetComponent<Slider>();
                FitSliderToWidth(slider);
            }
            else
            {
                // No game slider loaded: fall back to a stock Unity slider tinted to the UI palette
                var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
                go.name = "Limit Slider";
                go.transform.SetParent(parent, false);
                slider = go.GetComponent<Slider>();
                foreach (var graphic in go.GetComponentsInChildren<Image>())
                    graphic.color = graphic.name == "Background" ? new Color(0.1f, 0.15f, 0.2f) : TIUtilities.UITextColor;
            }
            slider.onValueChanged = new Slider.SliderEvent();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.wholeNumbers = false;
            slider.interactable = true;
            return slider;
        }

        // The options-menu slider has a fixed 200px background and moves its handle across the whole slider rect.
        // Stretch the background to the slider's width and inset the handle's travel by half its width, so the
        // handle sits flush with the track at 0 and at max whatever width the row gives the slider.
        static void FitSliderToWidth(Slider slider)
        {
            if (slider.transform.Find("Background") is RectTransform background)
            {
                background.anchorMin = new Vector2(0f, 0.5f);
                background.anchorMax = new Vector2(1f, 0.5f);
                background.anchoredPosition = Vector2.zero;
                background.sizeDelta = new Vector2(0f, background.sizeDelta.y);
            }
            var handle = slider.handleRect;
            if (handle == null || handle.parent != slider.transform)
                return;
            var halfWidth = handle.sizeDelta.x / 2f;
            var area = NewUIObject("Handle Slide Area", slider.transform);
            Stretch(area);
            var areaRect = (RectTransform)area.transform;
            areaRect.offsetMin = new Vector2(halfWidth, 0f);
            areaRect.offsetMax = new Vector2(-halfWidth, 0f);
            area.transform.SetSiblingIndex(handle.GetSiblingIndex());
            handle.SetParent(area.transform, false);
            slider.handleRect = handle;
        }

        static GameObject FindSliderTemplate()
        {
            var graphics = Resources.FindObjectsOfTypeAll<GraphicsMenuController>().FirstOrDefault(g => g.uiScaleSlider != null);
            if (graphics != null)
                return graphics.uiScaleSlider.gameObject;
            Main.Log("Game slider not found; using a default Unity slider");
            return null;
        }

        // Clones a game widget and strips everything that ties it to its original screen
        static GameObject CloneWidget(GameObject template, Transform parent, string name)
        {
            var clone = Object.Instantiate(template, parent, false);
            clone.name = name;
            clone.SetActive(true);
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var typeName = behaviour.GetType().Name;
                if (typeName == "TooltipTrigger" || typeName == "RightClickHandler" || typeName == "RectTransformChangeListener")
                    Object.DestroyImmediate(behaviour);
            }
            foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child != clone.transform && (child.name.StartsWith("Dummy_") || child.name == "RightClick Icon"))
                    Object.DestroyImmediate(child.gameObject);
            }
            // Replacing the events drops the persistent (inspector-wired) listeners that call the original screen
            if (clone.TryGetComponent<Button>(out var button))
                button.onClick = new Button.ButtonClickedEvent();
            if (clone.TryGetComponent<Toggle>(out var toggle))
                toggle.onValueChanged = new Toggle.ToggleEvent();
            if (clone.TryGetComponent<TMP_InputField>(out var input))
            {
                input.onValueChanged = new TMP_InputField.OnChangeEvent();
                input.onEndEdit = new TMP_InputField.SubmitEvent();
                input.onSubmit = new TMP_InputField.SubmitEvent();
                input.onSelect = new TMP_InputField.SelectionEvent();
                input.onDeselect = new TMP_InputField.SelectionEvent();
            }
            return clone;
        }

        static TMP_Text CloneText(GameObject template, Transform parent, string name, string text)
        {
            var clone = CloneWidget(template, parent, name);
            foreach (Transform child in clone.transform)
                Object.Destroy(child.gameObject);
            var tmp = clone.GetComponent<TMP_Text>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        static void SetLabel(GameObject widget, string text)
        {
            var label = widget.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = text;
        }

        static GameObject NewUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void CopyImage(Image from, Image to)
        {
            to.sprite = from.sprite;
            to.type = from.type;
            to.color = from.color;
            to.material = from.material;
        }

        static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Layout(GameObject go, float preferredWidth = -1, float preferredHeight = -1, float flexibleWidth = -1)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null)
                element = go.AddComponent<LayoutElement>();
            element.ignoreLayout = false;
            element.minWidth = preferredWidth;
            element.minHeight = preferredHeight;
            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
        }

        static string FormatValue(float value) =>
            value.ToString(value >= 100f ? "0" : value >= 1f ? "0.##" : "0.###", CultureInfo.InvariantCulture);

        static bool TryParseValue(string text, out float value)
        {
            text = (text ?? "").Replace(CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator, "").Trim();
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        static float lastChange = -1f;

        static void SaveSoon() => lastChange = Time.unscaledTime;

        // Saves the config to the mod's settings file a moment after the last change (slider drags fire constantly)
        class SaveOnIdle : MonoBehaviour
        {
            void Update()
            {
                if (lastChange >= 0f && Time.unscaledTime - lastChange > 1f)
                {
                    lastChange = -1f;
                    Main.SaveSettings();
                }
            }

            void OnDisable()
            {
                if (lastChange >= 0f)
                {
                    lastChange = -1f;
                    Main.SaveSettings();
                }
            }
        }
    }
}
