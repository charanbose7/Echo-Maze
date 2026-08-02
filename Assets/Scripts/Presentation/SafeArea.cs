using UnityEngine;

/// <summary>
/// Resizes a RectTransform to the device safe area (notches, punch-holes, rounded
/// corners, home indicator). Attach to a full-screen child of the Canvas and parent
/// all HUD elements under it. Re-applies when the safe area or orientation changes.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    private RectTransform _rt;
    private Rect _lastSafe = new Rect(0, 0, 0, 0);
    private Vector2Int _lastRes;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        // Cheap guard: only recompute when something actually changed (no per-frame alloc).
        var res = new Vector2Int(Screen.width, Screen.height);
        if (Screen.safeArea != _lastSafe || res != _lastRes)
            Apply();
    }

    private void Apply()
    {
        _lastSafe = Screen.safeArea;
        _lastRes = new Vector2Int(Screen.width, Screen.height);

        Rect safe = Screen.safeArea;
        if (Screen.width <= 0 || Screen.height <= 0) return;

        Vector2 min = safe.position;
        Vector2 max = safe.position + safe.size;
        min.x /= Screen.width; min.y /= Screen.height;
        max.x /= Screen.width; max.y /= Screen.height;

        _rt.anchorMin = min;
        _rt.anchorMax = max;
        _rt.offsetMin = Vector2.zero;
        _rt.offsetMax = Vector2.zero;
    }
}
