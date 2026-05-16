using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// manages piece GameObjects on the board,
/// board: 1120×1120 px, centred at world (0,0), Piece sprites: 140×140,
/// supports board-flip for playing as Black (coordinate mirror, no rotation),
/// exposes WorldToSquare and SquareToWorld for drag system.
/// </summary>

public class BoardVisuals : MonoBehaviour
{
    [Header("References")]
    public Board board;
    public BlindfoldController blindfold;

    [Header("Prefab")]
    public GameObject piecePrefab;

    [Header("Layout")]
    public float boardSize = 1120f;
    public float pieceSize = 140f;

    [Header("Movement animation")]
    [Tooltip("Animate piece slides. Disable for pure blindfold play.")]
    public bool animateMoves = true;
    [Tooltip("Duration of the slide in seconds.")]
    [Range(0.05f, 0.4f)]
    public float moveDuration = 0.13f;

    public Dictionary<Vector2Int, GameObject> PieceObjects { get; } = new();
    public Dictionary<Vector2Int, Sprite> OriginalSprites { get; } = new();

    private Dictionary<string, Sprite> _sprites = new();
    private Dictionary<GameObject, Coroutine> _activeLerps = new();
    private bool _isFlipped;

    private float Half => boardSize * 0.5f;
    private float HalfPiece => pieceSize * 0.5f;

    private static readonly string[] PieceKeys = { "wp","wn","wb","wr","wq","wk","bp","bn","bb","br","bq","bk" };

    #region Init

    private void Awake()
    {
        LoadSprites();
        Board.OnMoveMade += HandleMoveMade;
        Board.OnMoveUndone += HandleMoveUndone;
        Board.OnColorAssigned += HandleColorAssigned;
    }

    private void OnDestroy()
    {
        Board.OnMoveMade -= HandleMoveMade;
        Board.OnMoveUndone -= HandleMoveUndone;
        Board.OnColorAssigned -= HandleColorAssigned;
    }

    private void Start() => RebuildFromBoard();

    #endregion Init

    #region Events

    private void HandleMoveMade(BoardMove move)
    {
        if (move.IsEnPassant)
        {
            var epSq = new Vector2Int(move.To.x, move.From.y);
            RemovePieceAt(epSq);
        }
        if (!move.IsEnPassant)
            RemovePieceAt(move.To);
        if (PieceObjects.TryGetValue(move.From, out var go))
        {
            PieceObjects.Remove(move.From);
            OriginalSprites.Remove(move.From);
            PieceObjects[move.To] = go;
            Vector3 target = SquareToWorld(move.To);
            if (animateMoves)
                StartLerp(go, target);
            else
                go.transform.localPosition = target;
        }
        if (move.IsCastling) MoveCastlingRook(move);
        if (move.Promotion != PieceType.None && PieceObjects.TryGetValue(move.To, out var proGo))
        {
            var data = board.Get(move.To);
            var spr = GetSprite(data);
            SetSprite(proGo, spr, move.To);
        }
        RefreshVisibility();
    }

    private void HandleMoveUndone(BoardMove _) => RebuildFromBoard();

    private void HandleColorAssigned(PieceColor color)
    {
        _isFlipped = color == PieceColor.Black;
        RebuildFromBoard();
    }

    #endregion Events

    #region Rebuild

    public void RebuildFromBoard()
    {
        StopAllLerps();
        foreach (var kv in PieceObjects) if (kv.Value) Destroy(kv.Value);
        PieceObjects.Clear();
        OriginalSprites.Clear();
        for (int col = 0; col < 8; col++)
        for (int row = 0; row < 8; row++)
        {
            var sq = new Vector2Int(col, row);
            var data = board.Get(sq);
            if (data.IsEmpty) continue;
            SpawnPiece(sq, data);
        }
        RefreshVisibility();
    }

    private void SpawnPiece(Vector2Int sq, ChessPieceData data)
    {
        var go = Instantiate(piecePrefab, transform);
        go.transform.localPosition = SquareToWorld(sq);
        go.name = $"{data.ToFenChar()}@{(char)('a' + sq.x)}{sq.y + 1}";
        var spr = GetSprite(data);
        SetSprite(go, spr, sq);
        PieceObjects[sq] = go;
    }

    #endregion Rebuild

    #region Visibility

    public void RefreshVisibility()
    {
        blindfold?.ApplyVisibility(PieceObjects, board, OriginalSprites);
    }

    #endregion Visibility

    #region Board flip

    public void FlipBoard(bool blackAtBottom)
    {
        _isFlipped = blackAtBottom;
        RebuildFromBoard();
    }

    #endregion Board flip

    #region Coordinate helpers

    public Vector3 SquareToWorld(Vector2Int sq)
    {
        int dc = _isFlipped ? 7 - sq.x : sq.x;
        int dr = _isFlipped ? 7 - sq.y : sq.y;
        float x = -Half + HalfPiece + dc * pieceSize;
        float y = -Half + HalfPiece + dr * pieceSize;
        return new Vector3(x, y, -0.1f);
    }

    public Vector2Int? WorldToSquare(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        int dc = Mathf.FloorToInt((local.x + Half) / pieceSize);
        int dr = Mathf.FloorToInt((local.y + Half) / pieceSize);
        if (dc < 0 || dc > 7 || dr < 0 || dr > 7) return null;
        int col = _isFlipped ? 7 - dc : dc;
        int row = _isFlipped ? 7 - dr : dr;
        return new Vector2Int(col, row);
    }

    #endregion Coordinate helpers

    #region Lerp helpers

    private void StartLerp(GameObject go, Vector3 target)
    {
        if (_activeLerps.TryGetValue(go, out var old) && old != null)
            StopCoroutine(old);
        var c = StartCoroutine(LerpPiece(go, target));
        _activeLerps[go] = c;
    }

    private IEnumerator LerpPiece(GameObject go, Vector3 target)
    {
        if (!go) yield break;
        Vector3 start = go.transform.localPosition;
        float t = 0f;
        while (t < 1f && go)
        {
            t += Time.deltaTime / moveDuration;
            go.transform.localPosition = Vector3.Lerp(start, target, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t)));
            yield return null;
        }
        if (go) go.transform.localPosition = target;
        _activeLerps.Remove(go);
    }

    private void StopAllLerps()
    {
        foreach (var kv in _activeLerps)
            if (kv.Value != null) StopCoroutine(kv.Value);
        _activeLerps.Clear();
    }

    #endregion Lerp helpers

    #region Sprites

    private void LoadSprites()
    {
        var loaded = Resources.LoadAll<Sprite>("Pieces");
        foreach (var s in loaded) _sprites[s.name.ToLower()] = s;
        foreach (var key in PieceKeys)
            if (!_sprites.ContainsKey(key))
                Debug.LogWarning($"[BoardVisuals] Missing sprite: Resources/Pieces/{key}");
    }

    private Sprite GetSprite(ChessPieceData data)
    {
        char color = data.Color == PieceColor.White ? 'w' : 'b';
        char type = data.Type switch
        {
            PieceType.Pawn => 'p', PieceType.Knight => 'n', PieceType.Bishop => 'b',
            PieceType.Rook => 'r', PieceType.Queen => 'q', PieceType.King => 'k',
            _ => '?',
        };
        return _sprites.GetValueOrDefault($"{color}{type}");
    }

    private void SetSprite(GameObject go, Sprite spr, Vector2Int sq)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        if (!sr) return;
        sr.sprite = spr;
        OriginalSprites[sq] = spr;
    }

    #endregion Sprites

    #region Piece removal

    private void RemovePieceAt(Vector2Int sq)
    {
        if (PieceObjects.TryGetValue(sq, out var cap))
        {
            if (_activeLerps.TryGetValue(cap, out var lc) && lc != null)
                StopCoroutine(lc);
            _activeLerps.Remove(cap);
            Destroy(cap);
            PieceObjects.Remove(sq);
            OriginalSprites.Remove(sq);
        }
    }

    #endregion Piece removal

    #region Castling rook

    private void MoveCastlingRook(BoardMove kingMove)
    {
        bool ks = kingMove.To.x == 6;
        int rank = kingMove.From.y;
        var fromSq = new Vector2Int(ks ? 7 : 0, rank);
        var toSq = new Vector2Int(ks ? 5 : 3, rank);
        if (PieceObjects.TryGetValue(fromSq, out var rook))
        {
            PieceObjects.Remove(fromSq);
            if (OriginalSprites.TryGetValue(fromSq, out var spr)) { OriginalSprites.Remove(fromSq); OriginalSprites[toSq] = spr; }
            PieceObjects[toSq] = rook;
            Vector3 target = SquareToWorld(toSq);
            if (animateMoves)
                StartLerp(rook, target);
            else
                rook.transform.localPosition = target;
        }
    }

    #endregion Castling rook
}