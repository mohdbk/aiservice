using System.Text.Json;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Assessments;

/// <summary>How well a completed personality questionnaire matches one role profile.</summary>
public sealed record RoleFit(int ProfileId, string NameAr, string NameEn, double Percent, string Level, IReadOnlyList<string> BelowTarget);

/// <summary>
/// A role profile designed by HR for personality questionnaires, used as one input to selection and never alone:
/// <list type="bullet">
/// <item>Big Five models: a weight per dimension (adding up to 100) and an optional target band per dimension. Fit is the
/// weighted dimension score out of 25 as a percentage, for example Project Manager = C 35% + ES 25% + E 15% + A 15% + O 10%.
/// Strong Fit from StrongFrom percent, Moderate Fit from ModerateFrom, otherwise Weak Fit.</item>
/// <item>16-type questionnaire: the types that suit the role. Strong Fit when the type is one of them, Moderate Fit when
/// three of the four letters match one of them, otherwise Weak Fit.</item>
/// </list>
/// </summary>
public class RoleProfile
{
    public int Id { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public PersonalityModel PersonalityModel { get; set; } = PersonalityModel.WorkplaceBigFive;
    /// <summary>JSON {"C": 35, "ES": 25, ...}.</summary>
    public string Weights { get; set; } = "{}";
    /// <summary>JSON {"C": "High", ...}: the lowest band wanted per dimension.</summary>
    public string Targets { get; set; } = "{}";
    /// <summary>Comma-separated 4-letter types, for the 16-type questionnaire.</summary>
    public string Types { get; set; } = "";
    public byte StrongFrom { get; set; } = 75;
    public byte ModerateFrom { get; set; } = 55;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public static bool IsBigFive(PersonalityModel m) => m is PersonalityModel.WorkplaceBigFive or PersonalityModel.BigFive;

    public Dictionary<string, int> WeightMap() => Parse<int>(Weights);
    public Dictionary<string, string> TargetMap() => Parse<string>(Targets);
    public List<string> TypeList() => Types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    static Dictionary<string, T> Parse<T>(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, T>>(string.IsNullOrWhiteSpace(json) ? "{}" : json) ?? []; }
        catch (JsonException) { throw DomainException.Invalid("role_profile_invalid"); }
    }

    public void Validate()
    {
        NameAr = NameAr.Trim();
        NameEn = NameEn.Trim();
        if (NameAr.Length == 0 || NameEn.Length == 0) throw DomainException.Invalid("name_required");
        if (IsBigFive(PersonalityModel))
        {
            var codes = Personality.Traits(PersonalityModel).Select(t => t.Code).ToHashSet();
            var weights = WeightMap().Where(w => w.Value != 0).ToDictionary(w => w.Key, w => w.Value);
            if (weights.Count == 0 || weights.Any(w => !codes.Contains(w.Key) || w.Value is < 0 or > 100) || weights.Values.Sum() != 100)
                throw DomainException.Invalid("role_weights_invalid");
            var targets = TargetMap().Where(t => !string.IsNullOrEmpty(t.Value)).ToDictionary(t => t.Key, t => t.Value);
            if (targets.Any(t => !codes.Contains(t.Key) || !Personality.SumBands.Contains(t.Value))) throw DomainException.Invalid("role_profile_invalid");
            if (StrongFrom is < 1 or > 100 || ModerateFrom < 1 || ModerateFrom >= StrongFrom) throw DomainException.Invalid("role_thresholds_invalid");
            Weights = JsonSerializer.Serialize(weights);
            Targets = JsonSerializer.Serialize(targets);
            Types = "";
        }
        else if (PersonalityModel == PersonalityModel.WorkplaceType16)
        {
            var types = TypeList().Select(t => t.ToUpperInvariant()).Distinct().ToList();
            if (types.Count == 0 || types.Any(t => !Personality.TypeCode().IsMatch(t))) throw DomainException.Invalid("role_types_invalid");
            Types = string.Join(",", types);
            Weights = "{}";
            Targets = "{}";
        }
        else throw DomainException.Invalid("personality_model_required");
    }

    /// <summary>The fit of a completed profile, or null when it is for another model or misses a dimension the role weighs.</summary>
    public RoleFit? Fit(PersonalityModel model, IReadOnlyList<TraitScore> profile, BandCutoffs? cutoffs = null)
    {
        // Big Five profiles apply to both Big Five scorings (sum 5 to 25, or the 1 to 10 scale): the dimensions are the same.
        if (model != PersonalityModel && !(IsBigFive(model) && IsBigFive(PersonalityModel))) return null;
        if (model == PersonalityModel.WorkplaceType16)
        {
            if (Personality.TypeOf(profile) is not { } result) return null;
            var best = TypeList().Select(t => t.Zip(result.Type).Count(p => p.First == p.Second)).DefaultIfEmpty(0).Max();
            return new RoleFit(Id, NameAr, NameEn, best * 25, best == 4 ? "Strong" : best == 3 ? "Moderate" : "Weak", []);
        }
        var weights = WeightMap();
        double total = 0, weight = 0;
        foreach (var (code, w) in weights)
        {
            var t = profile.FirstOrDefault(x => x.Code == code);
            if (t == null) return null;
            total += Personality.OutOf25(t) * w;
            weight += w;
        }
        if (weight == 0) return null;
        var percent = Math.Round(total / (25 * weight) * 100, 1);
        var below = TargetMap().Where(target => profile.FirstOrDefault(x => x.Code == target.Key) is { } t &&
            Array.IndexOf(Personality.SumBands, Personality.SumBand(Personality.OutOf25(t), cutoffs)) < Array.IndexOf(Personality.SumBands, target.Value))
            .Select(target => target.Key).ToList();
        return new RoleFit(Id, NameAr, NameEn, percent, percent >= StrongFrom ? "Strong" : percent >= ModerateFrom ? "Moderate" : "Weak", below);
    }
}
