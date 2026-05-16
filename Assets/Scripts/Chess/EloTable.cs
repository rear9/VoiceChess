/// <summary>
/// maps Stockfish skill level (0–20) and depth to approximate ELO ratings.
/// </summary>

public static class EloTable
{
    public static readonly EloPreset[] Presets =
    {
        new("Beginner", 0, 1, 800),
        new("Casual", 2, 2, 1000),
        new("Club Player", 4, 4, 1200),
        new("Intermediate", 6, 5, 1400),
        new("Advanced", 8, 7, 1600),
        new("Strong Club", 10, 9, 1800),
        new("Expert", 12, 11, 2000),
        new("Candidate Master", 14, 13, 2200),
        new("Master", 16, 15, 2400),
        new("International M.", 18, 18, 2600),
        new("Grandmaster", 20, 22, 2850),
    };

    public static EloPreset FromElo(int elo)
    {
        var best = Presets[0];
        int bestDiff = int.MaxValue;
        foreach (var p in Presets)
        {
            int diff = System.Math.Abs(p.ApproxElo - elo);
            if (diff < bestDiff) { bestDiff = diff; best = p; }
        }
        return best;
    }

    public static EloPreset FromIndex(int index)
    {
        index = UnityEngine.Mathf.Clamp(index, 0, Presets.Length - 1);
        return Presets[index];
    }

    public static int SliderToIndex(float sliderValue) =>
        UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Lerp(0, Presets.Length - 1, sliderValue / 10f));
}

public readonly struct EloPreset
{
    public readonly string Label;
    public readonly int SkillLevel;
    public readonly int Depth;
    public readonly int ApproxElo;

    public EloPreset(string label, int skill, int depth, int elo)
    {
        Label = label;
        SkillLevel = skill;
        Depth = depth;
        ApproxElo = elo;
    }

    public override string ToString() => $"{Label}  (~{ApproxElo} ELO)";
}