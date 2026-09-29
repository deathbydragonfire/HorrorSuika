using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Binds a UI Slider to one <see cref="AudioChannel"/> volume. Drop it on any Slider in a
/// settings or pause panel; the value is loaded on enable and saved when the drag ends.
/// </summary>
[RequireComponent(typeof(Slider))]
public class AudioVolumeSlider : MonoBehaviour, IPointerUpHandler
{
    [SerializeField] private AudioChannel channel = AudioChannel.Music;

    private Slider slider;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    private void OnEnable()
    {
        slider.SetValueWithoutNotify(AudioVolumeSettings.Get(channel));
        slider.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnDisable()
    {
        slider.onValueChanged.RemoveListener(OnValueChanged);
        AudioVolumeSettings.Save();
    }

    /// <inheritdoc />
    public void OnPointerUp(PointerEventData eventData)
    {
        AudioVolumeSettings.Save();
        // Music is heard live while dragging; the one-shot channels need a sample at the new level.
        AudioManager.PreviewChannel(channel);
    }

    private void OnValueChanged(float value)
    {
        AudioVolumeSettings.Set(channel, value);
    }
}
