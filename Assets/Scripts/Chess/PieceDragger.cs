using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// mouse/touch drag-to-move, piece centres on cursor; cancels cleanly on focus loss.
/// </summary>

public class PieceDragger : MonoBehaviour
{
    [Header("References")]
    public Board board;
    public BoardVisuals visual;
    public BoardAnnotations annotations;

    [Header("Legal Move Dots")]
    public GameObject legalDotPrefab;
    public Color dotColor = new Color(0.2f, 0.8f, 0.2f, 0.6f);
    public Color captureDotColor = new Color(0.9f, 0.2f, 0.2f, 0.6f);

    [Header("Z Depth While Dragging")]
    public float dragZ = -0.5f;

    private GameObject _dragGO;
    private Vector2Int _dragOrigin;
    private List<GameObject> _dots = new();
    private List<BoardMove> _legal;
    private bool _isDragging;
    private Camera _cam;

    #region Init

    private void Awake() => _cam = Camera.main;

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && _isDragging) CancelDrag();
    }

    #endregion Init

    #region Update

    private void Update()
    {
        if (Input.GetMouseButtonDown(0)) TryBeginDrag(Input.mousePosition);
        if (_isDragging && Input.GetMouseButton(0)) DoDrag(Input.mousePosition);
        if (_isDragging && Input.GetMouseButtonUp(0)) EndDrag(Input.mousePosition);
        if (Input.touchCount == 1)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began) TryBeginDrag(t.position);
            if (t.phase == TouchPhase.Moved && _isDragging) DoDrag(t.position);
            if (t.phase == TouchPhase.Ended && _isDragging) EndDrag(t.position);
            if (t.phase == TouchPhase.Canceled && _isDragging) CancelDrag();
        }
    }

    #endregion Update

    #region Drag

    private void TryBeginDrag(Vector2 screen)
    {
        annotations?.ClearAll();
        var sq = ScreenToSquare(screen);
        if (!sq.HasValue) return;
        var piece = board.Get(sq.Value);
        if (piece.IsEmpty || piece.Color != board.LocalColor || piece.Color != board.ActiveColor) return;
        _legal = MoveValidator.GetLegalMovesFrom(board.State, sq.Value);
        if (_legal.Count == 0) return;
        if (!visual.PieceObjects.TryGetValue(sq.Value, out _dragGO)) return;
        _dragOrigin = sq.Value;
        _isDragging = true;
        var p = _dragGO.transform.position; p.z = dragZ;
        _dragGO.transform.position = p;
        ShowDots();
    }

    private void DoDrag(Vector2 screen)
    {
        if (!_dragGO) return;
        Vector3 w = ScreenToWorld(screen); w.z = dragZ;
        _dragGO.transform.position = w;
    }

    private void EndDrag(Vector2 screen)
    {
        ClearDots();
        _isDragging = false;
        if (!_dragGO) return;
        var targetSq = ScreenToSquare(screen);
        bool moved = false;
        if (targetSq.HasValue && targetSq.Value != _dragOrigin)
        {
            foreach (var lm in _legal)
            {
                if (lm.To != targetSq.Value) continue;
                var move = lm;
                if (move.Promotion == PieceType.None && board.Get(_dragOrigin).Type == PieceType.Pawn && (targetSq.Value.y == 7 || targetSq.Value.y == 0)) move.Promotion = PieceType.Queen;
                moved = board.ApplyMove(move);
                break;
            }
        }
        if (!moved) _dragGO.transform.localPosition = visual.SquareToWorld(_dragOrigin);
        _dragGO = null;
        _legal = null;
    }

    private void CancelDrag()
    {
        ClearDots();
        _isDragging = false;
        if (_dragGO) _dragGO.transform.localPosition = visual.SquareToWorld(_dragOrigin);
        _dragGO = null;
        _legal = null;
    }

    #endregion Drag

    #region Legal Dots

    private void ShowDots()
    {
        if (!legalDotPrefab) return;
        foreach (var move in _legal)
        {
            var dot = Instantiate(legalDotPrefab, visual.transform);
            var pos = visual.SquareToWorld(move.To); pos.z = -0.2f;
            dot.transform.localPosition = pos;
            bool isCapture = !board.Get(move.To).IsEmpty || move.IsEnPassant;
            var sr = dot.GetComponent<SpriteRenderer>();
            var img = dot.GetComponent<UnityEngine.UI.Image>();
            if (sr) sr.color = isCapture ? captureDotColor : dotColor;
            if (img) img.color = isCapture ? captureDotColor : dotColor;
            _dots.Add(dot);
        }
    }

    private void ClearDots()
    {
        foreach (var d in _dots) if (d) Destroy(d);
        _dots.Clear();
    }

    #endregion Legal Dots

    #region Helpers

    private Vector3 ScreenToWorld(Vector2 screen)
    {
        float z = Mathf.Abs(_cam.transform.position.z);
        var w = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, z));
        w.z = 0f;
        return w;
    }

    private Vector2Int? ScreenToSquare(Vector2 screen) => visual.WorldToSquare(ScreenToWorld(screen));

    #endregion Helpers
}