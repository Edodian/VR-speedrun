// GameTimer.cs
//
// Counts down from a configurable duration, displays MM:SS on a TMP_Text,
// and opens a Game Over panel (with a restart button) when time runs out.
//
// Setup:
//   1. Put this on any always-active GameObject in the scene.
//   2. Assign Timer Text (visible HUD), Game Over Panel (disabled by default
//      in the scene; the script will hide it on Awake), Game Over Text, and
//      Restart Button.
//   3. The script wires the Restart Button's onClick automatically.

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameTimer : MonoBehaviour
{
    [Header("Time")]
    [SerializeField, Min(1f)] private float startSeconds = 300f;   // 5 minutes
    [SerializeField] private bool startOnAwake = true;

    [Header("HUD")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private string timerFormat = "{0:00}:{1:00}"; // {0} = minutes, {1} = seconds

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text gameOverText;
    [SerializeField, TextArea] private string gameOverMessage = "Game Over\nTime's up!";
    [SerializeField] private Button restartButton;

    [Header("Behaviour")]
    [Tooltip("Pause the world (Time.timeScale = 0) when the timer runs out.")]
    [SerializeField] private bool pauseOnGameOver = true;
    [Tooltip("Unlock and show the cursor when the timer runs out (so the player can click Restart).")]
    [SerializeField] private bool unlockCursorOnGameOver = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onGameOver;

    private float remaining;
    private bool running;
    private bool gameOver;

    // ---- Public API ----
    public float Remaining => remaining;
    public bool IsRunning  => running;
    public bool IsGameOver => gameOver;

    private void Awake()
    {
        remaining = startSeconds;
        UpdateTimerText();

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (restartButton != null)
            restartButton.onClick.AddListener(RestartScene);
    }

    private void OnDestroy()
    {
        if (restartButton != null)
            restartButton.onClick.RemoveListener(RestartScene);
    }

    private void Start()
    {
        if (startOnAwake)
            StartTimer();
    }

    private void Update()
    {
        if (!running || gameOver) return;

        remaining -= Time.deltaTime;

        if (remaining <= 0f)
        {
            remaining = 0f;
            UpdateTimerText();
            TriggerGameOver();
            return;
        }

        UpdateTimerText();
    }

    public void StartTimer() => running = true;
    public void StopTimer()  => running = false;

    public void AddTime(float seconds)
    {
        remaining = Mathf.Max(0f, remaining + seconds);
        UpdateTimerText();
    }

    public void RestartScene()
    {
        // Always restore timescale before reloading so the new scene starts unpaused.
        Time.timeScale = 1f;
        var current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.buildIndex);
    }

    private void UpdateTimerText()
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        timerText.text = string.Format(timerFormat, minutes, seconds);
    }

    private void TriggerGameOver()
    {
        if (gameOver) return;

        gameOver = true;
        running  = false;

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverText  != null) gameOverText.text = gameOverMessage;

        if (unlockCursorOnGameOver)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (pauseOnGameOver)
            Time.timeScale = 0f;

        onGameOver?.Invoke();
    }
}
