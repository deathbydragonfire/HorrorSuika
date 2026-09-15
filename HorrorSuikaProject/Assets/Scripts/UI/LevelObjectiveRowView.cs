using TMPro;
using UnityEngine;

/// <summary>
/// One objective checklist row: label, current/required counter, and a completion tick.
/// </summary>
public class LevelObjectiveRowView : MonoBehaviour
{
    private static readonly Color PendingColor = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color CompleteColor = new Color(0.45f, 1f, 0.55f);

    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI counterText;
    [SerializeField] private GameObject completionTick;

    /// <summary>Index of the objective this row reflects.</summary>
    public int ObjectiveIndex { get; private set; } = -1;

    /// <summary>Applies an objective snapshot to the row.</summary>
    public void Bind(LevelObjectiveTracker.ObjectiveProgress progress)
    {
        ObjectiveIndex = progress.Index;

        if (labelText != null)
        {
            labelText.text = progress.Label;
            labelText.color = progress.IsComplete ? CompleteColor : PendingColor;
        }

        if (counterText != null)
        {
            counterText.text = $"{Mathf.Min(progress.Current, progress.Required)}/{progress.Required}";
            counterText.color = progress.IsComplete ? CompleteColor : PendingColor;
        }

        if (completionTick != null)
        {
            completionTick.SetActive(progress.IsComplete);
        }
    }
}
