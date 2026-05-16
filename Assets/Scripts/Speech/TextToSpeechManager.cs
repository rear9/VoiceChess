using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads chess moves aloud using platform-native TTS.
/// Windows: launches PowerShell with SAPI SpVoice (full COM support).
/// Android: Android TTS Java API.
/// Attach to a persistent GameObject, subscribes to Board.OnMoveMade.
/// </summary>

public class TextToSpeechManager : MonoBehaviour
{
    public static TextToSpeechManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    [Header("Settings")]
    [Tooltip("Speak opponent moves automatically.")]
    public bool speakOpponentMoves = true;

    [Tooltip("Speak your own moves as confirmation.")]
    public bool speakOwnMoves = false;

    [Range(0.5f, 2f)]
    public float rate = 1f;

    [Range(0f, 1f)]
    public float volume = 1f;

    private Queue<string> _queue = new();
    private bool _speaking;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject _tts;
    private bool _ttsReady;
#endif

    private void Start()
    {
        Board.OnMoveMade += OnMoveMade;
        InitPlatform();
    }

    private void OnDestroy()
    {
        Board.OnMoveMade -= OnMoveMade;
        ShutdownPlatform();
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _queue.Enqueue(text);
        if (!_speaking) StartCoroutine(DrainQueue());
    }

    public void StopSpeaking()
    {
        _queue.Clear();
        StopCoroutine(nameof(DrainQueue));
        _speaking = false;
        StopPlatform();
    }

    private void OnMoveMade(BoardMove move)
    {
        var cb = FindFirstObjectByType<Board>();
        if (!cb) return;

        PieceColor justMoved = BoardState.Opponent(cb.ActiveColor);
        bool isOpponent = justMoved != cb.LocalColor;

        if (isOpponent && speakOpponentMoves)
            Speak(MoveToSpeech(move));
        else if (!isOpponent && speakOwnMoves)
            Speak(MoveToSpeech(move));
    }

    private static string MoveToSpeech(BoardMove move)
    {
        if (move.IsCastling)
            return move.To.x == 6 ? "Castle kingside" : "Castle queenside";

        string piece = move.MovedPiece switch
        {
            PieceType.Knight => "Knight",
            PieceType.Bishop => "Bishop",
            PieceType.Rook => "Rook",
            PieceType.Queen => "Queen",
            PieceType.King => "King",
            _ => "Pawn",
        };

        string to = $"{(char)('A' + move.To.x)}{move.To.y + 1}";
        string cap = move.IsCapture ? " takes" : " to";
        string promo = move.Promotion != PieceType.None
            ? $", promotes to {move.Promotion}" : "";

        return $"{piece}{cap} {to}{promo}";
    }

    private IEnumerator DrainQueue()
    {
        _speaking = true;
        while (_queue.Count > 0)
        {
            string text = _queue.Dequeue();
            yield return StartCoroutine(SpeakPlatform(text));
            yield return new WaitForSeconds(0.1f);
        }
        _speaking = false;
    }

    private void InitPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(InitAndroid());
#endif
    }

    private void ShutdownPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        _tts?.Call("shutdown");
        _tts?.Dispose();
        _tts = null;
#endif
    }

    private void StopPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        _tts?.Call<int>("stop");
#endif
    }

    private IEnumerator SpeakPlatform(string text)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string escaped = text.Replace("'", "''");
        int r = Mathf.RoundToInt(Mathf.Lerp(-10, 10, rate / 2f));
        int v = Mathf.RoundToInt(volume * 100f);
        string script = "$sp=(New-Object -ComObject SAPI.SpVoice);"
            + "$sp.Rate=" + r + ";"
            + "$sp.Volume=" + v + ";"
            + "$sp.Speak('" + escaped + "')";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"" + script + "\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) { Debug.LogWarning("[TTS] PowerShell launch failed"); yield break; }
        float timeout = text.Length * 0.1f + 5f;
        float elapsed = 0f;
        while (!proc.HasExited && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (!proc.HasExited) { proc.Kill(); Debug.LogWarning("[TTS] timeout"); }
#elif UNITY_ANDROID && !UNITY_EDITOR
        if (!_ttsReady || _tts == null) yield break;
        _tts.Call<int>("speak", text, 1, null, "tts_utt");
        yield return new WaitForSeconds(text.Length * 0.07f + 0.5f);
#else
        Debug.Log("[TTS] " + text);
        yield return null;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator InitAndroid()
    {
        var ctx = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity");
        _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", ctx,
            new TtsInitListener(success => { _ttsReady = success; }));

        float t = 0f;
        while (!_ttsReady && t < 10f) { t += Time.deltaTime; yield return null; }

        if (_ttsReady)
        {
            var locale = new AndroidJavaClass("java.util.Locale")
                .GetStatic<AndroidJavaObject>("ENGLISH");
            _tts.Call<int>("setLanguage", locale);
            _tts.Call<int>("setSpeechRate", rate);
        }
    }

    private class TtsInitListener : AndroidJavaProxy
    {
        private readonly System.Action<bool> _callback;
        public TtsInitListener(System.Action<bool> cb)
            : base("android.speech.tts.TextToSpeech$OnInitListener") { _callback = cb; }
        public void onInit(int status) => _callback?.Invoke(status == 0);
    }
#endif
}
