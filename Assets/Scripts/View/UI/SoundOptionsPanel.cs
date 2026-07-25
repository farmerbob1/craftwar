using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// The Master / Music / Effects sliders, built once and used by both options
    /// screens — the in-game one (<see cref="OptionsScreen"/>) and the main
    /// menu's own panel, which is a separate UXML-driven UI. They have to behave
    /// identically because they write the same persisted settings.
    /// </summary>
    public static class SoundOptionsPanel
    {
        /// <summary>Fill <paramref name="page"/> with the three volume rows.</summary>
        public static void Build(VisualElement page)
        {
            if (page == null)
                return;
            page.Clear();
            var s = GameplaySettings.Current;
            Row(page, "Master", s.masterVolume, v => GameplaySettings.Current.masterVolume = v);
            Row(page, "Music", s.musicVolume, v => GameplaySettings.Current.musicVolume = v);
            Row(page, "Effects", s.effectsVolume, v => GameplaySettings.Current.effectsVolume = v);
        }

        /// <summary>
        /// A labelled 0-100 slider. Applies on every drag frame and saves on
        /// release: the volume must follow the handle to be adjustable by ear,
        /// but writing the settings file 60 times a second would not do.
        /// </summary>
        static void Row(VisualElement parent, string label, float value,
            System.Action<float> apply)
        {
            var row = new VisualElement();
            row.AddToClassList("options-row");

            var name = new Label(label) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("options-row__label");
            row.Add(name);

            var slider = new Slider(0f, 1f) { value = Mathf.Clamp01(value) };
            slider.AddToClassList("options-row__slider");
            row.Add(slider);

            var readout = new Label(Percent(value)) { pickingMode = PickingMode.Ignore };
            readout.AddToClassList("options-row__value");
            row.Add(readout);

            slider.RegisterValueChangedCallback(e =>
            {
                apply(e.newValue);
                readout.text = Percent(e.newValue);
                GameplaySettings.RaiseVolumesChanged();
            });
            slider.RegisterCallback<PointerUpEvent>(_ => GameplaySettings.Save());

            parent.Add(row);
        }

        static string Percent(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";
    }
}
