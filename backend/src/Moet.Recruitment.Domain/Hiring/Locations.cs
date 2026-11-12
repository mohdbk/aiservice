namespace Moet.Recruitment.Domain.Hiring;

/// <summary>
/// The seven emirates in Arabic and English. A job's location is stored as typed; when it names an emirate (in either
/// language, with common spellings) it is shown in the reader's language, otherwise as typed.
/// </summary>
public static class Locations
{
    public sealed record Emirate(string Key, string Ar, string En, string[] Other);

    public static readonly IReadOnlyList<Emirate> Emirates =
    [
        new("AbuDhabi", "أبوظبي", "Abu Dhabi", ["ابوظبي", "أبو ظبي", "ابو ظبي", "abudhabi", "abu-dhabi"]),
        new("Dubai", "دبي", "Dubai", []),
        new("Sharjah", "الشارقة", "Sharjah", ["الشارقه", "شارقة"]),
        new("Ajman", "عجمان", "Ajman", []),
        new("UmmAlQuwain", "أم القيوين", "Umm Al Quwain", ["ام القيوين", "umm al-quwain", "umm al qaiwain"]),
        new("RasAlKhaimah", "رأس الخيمة", "Ras Al Khaimah", ["راس الخيمة", "ras al-khaimah", "rak"]),
        new("Fujairah", "الفجيرة", "Fujairah", ["الفجيره", "al fujairah"]),
    ];

    public static Emirate? Find(string? location)
    {
        var l = (location ?? "").Trim();
        if (l.Length == 0) return null;
        return Emirates.FirstOrDefault(e => e.Key.Equals(l, StringComparison.OrdinalIgnoreCase) || e.Ar == l
            || e.En.Equals(l, StringComparison.OrdinalIgnoreCase) || e.Other.Any(o => o.Equals(l, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The location in a language: the emirate's name when it is one, otherwise as typed.</summary>
    public static string In(string? location, string lang) =>
        Find(location) is { } e ? (lang == "ar" ? e.Ar : e.En) : (location ?? "").Trim();
}
