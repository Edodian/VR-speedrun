 
using TMPro;
using UnityEngine;
using UnityEngine.Events;
 
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EscapeZone : MonoBehaviour
{
    [Header("Trigger")]
    [SerializeField] private string playerTag = "Player";
 
    [Header("UI")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TMP_Text winText;
    [SerializeField, TextArea] private string message = "Game Completed!\nThe heist is complete!";
 
    [Header("Timer Integration")]
    [Tooltip("Optional. If assigned, the timer is stopped when the player escapes so Game Over can't fire afterwards.")]
    [SerializeField] private GameTimer gameTimer;
 
    [Header("Behaviour")]
    [SerializeField] private bool unlockCursorOnWin = true;
 
    [Header("Events")]
    [SerializeField] private UnityEvent onGameCompleted;
 
    private bool gameCompleted;
 
    public bool GameCompleted => gameCompleted;
 
    private void Reset()
    {
        // Make sure the collider acts as a trigger when first added.
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }
 
    private void Awake()
    {
        if (winPanel != null)
            winPanel.SetActive(false);
    }
 
    private void OnTriggerEnter(Collider other)
    {
        if (gameCompleted) return;
        if (other == null || !other.CompareTag(playerTag)) return;
 
        Complete();
    }
 
    private void Complete()
    {
        gameCompleted = true;
 
        if (gameTimer != null)
            gameTimer.StopTimer();
 
        if (winPanel != null) winPanel.SetActive(true);
        if (winText  != null) winText.text = message;
 
        if (unlockCursorOnWin)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
 
        onGameCompleted?.Invoke();
    }
}
