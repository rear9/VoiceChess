using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// menu scene manager; settings panel now responds to voice: "engine on/off", "push to talk on/off". blindfold mode can be set by voice from either panel.
/// </summary>

public class MenuManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject modeSelectPanel;
    public GameObject settingsPanel;

    [Header("Main menu buttons")]
    public Button playButton;
    public Button modesButton;
    public Button settingsButton;
    public Button quitButton;

    [Header("Close buttons")]
    public Button modeSelectCloseButton;
    public Button settingsCloseButton;

    [Header("Mode select")]
    public Button[] blindfoldButtons;
    public TextMeshProUGUI currentModeLabel;

    [Header("Settings")]
    public Toggle pushToTalkToggle;
    public Toggle stockfishToggle;
    public Slider eloSlider;
    public TextMeshProUGUI eloLabel;
    public Button playWhiteButton;
    public Button playBlackButton;
    public TextMeshProUGUI selectedColorLabel;

    [Header("Speech feedback")]
    public TextMeshProUGUI transcriptLabel;
    public CanvasGroup feedbackGroup;

    private BlindfoldMode _selectedMode = BlindfoldMode.Normal;
    private GameObject _activePanel;

    #region Init

    private void Awake()
    {
        playButton.onClick.AddListener(OnPlayClicked);
        modesButton.onClick.AddListener(OpenModeSelect);
        settingsButton.onClick.AddListener(OpenSettings);
        quitButton.onClick.AddListener(Application.Quit);
        modeSelectCloseButton?.onClick.AddListener(CloseOverlay);
        settingsCloseButton?.onClick.AddListener(CloseOverlay);
        for (int i = 0; i < blindfoldButtons.Length; i++)
        {
            int idx = i;
            blindfoldButtons[i].onClick.AddListener(() => SelectBlindfoldMode(idx));
        }
        playWhiteButton?.onClick.AddListener(() => SelectColor(PieceColor.White));
        playBlackButton?.onClick.AddListener(() => SelectColor(PieceColor.Black));
        if (eloSlider != null)
        {
            eloSlider.minValue = 0; eloSlider.maxValue = 10; eloSlider.wholeNumbers = true;
            eloSlider.onValueChanged.AddListener(OnEloSliderChanged);
        }
        pushToTalkToggle?.onValueChanged.AddListener(OnPushToTalkChanged);
        stockfishToggle?.onValueChanged.AddListener(OnStockfishToggleChanged);
        _selectedMode = (BlindfoldMode)PlayerPrefs.GetInt("BlindfoldMode", 0);
    }

    private void OnEnable()
    {
        SpeechManager.OnCommandRecognized += HandleCommand;
        SpeechManager.OnTranscriptReady += ShowTranscript;
    }

    private void OnDisable()
    {
        SpeechManager.OnCommandRecognized -= HandleCommand;
        SpeechManager.OnTranscriptReady -= ShowTranscript;
    }

    private void Start()
    {
        mainMenuPanel.SetActive(true);
        modeSelectPanel.SetActive(false);
        settingsPanel.SetActive(false);
        _activePanel = mainMenuPanel;
        RefreshModeLabel();
    }

    #endregion Init

    #region Panel navigation

    private void OpenModeSelect()
    {
        modeSelectPanel.SetActive(true);
        settingsPanel.SetActive(false);
        _activePanel = modeSelectPanel;
        _selectedMode = (BlindfoldMode)PlayerPrefs.GetInt("BlindfoldMode", 0);
        RefreshModeLabel();
        RefreshBlindfoldHighlights();
    }

    private void OpenSettings()
    {
        settingsPanel.SetActive(true);
        modeSelectPanel.SetActive(false);
        _activePanel = settingsPanel;
        RefreshSettingsUI();
    }

    private void CloseOverlay()
    {
        modeSelectPanel.SetActive(false);
        settingsPanel.SetActive(false);
        _activePanel = mainMenuPanel;
    }

    #endregion Panel navigation

    #region Voice commands

    private void HandleCommand(VoiceCommand cmd)
    {
        switch (cmd.Type)
        {
            case CommandType.MenuPlay: OnPlayClicked(); break;
            case CommandType.MenuSettings: OpenSettings(); break;
            case CommandType.MenuBack:
            case CommandType.Pause: CloseOverlay(); break;
            case CommandType.MenuConfirm: OnPlayClicked(); break;
            case CommandType.MenuQuit: Application.Quit(); break;
            case CommandType.Restart: OnPlayClicked(); break;
            case CommandType.SetBlindfoldMode:
                if (int.TryParse(cmd.Payload, out int lvl))
                {
                    SelectBlindfoldMode(lvl);
                    OpenModeSelect();
                }
                break;
            case CommandType.ToggleEngine:
                bool engineOn = cmd.Payload == "on" || (cmd.Payload != "off" && PlayerPrefs.GetInt("StockfishEnabled", 0) == 0);
                PlayerPrefs.SetInt("StockfishEnabled", engineOn ? 1 : 0);
                PlayerPrefs.Save();
                if (_activePanel == settingsPanel) RefreshSettingsUI();
                ShowTranscript($"Engine {(engineOn ? "on" : "off")}");
                break;
            case CommandType.TogglePushToTalk:
                bool pttOn = cmd.Payload == "on" || (cmd.Payload != "off" && PlayerPrefs.GetInt("PushToTalk", 1) == 0);
                PlayerPrefs.SetInt("PushToTalk", pttOn ? 1 : 0);
                PlayerPrefs.Save();
                if (SpeechManager.Instance != null) SpeechManager.Instance.pushToTalk = pttOn;
                if (_activePanel == settingsPanel) RefreshSettingsUI();
                ShowTranscript($"Push-to-talk {(pttOn ? "on" : "off")}");
                break;
        }
    }

    #endregion Voice commands

    #region Blindfold

    private void SelectBlindfoldMode(int level)
    {
        _selectedMode = (BlindfoldMode)Mathf.Clamp(level, 0, 4);
        PlayerPrefs.SetInt("BlindfoldMode", (int)_selectedMode);
        PlayerPrefs.Save();
        RefreshModeLabel();
        RefreshBlindfoldHighlights();
    }

    private void RefreshModeLabel()
    {
        if (!currentModeLabel) return;
        string[] labels = { "Normal","Generic Pieces","Hide Opponent","Hide Self","Full Blindfold" };
        int idx = (int)_selectedMode;
        currentModeLabel.text = idx < labels.Length ? $"Mode: {labels[idx]}" : "";
    }

    private void RefreshBlindfoldHighlights()
    {
        for (int i = 0; i < blindfoldButtons.Length; i++)
        {
            var c = blindfoldButtons[i].colors;
            c.normalColor = i == (int)_selectedMode ? new Color(0.2f, 0.6f, 1f) : Color.white;
            blindfoldButtons[i].colors = c;
        }
    }

    #endregion Blindfold

    #region Settings UI

    private void RefreshSettingsUI()
    {
        pushToTalkToggle?.onValueChanged.RemoveListener(OnPushToTalkChanged);
        stockfishToggle?.onValueChanged.RemoveListener(OnStockfishToggleChanged);
        eloSlider?.onValueChanged.RemoveListener(OnEloSliderChanged);
        if (pushToTalkToggle) pushToTalkToggle.isOn = PlayerPrefs.GetInt("PushToTalk", 1) == 1;
        if (stockfishToggle) stockfishToggle.isOn = PlayerPrefs.GetInt("StockfishEnabled", 0) == 1;
        int eloIdx = PlayerPrefs.GetInt("EloPresetIndex", 5);
        if (eloSlider) eloSlider.value = eloIdx;
        RefreshEloLabel(eloIdx);
        int colorPref = PlayerPrefs.GetInt("PlayerColor", 0);
        RefreshColorLabel(colorPref == 1 ? PieceColor.Black : PieceColor.White);
        pushToTalkToggle?.onValueChanged.AddListener(OnPushToTalkChanged);
        stockfishToggle?.onValueChanged.AddListener(OnStockfishToggleChanged);
        eloSlider?.onValueChanged.AddListener(OnEloSliderChanged);
    }

    public void OnPushToTalkChanged(bool value)
    {
        PlayerPrefs.SetInt("PushToTalk", value ? 1 : 0);
        PlayerPrefs.Save();
        if (SpeechManager.Instance != null) SpeechManager.Instance.pushToTalk = value;
    }

    public void OnStockfishToggleChanged(bool value)
    {
        PlayerPrefs.SetInt("StockfishEnabled", value ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void OnEloSliderChanged(float value)
    {
        int idx = Mathf.RoundToInt(value);
        PlayerPrefs.SetInt("EloPresetIndex", idx);
        PlayerPrefs.Save();
        RefreshEloLabel(idx);
    }

    private void RefreshEloLabel(int idx)
    {
        if (!eloLabel) return;
        var p = EloTable.FromIndex(idx);
        eloLabel.text = $"{p.Label}  (~{p.ApproxElo} ELO)";
    }

    private void SelectColor(PieceColor color)
    {
        PlayerPrefs.SetInt("PlayerColor", color == PieceColor.Black ? 1 : 0);
        PlayerPrefs.Save();
        RefreshColorLabel(color);
    }

    private void RefreshColorLabel(PieceColor color)
    {
        if (!selectedColorLabel) return;
        selectedColorLabel.text = $"Playing as: {color}";
    }

    #endregion Settings UI

    #region Play

    private void OnPlayClicked()
    {
        PlayerPrefs.SetInt("BlindfoldMode", (int)_selectedMode);
        PlayerPrefs.Save();
        SceneManager.LoadScene("Play");
    }

    #endregion Play

    #region Transcript display

    private void ShowTranscript(string text)
    {
        if (!transcriptLabel) return;
        transcriptLabel.text = text;
        StopAllCoroutines();
        StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        if (!feedbackGroup) yield break;
        feedbackGroup.alpha = 1f;
        yield return new WaitForSeconds(2f);
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            feedbackGroup.alpha = 1f - t;
            yield return null;
        }
        feedbackGroup.alpha = 0f;
    }

    #endregion Transcript display
}