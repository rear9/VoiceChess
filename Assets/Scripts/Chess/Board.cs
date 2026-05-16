using System;
using UnityEngine;

/// <summary>
/// MonoBehaviour wrapper around BoardState. fires events on moves and game state changes.
/// </summary>

public class Board : MonoBehaviour
{
    public static event Action<BoardMove> OnMoveMade;
    public static event Action<BoardMove> OnMoveUndone;
    public static event Action<PieceColor> OnCheckDetected;
    public static event Action<PieceColor> OnCheckmateDetected;
    public static event Action OnStalemateDetected;
    public static event Action<PieceColor> OnColorAssigned;

    public BoardState State { get; private set; } = new();
    public PieceColor LocalColor { get; private set; } = PieceColor.White;
    public PieceColor ActiveColor => State.ActiveColor;
    public int MoveCount => State.MoveCount;
    public Vector2Int? EnPassantSquare => State.EnPassantSquare;
    public bool WhiteCanCastleK => State.WhiteCanCastleK;
    public bool WhiteCanCastleQ => State.WhiteCanCastleQ;
    public bool BlackCanCastleK => State.BlackCanCastleK;
    public bool BlackCanCastleQ => State.BlackCanCastleQ;

    public ChessPieceData Get(Vector2Int sq) => State.Get(sq);
    public ChessPieceData Get(int col, int row) => State.Get(col, row);

    #region Init

    private void Awake()
    {
        State = new BoardState();
        State.LoadFEN(BoardState.StartFEN);
    }

    public void NewGame(PieceColor localColor = PieceColor.White)
    {
        State = new BoardState();
        State.LoadFEN(BoardState.StartFEN);
        LocalColor = localColor;
        OnColorAssigned?.Invoke(localColor);
    }

    public void SetLocalColor(PieceColor color)
    {
        LocalColor = color;
        OnColorAssigned?.Invoke(color);
    }

    #endregion Init

    #region Move Application

    public bool ApplyMove(BoardMove move)
    {
        var legal = MoveValidator.GetLegalMovesFrom(State, move.From);
        BoardMove matched = default;
        bool found = false;
        foreach (var lm in legal)
        {
            if (lm.To != move.To) continue;
            if (move.Promotion != PieceType.None && lm.Promotion != move.Promotion) continue;
            matched = lm;
            if (matched.Promotion == PieceType.None && move.Promotion == PieceType.None && State.Get(move.From).Type == PieceType.Pawn && (move.To.y == 7 || move.To.y == 0)) matched.Promotion = PieceType.Queen;
            found = true;
            break;
        }
        if (!found) { Debug.LogWarning($"[Board] illegal move: {move.ToUCI()}"); return false; }
        State.ApplyMove(ref matched);
        OnMoveMade?.Invoke(matched);
        CheckGameState();
        return true;
    }

    public int UndoMoves(int count = 1)
    {
        int undone = 0;
        for (int i = 0; i < count; i++)
        {
            if (!State.UndoMove(out var move)) break;
            OnMoveUndone?.Invoke(move);
            undone++;
        }
        return undone;
    }

    public string GetFEN() => State.GetFEN();

    public bool ApplyUCIMove(string uci)
    {
        if (!BoardMove.TryParseUCI(uci, out var move)) return false;
        var piece = Get(move.From);
        if (piece.Type == PieceType.King && Math.Abs(move.To.x - move.From.x) == 2)
            move.IsCastling = true;
        return ApplyMove(move);
    }

    private void CheckGameState()
    {
        if (MoveValidator.IsCheckmate(State)) OnCheckmateDetected?.Invoke(ActiveColor);
        else if (MoveValidator.IsStalemate(State)) OnStalemateDetected?.Invoke();
        else if (MoveValidator.IsInCheck(State, ActiveColor)) OnCheckDetected?.Invoke(ActiveColor);
    }

    #endregion Move Application
}