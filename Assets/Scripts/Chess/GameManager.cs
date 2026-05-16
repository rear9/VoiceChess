using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Controls game flow, routes voice commands, and drives Stockfish queries.
/// </summary>

public class GameManager : MonoBehaviour
{
    [Header("References")]
    public Board board;
    public BoardVisuals boardVisual;
    public StockfishBridge stockfish;
    public PlaySceneUI ui;

    [Header("Defaults")]
    public bool stockfishEnabled = false;
    public int eloPresetIndex = 5;

    private bool _awaitingStockfish;
    private bool _gameOver;
    private EloPreset _preset;

    #region Init

    private void Start()
    {
        stockfishEnabled = PlayerPrefs.GetInt("StockfishEnabled", 0) == 1;
        eloPresetIndex = PlayerPrefs.GetInt("EloPresetIndex", 5);
        int colorPref = PlayerPrefs.GetInt("PlayerColor", 0);
        int blindfold = PlayerPrefs.GetInt("BlindfoldMode", 0);
        _preset = EloTable.FromIndex(eloPresetIndex);
        GetComponent<BlindfoldController>()?.SetMode((BlindfoldMode)blindfold);
        var localColor = colorPref == 1 ? PieceColor.Black : PieceColor.White;
        board.NewGame(localColor);
        ui?.ShowFeedback($"Playing as {localColor}  |  {_preset}");
        if (stockfishEnabled)
        {
            stockfish?.ApplyPreset(_preset);
            if (localColor == PieceColor.Black) StartCoroutine(WaitForEngineAndMove());
        }
    }

    private void OnEnable()
    {
        SpeechManager.OnCommandRecognized += HandleCommand;
        Board.OnMoveMade += OnMoveMade;
        Board.OnCheckmateDetected += OnCheckmate;
        Board.OnStalemateDetected += OnStalemate;
        Board.OnCheckDetected += OnCheck;
        Board.OnColorAssigned += OnColorAssigned;
    }

    private void OnDisable()
    {
        SpeechManager.OnCommandRecognized -= HandleCommand;
        Board.OnMoveMade -= OnMoveMade;
        Board.OnCheckmateDetected -= OnCheckmate;
        Board.OnStalemateDetected -= OnStalemate;
        Board.OnCheckDetected -= OnCheck;
        Board.OnColorAssigned -= OnColorAssigned;
    }

    private void Update() => MainThreadDispatcher.Flush();

    #endregion Init

    #region Command Routing

    private void HandleCommand(VoiceCommand cmd)
    {
        if (cmd.Type == CommandType.Restart) { HandleRestart(); return; }
        if (cmd.Type == CommandType.ToggleEngine) { HandleEngineToggle(cmd.Payload); return; }
        if (cmd.Type == CommandType.TogglePushToTalk) { HandlePTTToggle(cmd.Payload); return; }
        if (_gameOver && cmd.Type != CommandType.MenuBack && cmd.Type != CommandType.Pause) return;
        switch (cmd.Type)
        {
            case CommandType.MoveFromTo: HandleFromTo(cmd.Payload); break;
            case CommandType.MovePieceTo: HandlePieceTo(cmd.Payload); break;
            case CommandType.MovePawnTo: HandlePawnTo(cmd.Payload); break;
            case CommandType.CastleKingside: HandleCastle(true); break;
            case CommandType.CastleQueenside: HandleCastle(false); break;
            case CommandType.Undo: HandleUndo(); break;
            case CommandType.Resign: HandleResign(); break;
            case CommandType.SwitchColor: HandleSwitchColor(); break;
            case CommandType.Pause:
            case CommandType.MenuBack: SceneManager.LoadScene("Menu"); break;
            case CommandType.ToggleHint: RequestHint(); break;
        }
    }

    #endregion Command Routing

    #region Move Handlers

    private void HandleFromTo(string payload)
    {
        if (!BoardMove.TryParseUCI(payload, out var move))
        { ui?.ShowFeedback($"couldn't parse: {payload}"); return; }
        if (board.Get(move.From).Type == PieceType.King && System.Math.Abs(move.To.x - move.From.x) == 2) move.IsCastling = true;
        TryApplyMove(move);
    }

    private void HandlePieceTo(string payload)
    {
        if (payload.Length < 3) return;
        char pc = payload[0];
        string sqS = payload.Substring(1);
        if (sqS.Length != 2) return;
        var target = new Vector2Int(sqS[0] - 'a', sqS[1] - '1');
        if (target.x < 0 || target.x > 7 || target.y < 0 || target.y > 7)
        { ui?.ShowFeedback("invalid square."); return; }
        PieceType type = pc switch
        {
            'N' => PieceType.Knight, 'B' => PieceType.Bishop, 'R' => PieceType.Rook,
            'Q' => PieceType.Queen, 'K' => PieceType.King, 'P' => PieceType.Pawn,
            _ => PieceType.None,
        };
        if (type == PieceType.None) return;
        var cands = MoveValidator.FindCandidates(board.State, type, target, board.ActiveColor);
        switch (cands.Count)
        {
            case 0: ui?.ShowFeedback($"no {type} can reach {sqS}."); break;
            case 1: TryApplyMove(new BoardMove { From = cands[0], To = target }); break;
            default:
                string opts = "";
                foreach (var sq in cands) opts += $"{(char)('a' + sq.x)}{sq.y + 1} ";
                ui?.ShowFeedback($"which {type}? ({opts.Trim()})");
                break;
        }
    }

    private void HandlePawnTo(string payload)
    {
        if (payload.Length != 2) return;
        var target = new Vector2Int(payload[0] - 'a', payload[1] - '1');
        if (target.x < 0 || target.x > 7 || target.y < 0 || target.y > 7) return;
        var cands = MoveValidator.FindCandidates(board.State, PieceType.Pawn, target, board.ActiveColor);
        switch (cands.Count)
        {
            case 0: ui?.ShowFeedback($"no pawn can move to {payload}."); break;
            case 1: TryApplyMove(new BoardMove { From = cands[0], To = target }); break;
            default:
                ui?.ShowFeedback($"which pawn? say e.g. \"{(char)('a' + cands[0].x)} takes {payload}\"");
                break;
        }
    }

    private void HandleCastle(bool kingSide)
    {
        int rank = board.ActiveColor == PieceColor.White ? 0 : 7;
        TryApplyMove(new BoardMove
        {
            From = new Vector2Int(4, rank),
            To = new Vector2Int(kingSide ? 6 : 2, rank),
            IsCastling = true,
        });
    }

    private void HandleUndo()
    {
        if (board.MoveCount == 0) { ui?.ShowFeedback("nothing to undo."); return; }
        int count = stockfishEnabled && board.MoveCount >= 2 ? 2 : 1;
        int undone = board.UndoMoves(count);
        ui?.ShowFeedback(undone == 2 ? "move and engine reply taken back." : "move taken back.");
    }

    private void HandleResign()
    {
        _gameOver = true;
        ui?.ShowFeedback($"{board.LocalColor} resigned.");
        ui?.ShowGameOver($"{BoardState.Opponent(board.LocalColor)} wins by resignation.");
    }

    private void HandleRestart() => SceneManager.LoadScene("Play");

    #endregion Move Handlers

    #region Engine & PTT Toggles

    private void HandleEngineToggle(string payload)
    {
        stockfishEnabled = payload == "on" || (payload != "off" && !stockfishEnabled);
        PlayerPrefs.SetInt("StockfishEnabled", stockfishEnabled ? 1 : 0);
        PlayerPrefs.Save();
        ui?.ShowFeedback($"engine {(stockfishEnabled ? "enabled" : "disabled")}.");
    }

    private void HandlePTTToggle(string payload)
    {
        bool ptt = payload == "on" || (payload != "off" && !(SpeechManager.Instance?.pushToTalk ?? true));
        if (SpeechManager.Instance != null)
        {
            SpeechManager.Instance.pushToTalk = ptt;
            if (!ptt) SpeechManager.Instance.StartRecording();
        }
        PlayerPrefs.SetInt("PushToTalk", ptt ? 1 : 0);
        PlayerPrefs.Save();
        ui?.ShowFeedback($"push-to-talk {(ptt ? "on" : "off")}.");
    }

    #endregion Engine & PTT Toggles

    #region Color Selection

    public void HandleSwitchColor()
    {
        if (board.MoveCount > 0) { ui?.ShowFeedback("say 'restart' to switch colour."); return; }
        var newColor = BoardState.Opponent(board.LocalColor);
        PlayerPrefs.SetInt("PlayerColor", newColor == PieceColor.Black ? 1 : 0);
        PlayerPrefs.Save();
        board.SetLocalColor(newColor);
        ui?.ShowFeedback($"now playing as {newColor}.");
        if (newColor == PieceColor.Black && stockfishEnabled) StartCoroutine(WaitForEngineAndMove());
    }

    private void OnColorAssigned(PieceColor color) => boardVisual?.FlipBoard(color == PieceColor.Black);

    #endregion Color Selection

    #region Apply Move

    private void TryApplyMove(BoardMove move)
    {
        if (stockfishEnabled && board.ActiveColor != board.LocalColor)
        { ui?.ShowFeedback("engine is thinking…"); return; }
        if (!board.ApplyMove(move)) ui?.ShowFeedback("illegal move.");
    }

    #endregion Apply Move

    #region Engine

    private IEnumerator WaitForEngineAndMove()
    {
        float timeout = 15f, elapsed = 0f;
        ui?.ShowFeedback("waiting for engine…");
        while (stockfish != null && !stockfish.IsReady)
        {
            elapsed += Time.deltaTime;
            if (elapsed > timeout) { ui?.ShowFeedback("engine timeout."); yield break; }
            yield return null;
        }
        _awaitingStockfish = true;
        yield return StartCoroutine(AskStockfish());
    }

    private void OnMoveMade(BoardMove _)
    {
        if (!stockfishEnabled || _awaitingStockfish || _gameOver) return;
        if (board.ActiveColor != board.LocalColor)
        {
            _awaitingStockfish = true;
            StartCoroutine(AskStockfish());
        }
    }

    private IEnumerator AskStockfish()
    {
        ui?.ShowFeedback("engine thinking…");
        var task = stockfish.GetBestMoveAsync(board.GetFEN(), _preset.Depth);
        while (!task.IsCompleted) yield return null;
        _awaitingStockfish = false;
        if (!string.IsNullOrEmpty(task.Result)) board.ApplyUCIMove(task.Result);
    }

    private void RequestHint()
    {
        if (!stockfishEnabled) { ui?.ShowFeedback("enable engine for hints."); return; }
        StartCoroutine(ShowHint());
    }

    private IEnumerator ShowHint()
    {
        var task = stockfish.GetBestMoveAsync(board.GetFEN(), _preset.Depth);
        while (!task.IsCompleted) yield return null;
        ui?.ShowFeedback(task.Result != null ? $"hint: {task.Result}" : "no hint.");
    }

    #endregion Engine

    #region Game State

    private void OnCheckmate(PieceColor loser)
    {
        _gameOver = true;
        string winner = loser == PieceColor.White ? "Black" : "White";
        ui?.ShowFeedback($"checkmate! {winner} wins.");
        ui?.ShowGameOver($"Checkmate — {winner} wins!");
    }

    private void OnStalemate()
    {
        _gameOver = true;
        ui?.ShowFeedback("stalemate — draw!");
        ui?.ShowGameOver("Stalemate — Draw!");
    }

    private void OnCheck(PieceColor color) => ui?.ShowFeedback($"{color} is in check!");

    #endregion Game State
}