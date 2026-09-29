using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover and click sounds for the Selectable on this GameObject. <see cref="AudioManager"/> adds it
/// to every Selectable in each loaded scene, so views never have to wire sounds themselves.
/// </summary>
[DisallowMultipleComponent]
public class UiSoundHooks : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IPointerClickHandler, ISubmitHandler
{
    private Selectable selectable;

    private bool IsLive => selectable == null || selectable.IsInteractable();

    // Sliders and scrollbars hover but do not click; their drag is feedback enough.
    private bool Clicks => selectable == null || selectable is Button || selectable is Toggle || selectable is TMP_Dropdown || selectable is Dropdown;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
    }

    /// <inheritdoc />
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsLive)
        {
            AudioManager.PlayUiHover();
        }
    }

    /// <inheritdoc />
    public void OnSelect(BaseEventData eventData)
    {
        // A mouse press also selects; only keyboard/gamepad navigation should count as a hover.
        if (eventData is PointerEventData || eventData == null)
        {
            return;
        }

        if (IsLive)
        {
            AudioManager.PlayUiHover();
        }
    }

    /// <inheritdoc />
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && IsLive && Clicks)
        {
            AudioManager.PlayUiClick();
        }
    }

    /// <inheritdoc />
    public void OnSubmit(BaseEventData eventData)
    {
        if (IsLive && Clicks)
        {
            AudioManager.PlayUiClick();
        }
    }
}
