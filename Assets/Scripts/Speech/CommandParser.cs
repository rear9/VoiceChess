using System.Collections.Generic;
using System.Text.RegularExpressions;

public enum CommandType
{
    Unknown,
    MenuPlay, MenuWhite, MenuBlack, MenuSettings, MenuModes, MenuQuit, MenuBack, MenuConfirm,
    SetBlindfoldMode,
    MoveFromTo,
    MovePieceTo,
    MovePawnTo,
    CastleKingside, CastleQueenside,
    Undo, Resign, Pause, ToggleHint,
    SwitchColor,
    Restart,
    ToggleEngine,
    TogglePushToTalk,
}

public readonly struct VoiceCommand
{
    public readonly CommandType Type;
    public readonly string Payload;
    public VoiceCommand(CommandType type, string payload = "") { Type = type; Payload = payload; }
    public static readonly VoiceCommand None = new(CommandType.Unknown);
}

/// <summary>
/// parses raw whisper transcripts into VoiceCommands.
/// </summary>

public static class VoiceCommandParser
{
    private static readonly Dictionary<string, string> WordMap = new()
    {
        { "ayy", "a" }, { "hay", "a" }, { "hey", "a" }, { "ay", "a" }, { "eight", "a" },
        { "bee", "b" }, { "be", "b" }, { "beat", "b" },
        { "sea", "c" }, { "see", "c" }, { "cee", "c" },
        { "dee", "d" }, { "dea", "d" },
        { "eat", "e" }, { "ee", "e" }, { "easy", "e" }, { "east", "e" },
        { "eff", "f" }, { "ph", "f" },
        { "gee", "g" }, { "jee", "g" }, { "ji", "g" },
        { "aitch", "h" }, { "atch", "h" },
        { "won", "1" }, { "wun", "1" }, { "one", "1" }, { "zero", "0" },
        { "too", "2" }, { "two", "2" }, { "to", "2" }, { "tu", "2" },
        { "tree", "3" }, { "three", "3" }, { "trey", "3" },
        { "for", "4" }, { "four", "4" }, { "fore", "4" }, { "foe", "4" },
        { "fife", "5" }, { "five", "5" },
        { "six", "6" }, { "sicks", "6" },
        { "seven", "7" },
        { "knight", "knight" }, { "night", "knight" }, { "nite", "knight" }, { "9", "knight" },
        { "nought", "knight" }, { "knot", "knight" }, { "not", "knight" }, { "nine", "knight" },
        { "bishop", "bishop" }, { "bischoff", "bishop" }, { "bishoff", "bishop" }, { "bischop", "bishop" },
        { "rook", "rook" }, { "brook", "rook" }, { "bruk", "rook" }, { "route", "rook" }, { "ruk", "rook" }, { "rookie", "rook" },
        { "queen", "queen" }, { "queens", "queen" }, { "queenie", "queen" },
        { "king", "king" }, { "kings", "king" },
        { "pawn", "pawn" }, { "pond", "pawn" }, { "ponds", "pawn" }, { "pon", "pawn" }, { "on", "pawn" },
        { "spawn", "pawn" }, { "porn", "pawn" }, { "pone", "pawn" },
        { "kingside", "kingside" }, { "queenside", "queenside" },
        { "undo", "undo" }, { "resign", "resign" },
        { "hint", "hint" }, { "pause", "pause" },
        { "yes", "yes" }, { "okay", "yes" }, { "ok", "yes" },
        { "quit", "quit" }, { "exit", "quit" },
    };

    private static readonly (string wrong, string right)[] PhraseMap =
    {
        ("castle king side", "castle kingside"), ("castle queen side", "castle queenside"),
        ("king side castle", "castle kingside"), ("queen side castle", "castle queenside"),
        ("short castle", "castle kingside"), ("long castle", "castle queenside"),
        ("0 0 0", "castle queenside"), ("0 0", "castle kingside"),
        ("pawnee", "pawn e"), ("pony", "pawn e"), ("ponzi", "pawn c"), 
        ("ponsy", "pawn c"), ("pondy", "pawn d"), ("pawned", "pawn d"),
        ("horny", "h 4"), ("fortnite", "f 4"),
        ("rookie e", "rook e"), ("knight to", "knight"), ("bishop to", "bishop"),
        ("rook to", "rook"), ("queen to", "queen"), ("king to", "king"), ("pawn to", "pawn"),
        ("take back", "undo"), ("take it back", "undo"), ("go back", "undo"),
        ("main menu", "menu"), ("go to menu", "menu"),
        ("new game", "start"), ("reset game", "restart"), ("reset board", "restart"),
        ("no blindfold", "normal mode"), ("full board", "normal mode"), ("hide opponent", "hide opponent"),
        ("hide my pieces", "hide self"), ("full blindfold", "full blindfold"), ("no pieces", "full blindfold"),
        ("empty board", "full blindfold"),
    };

    private static readonly (string word, char letter)[] PieceLetter =
    {
        ("knight", 'N'), ("bishop", 'B'), ("rook", 'R'),
        ("queen", 'Q'), ("king", 'K'), ("pawn", 'P'),
    };

    private const string SQ = @"([a-h][1-8])";

    #region Parse

    public static VoiceCommand Parse(string raw)
    {
        string t = Normalise(raw);
        VoiceCommand cmd;
        if ((cmd = TryColor(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryRestart(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryMenu(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryBlindfold(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryCastle(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryFromTo(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryPieceTo(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryBarePawn(t)).Type != CommandType.Unknown) return cmd;
        if ((cmd = TryControl(t)).Type != CommandType.Unknown) return cmd;
        return VoiceCommand.None;
    }

    public static string Normalise(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = Regex.Replace(raw, @"[^a-zA-Z0-9\s]", "");
        s = Regex.Replace(s.ToLowerInvariant().Trim(), @"\s+", " ");
        string[] tokens = s.Split(' ');
        for (int i = 0; i < tokens.Length; i++)
        {
            if (WordMap.TryGetValue(tokens[i], out var mapped)) tokens[i] = mapped;
        }
        s = string.Join(" ", tokens).Trim();
        s = Regex.Replace(s, @"\b([a-h])\s+([1-8])\b", "$1$2");
        foreach (var (wrong, right) in PhraseMap) s = s.Replace(wrong, right);
        s = s.Replace("please ", "").Replace("can you ", "").Trim();
        return s;
    }

    #endregion Parse

    #region Matchers

    private static VoiceCommand TryColor(string t)
    {
        if (Contains(t, "switch", "swap sides", "flip board", "switch colour", "switch color", "swap colour")) return new(CommandType.SwitchColor);
        if (Contains(t, "play black", "i want black", "play as black", "want black")) return new(CommandType.SwitchColor, "black");
        if (Contains(t, "play white", "i want white", "play as white", "want white")) return new(CommandType.SwitchColor, "white");
        return VoiceCommand.None;
    }

    private static VoiceCommand TryRestart(string t)
    {
        if (Contains(t, "restart", "reset board", "reset game")) return new(CommandType.Restart);
        return VoiceCommand.None;
    }

    private static VoiceCommand TryMenu(string t)
    {
        if ((t == "play" || t == "start" || t == "start game" || t == "play game") && !ContainsSquare(t)) return new(CommandType.MenuPlay);
        if (Contains(t, "settings", "options")) return new(CommandType.MenuSettings);
        if (Contains(t, "mode", "modes")) return new(CommandType.MenuModes);
        if (Contains(t, "white")) return new(CommandType.MenuWhite);
        if (Contains(t, "black")) return new(CommandType.MenuBlack);
        if (Contains(t, "quit", "exit")) return new(CommandType.MenuQuit);
        if (Contains(t, "back", "cancel", "close")) return new(CommandType.MenuBack);
        if (Contains(t, "yes", "confirm", "okay")) return new(CommandType.MenuConfirm);
        return VoiceCommand.None;
    }

    private static VoiceCommand TryBlindfold(string t)
    {
        var m = Regex.Match(t, @"blindfold\s*(?:level\s*)?([0-4])");
        if (m.Success) return new(CommandType.SetBlindfoldMode, m.Groups[1].Value);
        if (Contains(t, "normal mode", "no blindfold", "full board")) return new(CommandType.SetBlindfoldMode, "0");
        if (Contains(t, "generic pieces", "silhouette", "hide pieces")) return new(CommandType.SetBlindfoldMode, "1");
        if (Contains(t, "hide opponent", "opponent hidden")) return new(CommandType.SetBlindfoldMode, "2");
        if (Contains(t, "hide self", "hide my", "blind self", "hide mine")) return new(CommandType.SetBlindfoldMode, "3");
        if (Contains(t, "full blindfold", "no pieces", "empty board")) return new(CommandType.SetBlindfoldMode, "4");
        return VoiceCommand.None;
    }

    private static VoiceCommand TryCastle(string t)
    {
        if (Contains(t, "castle kingside", "short castle", "king side castle")) return new(CommandType.CastleKingside);
        if (Contains(t, "castle queenside", "long castle", "queen side castle")) return new(CommandType.CastleQueenside);
        if (t == "castle" || t == "castles") return new(CommandType.CastleKingside);
        return VoiceCommand.None;
    }

    private static VoiceCommand TryFromTo(string t)
    {
        var m = Regex.Match(t, SQ + @"\s*(?:to|takes|x|captures?)?\s*" + SQ);
        if (!m.Success) return VoiceCommand.None;
        string from = m.Groups[1].Value;
        string to = m.Groups[2].Value;
        if (from == to) return VoiceCommand.None;
        return new(CommandType.MoveFromTo, from + to);
    }

    private static VoiceCommand TryPieceTo(string t)
    {
        foreach (var (word, letter) in PieceLetter)
        {
            if (!t.Contains(word)) continue;
            var m = Regex.Match(t, word + @"\s*(?:to|takes|x|captures?)?\s*" + SQ);
            if (!m.Success) continue;
            return new(CommandType.MovePieceTo, letter + m.Groups[1].Value);
        }
        return VoiceCommand.None;
    }

    private static VoiceCommand TryBarePawn(string t)
    {
        var m = Regex.Match(t.Trim(), @"^(?:to\s+)?([a-h][1-8])$");
        if (m.Success) return new(CommandType.MovePawnTo, m.Groups[1].Value);
        return VoiceCommand.None;
    }

    private static VoiceCommand TryControl(string t)
    {
        if (Contains(t, "undo")) return new(CommandType.Undo);
        if (Contains(t, "resign", "give up")) return new(CommandType.Resign);
        if (Contains(t, "pause", "menu", "escape")) return new(CommandType.Pause);
        if (Contains(t, "hint", "suggest", "best move")) return new(CommandType.ToggleHint);
        if (Contains(t, "engine on", "enable engine", "turn on engine")) return new(CommandType.ToggleEngine, "on");
        if (Contains(t, "engine off", "disable engine", "turn off engine")) return new(CommandType.ToggleEngine, "off");
        if (Contains(t, "push to talk on", "enable push to talk", "hold to talk")) return new(CommandType.TogglePushToTalk, "on");
        if (Contains(t, "push to talk off", "disable push to talk", "continuous", "always listen")) return new(CommandType.TogglePushToTalk, "off");
        return VoiceCommand.None;
    }

    private static bool Contains(string text, params string[] terms)
    {
        foreach (var t in terms) if (text.Contains(t)) return true;
        return false;
    }

    private static bool ContainsSquare(string t) => Regex.IsMatch(t, @"[a-h][1-8]");

    #endregion Matchers
}