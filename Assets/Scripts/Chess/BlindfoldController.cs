using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// controls piece visibility based on blindfold mode (0–4).
/// </summary>

public enum BlindfoldMode { Normal = 0, GenericPieces = 1, HideOpponent = 2, HideSelf = 3, Full = 4 }

public class BlindfoldController : MonoBehaviour
{
    public static event System.Action<BlindfoldMode> OnBlindfoldChanged;
    public BlindfoldMode CurrentMode { get; private set; } = BlindfoldMode.Normal;

    [Header("Sprites")]
    [SerializeField] private Sprite genericSprite;
    [SerializeField] private Sprite blindfoldSprite;

    private BoardVisuals _visual;
    private Board _board;

    #region Init

    private void Awake()
    {
        _visual = GetComponent<BoardVisuals>();
        _board = GetComponent<Board>();
    }

    private void OnEnable() => SpeechManager.OnCommandRecognized += HandleCommand;
    private void OnDisable() => SpeechManager.OnCommandRecognized -= HandleCommand;

    #endregion Init

    #region Commands

    private void HandleCommand(VoiceCommand cmd)
    {
        if (cmd.Type != CommandType.SetBlindfoldMode) return;
        if (int.TryParse(cmd.Payload, out int level)) SetMode((BlindfoldMode)Mathf.Clamp(level, 0, 4));
    }

    #endregion Commands

    #region Public API

    public void SetMode(BlindfoldMode mode)
    {
        CurrentMode = mode;
        if (_visual != null && _board != null) _visual.RefreshVisibility();
        OnBlindfoldChanged?.Invoke(mode);
    }

    public void ApplyVisibility(
        Dictionary<Vector2Int, GameObject> pieceObjects,
        Board board,
        Dictionary<Vector2Int, Sprite> originalSprites)
    {
        PieceColor localColor = board.LocalColor;
        foreach (var kv in pieceObjects)
        {
            var sr = kv.Value.GetComponent<SpriteRenderer>();
            if (sr == null) continue;
            var data = board.Get(kv.Key);
            bool isOwn = data.Color == localColor;
            switch (CurrentMode)
            {
                case BlindfoldMode.Normal:
                    sr.enabled = true;
                    sr.color = Color.white;
                    if (originalSprites.TryGetValue(kv.Key, out var orig)) sr.sprite = orig;
                    break;
                case BlindfoldMode.GenericPieces:
                    sr.enabled = true;
                    if (genericSprite != null) sr.sprite = genericSprite;
                    sr.color = isOwn ? Color.white : new Color(0.25f, 0.25f, 0.25f, 1f);
                    break;
                case BlindfoldMode.HideOpponent:
                    sr.enabled = isOwn;
                    if (sr.enabled) { sr.color = Color.white; RestoreSprite(sr, kv.Key, originalSprites); }
                    break;
                case BlindfoldMode.HideSelf:
                    sr.enabled = !isOwn;
                    if (sr.enabled) { sr.color = Color.white; RestoreSprite(sr, kv.Key, originalSprites); }
                    break;
                case BlindfoldMode.Full:
                    if (blindfoldSprite != null)
                    { sr.enabled = true; sr.sprite = blindfoldSprite; sr.color = new Color(1f, 1f, 1f, 0.15f); }
                    else sr.enabled = false;
                    break;
            }
        }
    }

    #endregion Public API

    private static void RestoreSprite(SpriteRenderer sr, Vector2Int sq, Dictionary<Vector2Int, Sprite> originals)
    {
        if (originals.TryGetValue(sq, out var s)) sr.sprite = s;
    }
}