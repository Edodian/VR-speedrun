using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class DiamondCounter : MonoBehaviour
{
    [SerializeField] private int diamondsNeeded = 22;
    [SerializeField] private TMP_Text counterText;

    [Header("Events")]
    [SerializeField] private UnityEvent onAllDiamondsCollected;

    private readonly HashSet<GameObject> collectedDiamonds = new HashSet<GameObject>();
    private bool allCollected;

    public bool AllCollected => allCollected;

    private void Start()
    {
        UpdateCounterText();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Diamond"))
            return;

        if (collectedDiamonds.Contains(other.gameObject))
            return;

        collectedDiamonds.Add(other.gameObject);
        UpdateCounterText();

        if (!allCollected && collectedDiamonds.Count >= diamondsNeeded)
        {
            allCollected = true;

            if (counterText != null)
                counterText.text = $"{collectedDiamonds.Count} / {diamondsNeeded}\nИдите к выходу!";

            onAllDiamondsCollected.Invoke();
        }
    }

    private void UpdateCounterText()
    {
        if (counterText == null)
            return;

        counterText.text = $"{collectedDiamonds.Count} / {diamondsNeeded}";
    }
}