using TMPro;
using UnityEngine;

public class EscapeZone : MonoBehaviour
{
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TMP_Text winText;
    [SerializeField] private string message = "Good job!\nThe heist is complete!";

    private bool gameEnded;

    private void OnTriggerEnter(Collider other)
    {
        if (gameEnded)
            return;

        if (!other.CompareTag("Player"))
            return;

        gameEnded = true;

        if (winPanel != null)
            winPanel.SetActive(true);

        if (winText != null)
            winText.text = message;
    }
}