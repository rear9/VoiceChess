using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// full legal move generator; handles pins, check, castling, en passant, and promotion.
/// </summary>

public static class MoveValidator
{
    private static readonly Vector2Int[] Cardinals = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    private static readonly Vector2Int[] Diagonals = { new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };
    private static readonly Vector2Int[] AllDirs;

    static MoveValidator()
    {
        AllDirs = new Vector2Int[8];
        Cardinals.CopyTo(AllDirs, 0);
        Diagonals.CopyTo(AllDirs, 4);
    }

    #region Public API

    public static List<BoardMove> GetAllLegalMoves(BoardState state)
    {
        var moves = new List<BoardMove>();
        for (int col = 0; col < 8; col++)
        for (int row = 0; row < 8; row++)
        {
            var sq = new Vector2Int(col, row);
            if (state.Get(sq).Color != state.ActiveColor) continue;
            moves.AddRange(GetLegalMovesFrom(state, sq));
        }
        return moves;
    }

    public static List<BoardMove> GetLegalMovesFrom(BoardState state, Vector2Int from)
    {
        var piece = state.Get(from);
        if (piece.IsEmpty || piece.Color != state.ActiveColor) return new();
        var pseudo = PseudoLegal(state, from);
        var legal = new List<BoardMove>(pseudo.Count);
        foreach (var m in pseudo) if (!LeavesOwnKingInCheck(state, m)) legal.Add(m);
        return legal;
    }

    public static List<Vector2Int> GetLegalTargets(BoardState state, Vector2Int from)
    {
        var targets = new List<Vector2Int>();
        foreach (var m in GetLegalMovesFrom(state, from)) targets.Add(m.To);
        return targets;
    }

    public static List<Vector2Int> FindCandidates(BoardState state, PieceType type, Vector2Int target, PieceColor color)
    {
        var result = new List<Vector2Int>();
        for (int col = 0; col < 8; col++)
        for (int row = 0; row < 8; row++)
        {
            var sq = new Vector2Int(col, row);
            var p = state.Get(sq);
            if (p.Type != type || p.Color != color) continue;
            foreach (var m in GetLegalMovesFrom(state, sq))
                if (m.To == target) { result.Add(sq); break; }
        }
        return result;
    }

    public static bool IsInCheck(BoardState state, PieceColor color)
    {
        var king = FindKing(state, color);
        return king.HasValue && IsAttackedBy(state, king.Value, BoardState.Opponent(color));
    }

    public static bool IsCheckmate(BoardState state) =>
        IsInCheck(state, state.ActiveColor) && GetAllLegalMoves(state).Count == 0;

    public static bool IsStalemate(BoardState state) =>
        !IsInCheck(state, state.ActiveColor) && GetAllLegalMoves(state).Count == 0;

    #endregion Public API

    #region Pseudo-Legal Generation

    private static List<BoardMove> PseudoLegal(BoardState state, Vector2Int from)
    {
        var p = state.Get(from);
        return p.Type switch
        {
            PieceType.Pawn => PawnMoves(state, from, p.Color),
            PieceType.Knight => KnightMoves(state, from, p.Color),
            PieceType.Bishop => SliderMoves(state, from, p.Color, Diagonals),
            PieceType.Rook => SliderMoves(state, from, p.Color, Cardinals),
            PieceType.Queen => SliderMoves(state, from, p.Color, AllDirs),
            PieceType.King => KingMoves(state, from, p.Color),
            _ => new(),
        };
    }

    private static List<BoardMove> PawnMoves(BoardState state, Vector2Int from, PieceColor color)
    {
        var moves = new List<BoardMove>();
        int dir = color == PieceColor.White ? 1 : -1;
        int startRank = color == PieceColor.White ? 1 : 6;
        var fwd = new Vector2Int(from.x, from.y + dir);
        if (InBounds(fwd) && state.Get(fwd).IsEmpty)
        {
            AddPawnMove(moves, from, fwd);
            if (from.y == startRank)
            {
                var dbl = new Vector2Int(from.x, from.y + dir * 2);
                if (state.Get(dbl).IsEmpty) moves.Add(new BoardMove { From = from, To = dbl });
            }
        }
        foreach (int dx in new[] { -1, 1 })
        {
            var cap = new Vector2Int(from.x + dx, from.y + dir);
            if (!InBounds(cap)) continue;
            var target = state.Get(cap);
            if (!target.IsEmpty && target.Color != color) AddPawnMove(moves, from, cap);
            if (state.EnPassantSquare.HasValue && state.EnPassantSquare.Value == cap) moves.Add(new BoardMove { From = from, To = cap, IsEnPassant = true });
        }
        return moves;
    }

    private static void AddPawnMove(List<BoardMove> moves, Vector2Int from, Vector2Int to)
    {
        if (to.y == 7 || to.y == 0)
        {
            foreach (var promo in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight }) moves.Add(new BoardMove { From = from, To = to, Promotion = promo });
        }
        else moves.Add(new BoardMove { From = from, To = to });
    }

    private static List<BoardMove> KnightMoves(BoardState state, Vector2Int from, PieceColor color)
    {
        var moves = new List<BoardMove>();
        int[] d = { -2, -1, 1, 2 };
        foreach (int dx in d) foreach (int dy in d)
        {
            if (System.Math.Abs(dx) == System.Math.Abs(dy)) continue;
            var to = new Vector2Int(from.x + dx, from.y + dy);
            if (InBounds(to) && state.Get(to).Color != color) moves.Add(new BoardMove { From = from, To = to });
        }
        return moves;
    }

    private static List<BoardMove> SliderMoves(BoardState state, Vector2Int from, PieceColor color, Vector2Int[] dirs)
    {
        var moves = new List<BoardMove>();
        foreach (var dir in dirs)
        {
            var sq = from + dir;
            while (InBounds(sq))
            {
                var p = state.Get(sq);
                if (p.IsEmpty) moves.Add(new BoardMove { From = from, To = sq });
                else { if (p.Color != color) moves.Add(new BoardMove { From = from, To = sq }); break; }
                sq += dir;
            }
        }
        return moves;
    }

    private static List<BoardMove> KingMoves(BoardState state, Vector2Int from, PieceColor color)
    {
        var moves = new List<BoardMove>();
        foreach (var dir in AllDirs)
        {
            var to = from + dir;
            if (InBounds(to) && state.Get(to).Color != color) moves.Add(new BoardMove { From = from, To = to });
        }
        AddCastling(state, from, color, moves);
        return moves;
    }

    private static void AddCastling(BoardState state, Vector2Int kingPos, PieceColor color, List<BoardMove> moves)
    {
        int rank = color == PieceColor.White ? 0 : 7;
        if (kingPos != new Vector2Int(4, rank)) return;
        if (IsAttackedBy(state, kingPos, BoardState.Opponent(color))) return;
        bool canK = color == PieceColor.White ? state.WhiteCanCastleK : state.BlackCanCastleK;
        bool canQ = color == PieceColor.White ? state.WhiteCanCastleQ : state.BlackCanCastleQ;
        var opp = BoardState.Opponent(color);
        if (canK)
        {
            var f = new Vector2Int(5, rank); var g = new Vector2Int(6, rank);
            if (state.Get(f).IsEmpty && state.Get(g).IsEmpty && !IsAttackedBy(state, f, opp)) moves.Add(new BoardMove { From = kingPos, To = g, IsCastling = true });
        }
        if (canQ)
        {
            var b = new Vector2Int(1, rank); var c = new Vector2Int(2, rank); var d = new Vector2Int(3, rank);
            if (state.Get(b).IsEmpty && state.Get(c).IsEmpty && state.Get(d).IsEmpty && !IsAttackedBy(state, d, opp)) moves.Add(new BoardMove { From = kingPos, To = c, IsCastling = true });
        }
    }

    #endregion Pseudo-Legal Generation

    #region Attack Detection

    public static bool IsAttackedBy(BoardState state, Vector2Int sq, PieceColor attacker)
    {
        int[] kd = { -2, -1, 1, 2 };
        foreach (int dx in kd) foreach (int dy in kd)
        {
            if (System.Math.Abs(dx) == System.Math.Abs(dy)) continue;
            var s = new Vector2Int(sq.x + dx, sq.y + dy);
            if (!InBounds(s)) continue;
            var p = state.Get(s);
            if (p.Type == PieceType.Knight && p.Color == attacker) return true;
        }
        foreach (var dir in Cardinals)
        {
            var s = sq + dir;
            while (InBounds(s))
            {
                var p = state.Get(s);
                if (!p.IsEmpty)
                {
                    if (p.Color == attacker && (p.Type == PieceType.Rook || p.Type == PieceType.Queen)) return true;
                    break;
                }
                s += dir;
            }
        }
        foreach (var dir in Diagonals)
        {
            var s = sq + dir;
            while (InBounds(s))
            {
                var p = state.Get(s);
                if (!p.IsEmpty)
                {
                    if (p.Color == attacker && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen)) return true;
                    break;
                }
                s += dir;
            }
        }
        int pd = attacker == PieceColor.White ? -1 : 1;
        foreach (int dx in new[] { -1, 1 })
        {
            var s = new Vector2Int(sq.x + dx, sq.y + pd);
            if (!InBounds(s)) continue;
            var p = state.Get(s);
            if (p.Type == PieceType.Pawn && p.Color == attacker) return true;
        }
        foreach (var dir in AllDirs)
        {
            var s = sq + dir;
            if (!InBounds(s)) continue;
            var p = state.Get(s);
            if (p.Type == PieceType.King && p.Color == attacker) return true;
        }
        return false;
    }

    #endregion Attack Detection

    #region Check Helpers

    private static bool LeavesOwnKingInCheck(BoardState state, BoardMove move)
    {
        var copy = state.Clone();
        copy.ApplyMove(move);
        return IsInCheck(copy, BoardState.Opponent(copy.ActiveColor));
    }

    private static Vector2Int? FindKing(BoardState state, PieceColor color)
    {
        for (int col = 0; col < 8; col++)
        for (int row = 0; row < 8; row++)
        {
            var p = state.Get(col, row);
            if (p.Type == PieceType.King && p.Color == color) return new Vector2Int(col, row);
        }
        return null;
    }

    private static bool InBounds(Vector2Int sq) => sq.x is >= 0 and <= 7 && sq.y is >= 0 and <= 7;

    #endregion Check Helpers
}