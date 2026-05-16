using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// right-click/two-finger annotations: square highlights and directional arrows
/// </summary>

public class BoardAnnotations : MonoBehaviour
{
    [Header("References")]
    public BoardVisuals boardVisuals;

    [Header("Prefabs")]
    [Tooltip("Thin rectangle, pivot left-centre (0, 0.5), pointing right.")]
    public GameObject segmentPrefab;
    [Tooltip("Triangle pointing right, placed at arrow tip.")]
    public GameObject arrowheadPrefab;
    [Tooltip("Square overlay, 140×140, semi-transparent.")]
    public GameObject highlightPrefab;

    [Header("Canvas Mode")]
    public bool isCanvasSpace = false;
    public Canvas parentCanvas;

    [Header("Visuals")]
    public Color highlightColor = new Color(0.85f, 0.10f, 0.10f, 0.45f);
    public Color arrowColor = new Color(1.00f, 0.55f, 0.00f, 0.85f);
    public Color knightArrowColor = new Color(0.20f, 0.75f, 1.00f, 0.85f);
    [Tooltip("Arrow segment thickness in world units (or canvas px in canvas mode).")]
    public float segmentThickness = 18f;
    [Tooltip("Arrowhead size in world units.")]
    public float arrowheadSize = 30f;

    private Camera _cam;
    private Dictionary<Vector2Int, List<GameObject>> _highlights = new();
    private Dictionary<(Vector2Int, Vector2Int), List<GameObject>> _arrows = new();
    private bool _rightDragActive;
    private Vector2Int _rightDragOrigin;

    #region Init

    private void Awake()
    {
        _cam = Camera.main;
        if (!boardVisuals) boardVisuals = GetComponent<BoardVisuals>();
    }

    #endregion Init

    #region Update

    private void Update()
    {
        if (Input.GetMouseButtonDown(0)) ClearAll();
        if (Input.GetMouseButtonDown(1))
        {
            var sq = SquareAt(Input.mousePosition);
            if (sq.HasValue) { _rightDragActive = true; _rightDragOrigin = sq.Value; }
        }
        if (Input.GetMouseButtonUp(1) && _rightDragActive)
        {
            _rightDragActive = false;
            var sq = SquareAt(Input.mousePosition);
            if (!sq.HasValue) return;
            if (sq.Value == _rightDragOrigin) ToggleHighlight(sq.Value);
            else ToggleArrow(_rightDragOrigin, sq.Value);
        }
        HandleTwoFingerAnnotation();
    }

    #endregion Update

    #region Highlight

    private void ToggleHighlight(Vector2Int sq)
    {
        if (_highlights.TryGetValue(sq, out var existing)) { KillAll(existing); _highlights.Remove(sq); return; }
        if (!highlightPrefab) return;
        var go = Spawn(highlightPrefab, SquareCentre(sq), 0f, Vector2.one);
        if (go == null) return;
        SetColor(go, highlightColor);
        _highlights[sq] = new List<GameObject> { go };
    }

    #endregion Highlight

    #region Arrows

    private void ToggleArrow(Vector2Int from, Vector2Int to)
    {
        var key = (from, to);
        if (_arrows.TryGetValue(key, out var existing)) { KillAll(existing); _arrows.Remove(key); return; }
        int dx = to.x - from.x, dy = to.y - from.y;
        bool isKnight = (Mathf.Abs(dx) == 2 && Mathf.Abs(dy) == 1) || (Mathf.Abs(dx) == 1 && Mathf.Abs(dy) == 2);
        var parts = isKnight ? BuildKnightArrow(from, to, dx, dy) : BuildStraightArrow(from, to);
        if (parts != null && parts.Count > 0) _arrows[key] = parts;
    }

    private List<GameObject> BuildStraightArrow(Vector2Int from, Vector2Int to)
    {
        var parts = new List<GameObject>();
        Vector3 src = SquareCentre(from);
        Vector3 dst = SquareCentre(to);
        Vector3 dir = (dst - src).normalized;
        Vector3 shaftEnd = dst - dir * WorldUnits(arrowheadSize * 0.5f);
        var shaft = BuildSegment(src, shaftEnd, arrowColor);
        if (shaft) parts.Add(shaft);
        var head = BuildArrowhead(dst, dir, arrowColor);
        if (head) parts.Add(head);
        return parts;
    }

    private List<GameObject> BuildKnightArrow(Vector2Int from, Vector2Int to, int dx, int dy)
    {
        var parts = new List<GameObject>();
        bool longIsY = Mathf.Abs(dy) == 2;
        Vector2Int bend = longIsY
            ? new Vector2Int(from.x, from.y + dy)
            : new Vector2Int(from.x + dx, from.y);
        Vector3 src = SquareCentre(from);
        Vector3 corner = SquareCentre(bend);
        Vector3 dst = SquareCentre(to);
        Color col = knightArrowColor;
        var leg1 = BuildSegment(src, corner, col);
        if (leg1) parts.Add(leg1);
        Vector3 dir2 = (dst - corner).normalized;
        Vector3 shaftEnd = dst - dir2 * WorldUnits(arrowheadSize * 0.5f);
        var leg2 = BuildSegment(corner, shaftEnd, col);
        if (leg2) parts.Add(leg2);
        var head = BuildArrowhead(dst, dir2, col);
        if (head) parts.Add(head);
        var dot = BuildBendDot(corner, col);
        if (dot) parts.Add(dot);
        return parts;
    }

    #endregion Arrows

    #region Builders

    private GameObject BuildSegment(Vector3 worldA, Vector3 worldB, Color col)
    {
        if (!segmentPrefab) return null;
        Vector3 delta = worldB - worldA;
        float length = delta.magnitude;
        if (length < 0.001f) return null;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        float th = WorldUnits(segmentThickness);
        var go = Spawn(segmentPrefab, worldA, angle, new Vector2(length, th));
        SetColor(go, col);
        return go;
    }

    private GameObject BuildArrowhead(Vector3 worldPos, Vector3 dir, Color col)
    {
        if (!arrowheadPrefab) return null;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float sz = WorldUnits(arrowheadSize);
        var go = Spawn(arrowheadPrefab, worldPos, angle, new Vector2(sz, sz));
        SetColor(go, col);
        return go;
    }

    private GameObject BuildBendDot(Vector3 worldPos, Color col)
    {
        if (!segmentPrefab) return null;
        float sz = WorldUnits(segmentThickness * 1.4f);
        var go = Spawn(segmentPrefab, worldPos, 0f, new Vector2(sz, sz));
        SetColor(go, col);
        return go;
    }

    #endregion Builders

    #region Spawn

    private GameObject Spawn(GameObject prefab, Vector3 worldPos, float angleDeg, Vector2 worldSize)
    {
        if (!prefab) return null;
        return isCanvasSpace ? SpawnCanvas(prefab, worldPos, angleDeg, worldSize) : SpawnWorld(prefab, worldPos, angleDeg, worldSize);
    }

    private GameObject SpawnWorld(GameObject prefab, Vector3 worldPos, float angleDeg, Vector2 worldSize)
    {
        var go = Instantiate(prefab, boardVisuals.transform);
        go.transform.position = worldPos;
        go.transform.eulerAngles = new Vector3(0f, 0f, angleDeg);
        go.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
        var p = go.transform.localPosition; p.z = -0.15f;
        go.transform.localPosition = p;
        return go;
    }

    private GameObject SpawnCanvas(GameObject prefab, Vector3 worldPos, float angleDeg, Vector2 worldSize)
    {
        if (!parentCanvas) return null;
        Vector2 screen = _cam.WorldToScreenPoint(worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentCanvas.GetComponent<RectTransform>(), screen, parentCanvas.worldCamera, out Vector2 canvasLocal);
        var go = Instantiate(prefab, boardVisuals.transform);
        var rt = go.GetComponent<RectTransform>();
        if (rt)
        {
            rt.anchoredPosition = canvasLocal;
            rt.localEulerAngles = new Vector3(0f, 0f, angleDeg);
            float sf = CanvasScaleFactor();
            rt.sizeDelta = new Vector2(worldSize.x / sf, worldSize.y / sf);
        }
        return go;
    }

    #endregion Spawn

    #region Clear

    public void ClearAll()
    {
        foreach (var kv in _highlights) KillAll(kv.Value);
        foreach (var kv in _arrows) KillAll(kv.Value);
        _highlights.Clear();
        _arrows.Clear();
    }

    private static void KillAll(List<GameObject> list)
    {
        foreach (var go in list) if (go) Object.Destroy(go);
    }

    #endregion Clear

    #region Touch

    private bool _twoFingerActive;
    private Vector2Int _twoFingerOrigin;

    private void HandleTwoFingerAnnotation()
    {
        if (Input.touchCount != 2) { _twoFingerActive = false; return; }
        var t0 = Input.GetTouch(0);
        var t1 = Input.GetTouch(1);
        if (t1.phase == TouchPhase.Began)
        {
            var sq = SquareAt(t0.position);
            if (sq.HasValue) { _twoFingerActive = true; _twoFingerOrigin = sq.Value; }
        }
        if (_twoFingerActive && (t0.phase == TouchPhase.Ended || t1.phase == TouchPhase.Ended))
        {
            _twoFingerActive = false;
            Vector2 releasePos = t0.phase == TouchPhase.Ended ? t0.position : t1.position;
            var sq = SquareAt(releasePos);
            if (!sq.HasValue) return;
            if (sq.Value == _twoFingerOrigin) ToggleHighlight(sq.Value);
            else ToggleArrow(_twoFingerOrigin, sq.Value);
        }
    }

    #endregion Touch

    #region Helpers

    private Vector2Int? SquareAt(Vector2 screenPos)
    {
        float z = Mathf.Abs(_cam.transform.position.z);
        var w = _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, z));
        w.z = 0f;
        return boardVisuals.WorldToSquare(w);
    }

    private Vector3 SquareCentre(Vector2Int sq) =>
        boardVisuals.transform.TransformPoint(boardVisuals.SquareToWorld(sq));

    private float WorldUnits(float worldVal) =>
        isCanvasSpace ? worldVal / CanvasScaleFactor() : worldVal;

    private float CanvasScaleFactor()
    {
        if (!parentCanvas) return 1f;
        float sf = parentCanvas.scaleFactor;
        return sf > 0f ? sf : 1f;
    }

    private static void SetColor(GameObject go, Color c)
    {
        if (!go) return;
        var img = go.GetComponent<Image>();
        if (img) { img.color = c; return; }
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr) sr.color = c;
    }

    #endregion Helpers
}