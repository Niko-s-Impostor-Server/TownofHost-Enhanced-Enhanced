using BepInEx.Configuration;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE;

//来源：https://github.com/tukasa0001/TownOfHost/pull/1265
public class ClientOptionItem
{
    public ConfigEntry<bool> Config;
    public ToggleButtonBehaviour ToggleButton;

    public static SpriteRenderer CustomBackground;
    private static int numOptions = 0;
    private readonly string optionName;
    private readonly Func<bool> forced;

    private ClientOptionItem(
        string name,
        ConfigEntry<bool> config,
        OptionsMenuBehaviour optionsMenuBehaviour,
        Action additionalOnClickAction = null,
        Func<bool> forced = null)
    {
        try
        {
            Config = config;
            optionName = name;
            this.forced = forced;

            var mouseMoveToggle = optionsMenuBehaviour.DisableMouseMovement;
            const float originalBackgroundScaleX = 0.9f;
            const float customBackgroundScaleX = 1.4f;
            const float horizontalScaleRatio = originalBackgroundScaleX / customBackgroundScaleX;

            // 1つ目のボタンの生成時に背景も生成
            if (CustomBackground == null)
            {
                numOptions = 0;
                CustomBackground = Object.Instantiate(optionsMenuBehaviour.Background, optionsMenuBehaviour.transform);
                CustomBackground.name = "CustomBackground";
                CustomBackground.transform.localScale = new(customBackgroundScaleX, originalBackgroundScaleX, 1f);
                CustomBackground.transform.localPosition += Vector3.back * 8;
                CustomBackground.gameObject.SetActive(false);

                var closeButton = Object.Instantiate(mouseMoveToggle, CustomBackground.transform);
                var closeButtonScale = closeButton.transform.localScale;
                closeButton.transform.localScale = new(
                    closeButtonScale.x * horizontalScaleRatio,
                    closeButtonScale.y,
                    closeButtonScale.z);
                closeButton.transform.localPosition = new(0f, -2.3f, -6f);
                closeButton.name = "Back";
                closeButton.Text.text = Translator.GetString("Back");
                closeButton.Background.color = Palette.DisabledGrey;
                var closePassiveButton = closeButton.GetComponent<PassiveButton>();
                closePassiveButton.OnClick = new();
                closePassiveButton.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    CustomBackground.gameObject.SetActive(false);
                }));

                UiElement[] selectableButtons = optionsMenuBehaviour.ControllerSelectable.ToArray();
                PassiveButton leaveButton = null;
                PassiveButton returnButton = null;
                foreach (var button in selectableButtons)
                {
                    if (button == null) continue;

                    if (button.name == "LeaveGameButton")
                        leaveButton = button.GetComponent<PassiveButton>();
                    else if (button.name == "ReturnToGameButton")
                        returnButton = button.GetComponent<PassiveButton>();
                }
                var generalTab = mouseMoveToggle.transform.parent.parent.parent;

                var modOptionsButton = Object.Instantiate(mouseMoveToggle, generalTab);
                modOptionsButton.transform.localPosition = leaveButton?.transform?.localPosition ?? new(0f, -2.4f, 1f);
                modOptionsButton.name = "TOHEOptions";
                modOptionsButton.Text.text = Translator.GetString("TOHEOptions");
                modOptionsButton.Background.color = new Color32(255, 192, 203, byte.MaxValue);
                var modOptionsPassiveButton = modOptionsButton.GetComponent<PassiveButton>();
                modOptionsPassiveButton.OnClick = new();
                modOptionsPassiveButton.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    CustomBackground.gameObject.SetActive(true);
                }));

                if (leaveButton != null)
                    leaveButton.transform.localPosition = new(-1.35f, -2.411f, -1f);
                if (returnButton != null)
                    returnButton.transform.localPosition = new(1.35f, -2.411f, -1f);
            }

            // ボタン生成
            ToggleButton = Object.Instantiate(mouseMoveToggle, CustomBackground.transform);
            var toggleScale = ToggleButton.transform.localScale;
            ToggleButton.transform.localScale = new(
                toggleScale.x * horizontalScaleRatio,
                toggleScale.y,
                toggleScale.z);
            const float columnSpacing = 2.7f * horizontalScaleRatio;
            const float rowSpacing = 0.55f;
            var column = numOptions % 3;
            var row = numOptions / 3;
            ToggleButton.transform.localPosition = new Vector3(
                (column - 1) * columnSpacing,
                2.2f - (rowSpacing * row),
                -6f);
            ToggleButton.name = name;
            ToggleButton.Text.text = Translator.GetString(name);
            var passiveButton = ToggleButton.GetComponent<PassiveButton>();
            passiveButton.OnClick = new();
            passiveButton.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                if (this.forced?.Invoke() == true) { UpdateToggle(); return; }
                if (config != null) config.Value = !config.Value;
                UpdateToggle();
                additionalOnClickAction?.Invoke();
            }));
            UpdateToggle();
        }
        finally { numOptions++; }
    }

    public static ClientOptionItem Create(
        string name,
        ConfigEntry<bool> config,
        OptionsMenuBehaviour optionsMenuBehaviour,
        Action additionalOnClickAction = null,
        Func<bool> forced = null)
    {
        return new(name, config, optionsMenuBehaviour, additionalOnClickAction, forced);
    }

    public void UpdateToggle()
    {
        if (ToggleButton == null) return;

        var isForced = forced?.Invoke() == true;
        ToggleButton.Text.text = Translator.GetString(optionName) + (isForced ? $" ({Translator.GetString("OfficialAnticheatForced")})" : "");
        var color = (Config == null || isForced || Config.Value) ? new Color32(255, 192, 203, byte.MaxValue) : new Color32(77, 77, 77, byte.MaxValue);
        ToggleButton.Background.color = color;
        ToggleButton.Rollover?.ChangeOutColor(color);
    }
}
