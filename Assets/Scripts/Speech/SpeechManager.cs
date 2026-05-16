using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using Whisper;
using Whisper.Utils;

/// <summary>
/// singleton speech manager, persists across scenes; handles mic recording, Whisper inference, and command dispatch.
/// </summary>

public class SpeechManager : MonoBehaviour
{
    public static SpeechManager Instance { get; private set; }

    [Header("Whisper")]
    public WhisperManager whisper;

    [Header("Microphone")]
    public string microphoneDevice = "";
    public float maxRecordSeconds = 8f;
    public float silenceThreshold = 0.02f;
    public float silenceDuration = 1.0f;

    [Header("Mode")]
    public bool pushToTalk = true;
    public KeyCode pushToTalkKey = KeyCode.Space;

    [Header("Watchdog")]
    [Tooltip("Seconds of busy inference with no progress before force-reset.")]
    public float inferenceWatchdog = 35f;

    public static event Action<string> OnPartialTranscript;
    public static event Action<string> OnTranscriptReady;
    public static event Action<VoiceCommand> OnCommandRecognized;
    public static event Action<bool> OnRecordingStateChanged;

    private AudioClip _clip;
    private bool _isRecording;
    private bool _whisperBusy;
    private bool _modelReady;
    private bool _waitingForModel;
    private bool _rearmPending;
    private bool _hasFocus = true;
    private float _silenceTimer;
    private float _busyTimer;
    private Coroutine _silenceCoroutine;

    private static readonly string[] BlankPatterns =
    {
        "[blank_audio]","[silence]","(silence)","[ silence ]",
        "[inaudible]","(inaudible)","[ blank audio ]",
        "[ BLANK_AUDIO ]","[BLANK_AUDIO]","[ Inaudible ]",
        "[ Blank_Audio ]","[blank audio]",
    };

    #region Init

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubWhisper();
    }

    private void OnEnable()
    {
        if (whisper == null) whisper = FindFirstObjectByType<WhisperManager>();
        if (whisper == null) { Debug.LogError("[SpeechManager] WhisperManager missing!"); return; }
        SubWhisper();
        if (!_waitingForModel) StartCoroutine(WaitForModelThenStart());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _silenceCoroutine = null;
        _rearmPending = false;
        _waitingForModel = false;
        _busyTimer = 0f;
        if (_isRecording) { Microphone.End(microphoneDevice); _isRecording = false; OnRecordingStateChanged?.Invoke(false); }
        if (_clip != null) { Destroy(_clip); _clip = null; }
        UnsubWhisper();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var found = FindFirstObjectByType<WhisperManager>();
        if (found != null && found != whisper) { UnsubWhisper(); whisper = found; }
        if (whisper == null) { Debug.LogError("[SpeechManager] no WhisperManager in scene!"); return; }
        SubWhisper();
        _modelReady = whisper.IsLoaded;
        if (!_modelReady && !_waitingForModel) StartCoroutine(WaitForModelThenStart());
        else if (_modelReady && !pushToTalk && !_isRecording && !_whisperBusy && _hasFocus) ScheduleRearm(0.5f);
    }

    private void SubWhisper()
    {
        if (whisper == null) return;
        whisper.OnNewSegment -= OnPartialSegment;
        whisper.OnNewSegment += OnPartialSegment;
    }

    private void UnsubWhisper()
    {
        if (whisper != null) whisper.OnNewSegment -= OnPartialSegment;
    }

    private IEnumerator WaitForModelThenStart()
    {
        _waitingForModel = true;
        float elapsed = 0f;
        while (whisper != null && !whisper.IsLoaded)
        {
            elapsed += Time.deltaTime;
            if (elapsed > 120f) { Debug.LogError("[SpeechManager] model load timeout."); _waitingForModel = false; yield break; }
            yield return null;
        }
        _waitingForModel = false;
        _modelReady = true;
        Debug.Log("[SpeechManager] model ready ✓");
        if (!pushToTalk && _hasFocus) StartRecording();
    }

    #endregion Init

    #region Focus

    private void OnApplicationFocus(bool hasFocus)
    {
        _hasFocus = hasFocus;
        if (!hasFocus)
        {
            if (_isRecording)
            {
                if (_silenceCoroutine != null) { StopCoroutine(_silenceCoroutine); _silenceCoroutine = null; }
                Microphone.End(microphoneDevice);
                _isRecording = false;
                OnRecordingStateChanged?.Invoke(false);
                if (_clip != null) { Destroy(_clip); _clip = null; }
            }
        }
        else if (_modelReady && !pushToTalk && !_isRecording && !_whisperBusy) ScheduleRearm(0.3f);
    }

    #endregion Focus

    #region Update & Watchdog

    private void Update()
    {
        if (pushToTalk && _modelReady)
        {
            if (Input.GetKeyDown(pushToTalkKey) && !_isRecording && !_whisperBusy && _hasFocus) StartRecording();
            if (Input.GetKeyUp(pushToTalkKey) && _isRecording) StopAndProcess();
        }
        if (_whisperBusy)
        {
            _busyTimer += Time.deltaTime;
            if (_busyTimer > inferenceWatchdog)
            {
                Debug.LogWarning("[SpeechManager] watchdog triggered — force-resetting.");
                _whisperBusy = false;
                _busyTimer = 0f;
                if (_clip != null) { Destroy(_clip); _clip = null; }
                ScheduleRearm(0.5f);
            }
        }
        else _busyTimer = 0f;
    }

    #endregion Update & Watchdog

    #region Recording

    public void StartRecording()
    {
        if (_isRecording || _whisperBusy || !_modelReady || !_hasFocus) return;
        _isRecording = true;
        _silenceTimer = 0f;
        _clip = Microphone.Start(microphoneDevice, false, (int)maxRecordSeconds, 16000);
        OnRecordingStateChanged?.Invoke(true);
        if (!pushToTalk)
        {
            if (_silenceCoroutine != null) { StopCoroutine(_silenceCoroutine); _silenceCoroutine = null; }
            _silenceCoroutine = StartCoroutine(SilenceWatcher());
        }
    }

    public void StopAndProcess()
    {
        if (!_isRecording) return;
        _isRecording = false;
        if (_silenceCoroutine != null) { StopCoroutine(_silenceCoroutine); _silenceCoroutine = null; }
        OnRecordingStateChanged?.Invoke(false);
        int pos = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);
        if (pos < 800 || _clip == null) { ScheduleRearm(); return; }
        float[] data = new float[pos * _clip.channels];
        _clip.GetData(data, 0);
        AudioClip trimmed = AudioClip.Create("speech", pos, _clip.channels, _clip.frequency, false);
        trimmed.SetData(data, 0);
        Destroy(_clip);
        _clip = null;
        StartCoroutine(RunWhisper(trimmed));
    }

    private IEnumerator SilenceWatcher()
    {
        while (_isRecording)
        {
            yield return new WaitForSeconds(0.1f);
            if (_clip == null) break;
            int pos = Microphone.GetPosition(microphoneDevice);
            if (pos <= 0) continue;
            int n = Mathf.Min(512, pos);
            float[] buf = new float[n];
            _clip.GetData(buf, Mathf.Max(0, pos - n));
            float rms = 0f;
            foreach (float s in buf) rms += s * s;
            rms = Mathf.Sqrt(rms / n);
            _silenceTimer = rms < silenceThreshold ? _silenceTimer + 0.1f : 0f;
            if (_silenceTimer >= silenceDuration) { _silenceCoroutine = null; StopAndProcess(); yield break; }
        }
        _silenceCoroutine = null;
    }

    #endregion Recording

    #region Inference

    private void OnPartialSegment(WhisperSegment segment)
    {
        string raw = segment?.Text?.Trim() ?? "";
        if (!IsBlank(raw)) OnPartialTranscript?.Invoke(raw);
    }

    private IEnumerator RunWhisper(AudioClip clip)
    {
        _whisperBusy = true;
        _busyTimer = 0f;
        if (whisper == null || !whisper.IsLoaded) { Cleanup(clip); ScheduleRearm(); yield break; }
        var task = whisper.GetTextAsync(clip);
        while (!task.IsCompleted) yield return null;
        Cleanup(clip);
        if (task.IsFaulted)
        {
            Debug.LogError($"[SpeechManager] {task.Exception?.GetBaseException().Message}");
            ScheduleRearm(); yield break;
        }
        string raw = task.Result?.Result?.Trim() ?? "";
       if (IsBlank(raw)) { ScheduleRearm(); yield break; }
        string normalised = VoiceCommandParser.Normalise(raw);
        OnTranscriptReady?.Invoke(normalised);
        var cmd = VoiceCommandParser.Parse(raw);
        if (cmd.Type != CommandType.Unknown) OnCommandRecognized?.Invoke(cmd);
        ScheduleRearm();
    }

    private void Cleanup(AudioClip clip)
    {
        _whisperBusy = false;
        _busyTimer = 0f;
        if (clip) Destroy(clip);
    }

    #endregion Inference

    #region Rearm

    private void ScheduleRearm(float delay = 0.15f)
    {
        if (pushToTalk || !_modelReady || !_hasFocus || _rearmPending) return;
        _rearmPending = true;
        StartCoroutine(DelayedRearm(delay));
    }

    private IEnumerator DelayedRearm(float delay)
    {
        yield return new WaitForSeconds(delay);
        _rearmPending = false;
        if (!_isRecording && !_whisperBusy && _hasFocus) StartRecording();
    }

    #endregion Rearm

    #region Helpers

    private static bool IsBlank(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return true;
        if (Regex.Replace(t, @"[^a-zA-Z0-9]", "").Length <= 1) return true;
        string lower = t.ToLowerInvariant();
        foreach (var p in BlankPatterns) if (lower.Contains(p.ToLowerInvariant())) return true;
        return false;
    }

    private static readonly Regex Regex = new(@"[^a-zA-Z0-9]");

    public void InjectCommand(VoiceCommand cmd) => OnCommandRecognized?.Invoke(cmd);

    #endregion Helpers
}