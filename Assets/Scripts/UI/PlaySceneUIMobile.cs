using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlaySceneUIMobile : MonoBehaviour
{
    [Header("Feedback toast")]
    public TextMeshProUGUI feedbackText;
    public CanvasGroup feedbackGroup;
    public float feedbackDuration = 3f;

    [Header("Transcript (live)")]
    public TextMeshProUGUI transcriptText;
    [Tooltip("Color for partial (in-progress) transcripts.")]
    public Color partialColor = new Color(1f, 1f, 1f, 0.55f);
    [Tooltip("Color for confirmed final transcripts.")]
    public Color finalColor = Color.white;

    [Header("Recording indicator")]
    public GameObject recordingDot;
    public TextMeshProUGUI recordingLabel;

    [Header("Game info")]
    public TextMeshProUGUI turnLabel;
    public TextMeshProUGUI blindfoldLabel;
    public TextMeshProUGUI checkLabel;

    [Header("Move history — two TMP labels")]
    public TextMeshProUGUI whiteHistoryText;
    public TextMeshProUGUI blackHistoryText;

    [Header("Buttons")]
    public Button undoButton;
    public Button menuButton;
    public Button hintButton;
    public Button switchColorButton;

    [Header("Game over panel")]
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverLabel;
    public Button newGameButton;
    public Button goMenuButton;

    private int _moveNumber = 1;
    private bool _gameOver;

    private void Awake()
    {
        undoButton?.onClick.AddListener(() => SpeechManager.Instance?.InjectCommand(new VoiceCommand(CommandType.Undo)));
        menuButton?.onClick.AddListener(() => UnityEngine.SceneManagement.SceneManager.LoadScene("Menu"));
        hintButton?.onClick.AddListener(() => SpeechManager.Instance?.InjectCommand(new VoiceCommand(CommandType.ToggleHint)));
        switchColorButton?.onClick.AddListener(OnSwitchColor);
        newGameButton?.onClick.AddListener(() => UnityEngine.SceneManagement.SceneManager.LoadScene("Play"));
        goMenuButton?.onClick.AddListener(() => UnityEngine.SceneManagement.SceneManager.LoadScene("Menu"));
    }

    private void OnEnable()
    {
        SpeechManager.OnPartialTranscript += OnPartial;
        SpeechManager.OnTranscriptReady += OnFinalTranscript;
        SpeechManager.OnRecordingStateChanged += OnRecordingState;
        Board.OnMoveMade += OnMoveMade;
        Board.OnMoveUndone += OnMoveUndone;
        Board.OnCheckDetected += OnCheck;
        Board.OnCheckmateDetected += OnCheckmate;
        Board.OnStalemateDetected += OnStalemate;
        BlindfoldController.OnBlindfoldChanged += OnBlindfoldChanged;
    }

    private void OnDisable()
    {
        SpeechManager.OnPartialTranscript -= OnPartial;
        SpeechManager.OnTranscriptReady -= OnFinalTranscript;
        SpeechManager.OnRecordingStateChanged -= OnRecordingState;
        Board.OnMoveMade -= OnMoveMade;
        Board.OnMoveUndone -= OnMoveUndone;
        Board.OnCheckDetected -= OnCheck;
        Board.OnCheckmateDetected -= OnCheckmate;
        Board.OnStalemateDetected -= OnStalemate;
        BlindfoldController.OnBlindfoldChanged -= OnBlindfoldChanged;
    }

    private void Start()
    {
        SetBlindfoldLabel((BlindfoldMode)PlayerPrefs.GetInt("BlindfoldMode", 0));
        SetTurnLabel(PieceColor.White);
        ResetHistory();
        gameOverPanel?.SetActive(false);
        checkLabel?.gameObject.SetActive(false);
        if (transcriptText) transcriptText.text = "";
        OnRecordingState(false);
    }

    public void ShowFeedback(string msg)
    {
        if (!feedbackText) return;
        feedbackText.text = msg;
        StopCoroutine(nameof(FadeRoutine));
        StartCoroutine(nameof(FadeRoutine));
    }

    public void ShowGameOver(string msg)
    {
        _gameOver = true;
        gameOverPanel?.SetActive(true);
        if (gameOverLabel) gameOverLabel.text = msg;
    }

    private void OnPartial(string text)
    {
        if (!transcriptText) return;
        transcriptText.text = $"\u201c{text}\u2026\u201d";
        transcriptText.color = partialColor;
    }

    private void OnFinalTranscript(string text)
    {
        if (!transcriptText) return;
        transcriptText.text = $"\u201c{text}\u201d";
        transcriptText.color = finalColor;
    }

    private void OnRecordingState(bool recording)
    {
        if (recordingDot) recordingDot.SetActive(recording);
        if (recordingLabel) recordingLabel.text = recording ? "Listening\u2026" : " - ";
    }

    private void OnMoveMade(BoardMove move)
    {
        var cb = FindFirstObjectByType<Board>();
        if (!cb) return;
        PieceColor justMoved = BoardState.Opponent(cb.ActiveColor);
        string san = move.ToAlgebraic();
        if (justMoved == PieceColor.White)
        {
            if (whiteHistoryText) whiteHistoryText.text += $"\n<b>{_moveNumber}.</b> {san}";
        }
        else
        {
            if (blackHistoryText) blackHistoryText.text += $"\n{san}";
            _moveNumber++;
        }
        SetTurnLabel(cb.ActiveColor);
        checkLabel?.gameObject.SetActive(false);
    }

    private void OnMoveUndone(BoardMove _)
    {
        ResetHistory();
        var cb = FindFirstObjectByType<Board>();
        if (cb) SetTurnLabel(cb.ActiveColor);
    }

    private void OnCheck(PieceColor color)
    {
        if (!checkLabel) return;
        checkLabel.text = $"{color} is in check.";
        checkLabel.gameObject.SetActive(true);
    }

    private void OnCheckmate(PieceColor _)
    {
        checkLabel?.gameObject.SetActive(false);
        ShowGameOver("Checkmate");
    }

    private void OnStalemate()
    {
        checkLabel?.gameObject.SetActive(false);
        ShowGameOver("Stalemate — Draw!");
    }

    private void OnBlindfoldChanged(BlindfoldMode m) => SetBlindfoldLabel(m);

    private void OnSwitchColor()
        => FindFirstObjectByType<GameManager>()?.HandleSwitchColor();

    private void ResetHistory()
    {
        _moveNumber = 1;
        if (whiteHistoryText) whiteHistoryText.text = "<b>White</b>";
        if (blackHistoryText) blackHistoryText.text = "<b>Black</b>";
    }

    private void SetTurnLabel(PieceColor color)
    {
        if (!turnLabel) return;
        turnLabel.text = color == PieceColor.White ? "White to move" : "Black to move";
        turnLabel.color = color == PieceColor.White ? Color.white : new Color(0.45f, 0.45f, 0.45f);
    }

    private void SetBlindfoldLabel(BlindfoldMode mode)
    {
        if (!blindfoldLabel) return;
        blindfoldLabel.text = mode == BlindfoldMode.Normal ? "" : $"Blindfold: {mode}";
    }

    private IEnumerator FadeRoutine()
    {
        if (!feedbackGroup) yield break;
        feedbackGroup.alpha = 1f;
        yield return new WaitForSeconds(feedbackDuration);
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            feedbackGroup.alpha = Mathf.Lerp(1f, 0f, t / 0.5f);
            yield return null;
        }
        feedbackGroup.alpha = 0f;
    }
}
