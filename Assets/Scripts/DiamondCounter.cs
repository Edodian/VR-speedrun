 
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DiamondCounter : MonoBehaviour
{
    public enum DisposalMode
    {
        Destroy,
        Deactivate
    }
 
    [Header("Goal")]
    [SerializeField, Min(1)] private int diamondsNeeded = 22;
    [SerializeField] private string diamondTag = "Diamond";
 
    [Header("UI")]
    [SerializeField] private TMP_Text counterText;
    [Tooltip("Format used while collecting. {0} = collected, {1} = needed.")]
    [SerializeField] private string progressFormat = "{0} / {1}";
    [Tooltip("Shown once all diamonds are in. {0} = collected, {1} = needed.")]
    [SerializeField, TextArea] private string completionMessage = "{0} / {1}\nHead to the exit!";
 
    [Header("Disposal")]
    [Tooltip("What happens to a diamond once it has been counted.")]
    [SerializeField] private DisposalMode disposalMode = DisposalMode.Destroy;
    [Tooltip("Delay before disposal, useful to let a particle / sound play.")]
    [SerializeField, Min(0f)] private float disposalDelay = 0f;
    [Tooltip("Optional VFX spawned at the diamond's position on collection.")]
    [SerializeField] private GameObject collectEffectPrefab;
 
    [Header("Events")]
    [Tooltip("Fires each time a new diamond is counted. Argument is the current total.")]
    [SerializeField] private UnityEvent<int> onDiamondCollected;
    [Tooltip("Fires exactly once, when the goal is reached.")]
    [SerializeField] private UnityEvent onAllDiamondsCollected;
 
    private readonly HashSet<GameObject> countedDiamonds = new HashSet<GameObject>();
    private bool allCollected;
 
    // ---- Public API ----------------------------------------------------------
    public int CollectedCount => countedDiamonds.Count;
    public int DiamondsNeeded => diamondsNeeded;
    public bool AllCollected   => allCollected;
 
    // ---- Unity lifecycle -----------------------------------------------------
 
    private void Reset()
    {
        // Make sure the chest's collider acts as a trigger by default.
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }
 
    private void Start()
    {
        RefreshCounterText();
    }
 
    private void OnTriggerEnter(Collider other)
    {
        if (allCollected) return;
        if (other == null || !other.CompareTag(diamondTag)) return;
 
        GameObject root = ResolveDiamondRoot(other);
        if (root == null) return;
 
        TryCollect(root);
    }
 
    // ---- Collection ----------------------------------------------------------
 
    /// <summary>
    /// Manually mark a diamond as collected. Safe to call from gameplay code
    /// (e.g. cheats / scripted events) as well as from the trigger.
    /// </summary>
    public bool TryCollect(GameObject diamond)
    {
        if (allCollected || diamond == null) return false;
        if (!countedDiamonds.Add(diamond)) return false;
 
        ReleaseFromInteractors(diamond);
        SpawnCollectEffect(diamond.transform.position);
        DisposeDiamond(diamond);
 
        onDiamondCollected?.Invoke(countedDiamonds.Count);
        RefreshCounterText();
        CheckCompletion();
 
        return true;
    }
 
    private static GameObject ResolveDiamondRoot(Collider other)
    {
        // Prefer the rigidbody owner so compound colliders only count once.
        return other.attachedRigidbody != null
            ? other.attachedRigidbody.gameObject
            : other.gameObject;
    }
 
    private static void ReleaseFromInteractors(GameObject diamond)
    {
        // If a hand / desktop interactor is still selecting this diamond, ask
        // the manager to exit the selection cleanly. Without this the interactor
        // would keep a reference to a destroyed/disabled object for one frame
        // and emit warnings (or, worse, fire stale events).
        var grab = diamond.GetComponent<XRGrabInteractable>();
        if (grab == null || grab.interactionManager == null || !grab.isSelected)
            return;
 
        var selecting = new List<IXRSelectInteractor>(grab.interactorsSelecting);
        for (int i = 0; i < selecting.Count; i++)
            grab.interactionManager.SelectExit(selecting[i], (IXRSelectInteractable)grab);
    }
 
    private void SpawnCollectEffect(Vector3 position)
    {
        if (collectEffectPrefab != null)
            Instantiate(collectEffectPrefab, position, Quaternion.identity);
    }
 
    private void DisposeDiamond(GameObject diamond)
    {
        switch (disposalMode)
        {
            case DisposalMode.Destroy:
                Destroy(diamond, disposalDelay);
                break;
 
            case DisposalMode.Deactivate:
                if (disposalDelay <= 0f)
                    diamond.SetActive(false);
                else
                    StartCoroutine(DeactivateAfter(diamond, disposalDelay));
                break;
        }
    }
 
    private static IEnumerator DeactivateAfter(GameObject diamond, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (diamond != null) diamond.SetActive(false);
    }
 
    // ---- UI / completion -----------------------------------------------------
 
    private void CheckCompletion()
    {
        if (allCollected || countedDiamonds.Count < diamondsNeeded)
            return;
 
        allCollected = true;
 
        if (counterText != null)
            counterText.text = string.Format(completionMessage, countedDiamonds.Count, diamondsNeeded);
 
        onAllDiamondsCollected?.Invoke();
    }
 
    private void RefreshCounterText()
    {
        if (counterText == null || allCollected) return;
        counterText.text = string.Format(progressFormat, countedDiamonds.Count, diamondsNeeded);
    }
}
