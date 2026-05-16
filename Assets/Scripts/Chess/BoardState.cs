using System;
using System.Collections.Generic;
using UnityEngine;

public enum PieceType { None, Pawn, Knight, Bishop, Rook, Queen, King }
public enum PieceColor { None, White, Black }

[Serializable]
public struct ChessPieceData
{
    public PieceType Type;
    public PieceColor Color;

    public static readonly ChessPieceData Empty = new() { Type = PieceType.None, Color = PieceColor.None };
    public bool IsEmpty => Type == PieceType.None;

    public char ToFenChar()
    {
        char c = Type switch
        {
            PieceType.Pawn => 'p', PieceType.Knight => 'n', PieceType.Bishop => 'b',
            PieceType.Rook => 'r', PieceType.Queen => 'q', PieceType.King => 'k',
            _ => '.',
        };
        return Color == PieceColor.White ? char.ToUpper(c) : c;
    }
}

[Serializable]
public struct BoardMove
{
    public Vector2Int From;
    public Vector2Int To;
    public PieceType Promotion;
    public PieceType MovedPiece;
    public bool IsCastling;
    public bool IsEnPassant;
    public bool IsCapture;

    public static bool TryParseUCI(string uci, out BoardMove move)
    {
        move = default;
        if (uci == null || uci.Length < 4) return false;
        int fc = uci[0] - 'a', fr = uci[1] - '1';
        int tc = uci[2] - 'a', tr = uci[3] - '1';
        if (fc < 0 || fc > 7 || fr < 0 || fr > 7 || tc < 0 || tc > 7 || tr < 0 || tr > 7) return false;
        move.From = new Vector2Int(fc, fr);
        move.To = new Vector2Int(tc, tr);
        move.Promotion = uci.Length > 4 ? CharToPiece(uci[4]) : PieceType.None;
        return true;
    }

    public string ToUCI()
    {
        string s = $"{(char)('a' + From.x)}{From.y + 1}{(char)('a' + To.x)}{To.y + 1}";
        if (Promotion != PieceType.None) s += PieceToChar(Promotion);
        return s;
    }

    public string ToAlgebraic()
    {
        if (IsCastling) return To.x == 6 ? "O-O" : "O-O-O";
        string pc = MovedPiece switch
        {
            PieceType.Knight => "N", PieceType.Bishop => "B", PieceType.Rook => "R",
            PieceType.Queen => "Q", PieceType.King => "K", _ => "",
        };
        string toSq = $"{(char)('a' + To.x)}{To.y + 1}";
        string promo = Promotion != PieceType.None ? $"={PieceToChar(Promotion).ToString().ToUpper()}" : "";
        string fromFile = (MovedPiece == PieceType.Pawn && IsCapture) ? ((char)('a' + From.x)).ToString() : "";
        string capStr = IsCapture ? "x" : "";
        return $"{pc}{fromFile}{capStr}{toSq}{promo}";
    }

    private static PieceType CharToPiece(char c) => char.ToLower(c) switch
    { 'q' => PieceType.Queen, 'r' => PieceType.Rook, 'b' => PieceType.Bishop, 'n' => PieceType.Knight, _ => PieceType.None };

    private static char PieceToChar(PieceType t) => t switch
    { PieceType.Queen => 'q', PieceType.Rook => 'r', PieceType.Bishop => 'b', PieceType.Knight => 'n', _ => '?' };
}

/// <summary>
/// pure chess state; board array, move history, castling rights, en passant, clocks.
/// </summary>

public class BoardState
{
    public const string StartFEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private ChessPieceData[,] _board = new ChessPieceData[8, 8];
    private Stack<(BoardMove move, ChessPieceData captured)> _history = new();

    public PieceColor ActiveColor { get; set; } = PieceColor.White;
    public bool WhiteCanCastleK { get; set; } = true;
    public bool WhiteCanCastleQ { get; set; } = true;
    public bool BlackCanCastleK { get; set; } = true;
    public bool BlackCanCastleQ { get; set; } = true;
    public Vector2Int? EnPassantSquare { get; set; }
    public int HalfmoveClock { get; set; }
    public int FullmoveNumber { get; set; } = 1;
    public int MoveCount => _history.Count;

    public ChessPieceData Get(Vector2Int sq) => _board[sq.x, sq.y];
    public ChessPieceData Get(int col, int row) => _board[col, row];
    public void SetPiece(Vector2Int sq, ChessPieceData p) => _board[sq.x, sq.y] = p;

    #region FEN

    public void LoadFEN(string fen)
    {
        _board = new ChessPieceData[8, 8];
        _history.Clear();
        string[] parts = fen.Split(' ');
        ParsePiecePlacement(parts[0]);
        ActiveColor = parts.Length > 1 && parts[1] == "b" ? PieceColor.Black : PieceColor.White;
        if (parts.Length > 2)
        {
            string cr = parts[2];
            WhiteCanCastleK = cr.Contains('K'); WhiteCanCastleQ = cr.Contains('Q');
            BlackCanCastleK = cr.Contains('k'); BlackCanCastleQ = cr.Contains('q');
        }
        EnPassantSquare = parts.Length > 3 && parts[3] != "-" ? ParseSquare(parts[3]) : (Vector2Int?)null;
        HalfmoveClock = parts.Length > 4 ? int.Parse(parts[4]) : 0;
        FullmoveNumber = parts.Length > 5 ? int.Parse(parts[5]) : 1;
    }

    public string GetFEN()
    {
        var sb = new System.Text.StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                var p = _board[file, rank];
                if (p.IsEmpty) empty++;
                else { if (empty > 0) { sb.Append(empty); empty = 0; } sb.Append(p.ToFenChar()); }
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        sb.Append(' ').Append(ActiveColor == PieceColor.White ? 'w' : 'b');
        string cr = "";
        if (WhiteCanCastleK) cr += 'K'; if (WhiteCanCastleQ) cr += 'Q';
        if (BlackCanCastleK) cr += 'k'; if (BlackCanCastleQ) cr += 'q';
        sb.Append(' ').Append(cr.Length > 0 ? cr : "-");
        sb.Append(' ').Append(EnPassantSquare.HasValue ? $"{(char)('a' + EnPassantSquare.Value.x)}{EnPassantSquare.Value.y + 1}" : "-");
        sb.Append($" {HalfmoveClock} {FullmoveNumber}");
        return sb.ToString();
    }

    #endregion FEN

    #region Move Application

    public ChessPieceData ApplyMove(ref BoardMove move)
    {
        var piece = Get(move.From);
        var captured = Get(move.To);
        move.MovedPiece = piece.Type;
        move.IsCapture = !captured.IsEmpty || move.IsEnPassant;
        _history.Push((move, captured));
        SetPiece(move.To, piece);
        SetPiece(move.From, ChessPieceData.Empty);
        if (move.Promotion != PieceType.None) SetPiece(move.To, new ChessPieceData { Type = move.Promotion, Color = piece.Color });
        if (move.IsCastling) ApplyCastlingRook(move);
        if (move.IsEnPassant) ApplyEPCapture(move, piece.Color);
        UpdateCastlingRights(piece, move);
        EnPassantSquare = IsDoublePush(piece, move) ? new Vector2Int(move.To.x, (move.From.y + move.To.y) / 2) : (Vector2Int?)null;
        HalfmoveClock = (piece.Type == PieceType.Pawn || !captured.IsEmpty) ? 0 : HalfmoveClock + 1;
        if (ActiveColor == PieceColor.Black) FullmoveNumber++;
        ActiveColor = Opponent(ActiveColor);
        return captured;
    }

    public ChessPieceData ApplyMove(BoardMove move) => ApplyMove(ref move);

    public bool UndoMove(out BoardMove undoneMove)
    {
        undoneMove = default;
        if (_history.Count == 0) return false;
        var (move, captured) = _history.Pop();
        undoneMove = move;
        PieceColor movedColor = Opponent(ActiveColor);
        var piece = Get(move.To);
        if (move.Promotion != PieceType.None) piece = new ChessPieceData { Type = PieceType.Pawn, Color = movedColor };
        SetPiece(move.From, piece);
        SetPiece(move.To, captured);
        if (move.IsCastling) UndoCastlingRook(move);
        if (move.IsEnPassant) UndoEPCapture(move, movedColor);
        if (movedColor == PieceColor.Black) FullmoveNumber--;
        ActiveColor = movedColor;
        return true;
    }

    public BoardState Clone()
    {
        var c = new BoardState();
        for (int col = 0; col < 8; col++)
        for (int row = 0; row < 8; row++) c._board[col, row] = _board[col, row];
        c.ActiveColor = ActiveColor;
        c.WhiteCanCastleK = WhiteCanCastleK; c.WhiteCanCastleQ = WhiteCanCastleQ;
        c.BlackCanCastleK = BlackCanCastleK; c.BlackCanCastleQ = BlackCanCastleQ;
        c.EnPassantSquare = EnPassantSquare;
        c.HalfmoveClock = HalfmoveClock;
        c.FullmoveNumber = FullmoveNumber;
        return c;
    }

    #endregion Move Application

    #region Helpers

    public static PieceColor Opponent(PieceColor c) => c == PieceColor.White ? PieceColor.Black : PieceColor.White;

    private void ApplyCastlingRook(BoardMove move)
    {
        bool ks = move.To.x == 6; int rank = move.From.y;
        int ff = ks ? 7 : 0, tf = ks ? 5 : 3;
        var rook = Get(ff, rank);
        SetPiece(new Vector2Int(tf, rank), rook);
        SetPiece(new Vector2Int(ff, rank), ChessPieceData.Empty);
    }

    private void UndoCastlingRook(BoardMove move)
    {
        bool ks = move.To.x == 6; int rank = move.From.y;
        int ff = ks ? 7 : 0, tf = ks ? 5 : 3;
        var rook = Get(tf, rank);
        SetPiece(new Vector2Int(ff, rank), rook);
        SetPiece(new Vector2Int(tf, rank), ChessPieceData.Empty);
    }

    private void ApplyEPCapture(BoardMove move, PieceColor mover)
    {
        int dir = mover == PieceColor.White ? -1 : 1;
        SetPiece(new Vector2Int(move.To.x, move.To.y + dir), ChessPieceData.Empty);
    }

    private void UndoEPCapture(BoardMove move, PieceColor mover)
    {
        int dir = mover == PieceColor.White ? -1 : 1;
        SetPiece(new Vector2Int(move.To.x, move.To.y + dir), new ChessPieceData { Type = PieceType.Pawn, Color = Opponent(mover) });
    }

    private void UpdateCastlingRights(ChessPieceData piece, BoardMove move)
    {
        if (piece.Type == PieceType.King)
        {
            if (piece.Color == PieceColor.White) { WhiteCanCastleK = false; WhiteCanCastleQ = false; }
            else { BlackCanCastleK = false; BlackCanCastleQ = false; }
        }
        if (piece.Type == PieceType.Rook)
        {
            if (move.From == new Vector2Int(0, 0)) WhiteCanCastleQ = false;
            if (move.From == new Vector2Int(7, 0)) WhiteCanCastleK = false;
            if (move.From == new Vector2Int(0, 7)) BlackCanCastleQ = false;
            if (move.From == new Vector2Int(7, 7)) BlackCanCastleK = false;
        }
        if (move.To == new Vector2Int(0, 0)) WhiteCanCastleQ = false;
        if (move.To == new Vector2Int(7, 0)) WhiteCanCastleK = false;
        if (move.To == new Vector2Int(0, 7)) BlackCanCastleQ = false;
        if (move.To == new Vector2Int(7, 7)) BlackCanCastleK = false;
    }

    private bool IsDoublePush(ChessPieceData p, BoardMove m) =>
        p.Type == PieceType.Pawn && Math.Abs(m.To.y - m.From.y) == 2;

    private void ParsePiecePlacement(string s)
    {
        int rank = 7, file = 0;
        foreach (char c in s)
        {
            if (c == '/') { rank--; file = 0; }
            else if (char.IsDigit(c)) file += c - '0';
            else
            {
                _board[file, rank] = new ChessPieceData
                {
                    Color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black,
                    Type = char.ToLower(c) switch
                    {
                        'p' => PieceType.Pawn, 'n' => PieceType.Knight, 'b' => PieceType.Bishop,
                        'r' => PieceType.Rook, 'q' => PieceType.Queen, 'k' => PieceType.King,
                        _ => PieceType.None,
                    }
                };
                file++;
            }
        }
    }

    private static Vector2Int ParseSquare(string s) => new(s[0] - 'a', s[1] - '1');

    #endregion Helpers
}