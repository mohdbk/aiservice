using System.Globalization;
using System.Text;

namespace Moet.Hrms.Application.Common;

/// <summary>
/// A calendar invitation (iCalendar, RFC 5545) for an interview, which Outlook, Google Calendar and Apple Calendar all
/// open: attached to the "Interview Scheduled" email and downloadable from the backoffice.
/// </summary>
public static class CalendarInvite
{
    public static MailAttachment Invite(string uid, string title, DateTime startsUtc, DateTime endsUtc, string? location, string? description,
        string organizer = "Ministry of Economy & Tourism", DateTime? stampUtc = null) =>
        new("interview.ics", "text/calendar; charset=utf-8; method=PUBLISH", Encoding.UTF8.GetBytes(Ics(uid, title, startsUtc, endsUtc, location, description, organizer, stampUtc)));

    public static string Ics(string uid, string title, DateTime startsUtc, DateTime endsUtc, string? location, string? description,
        string organizer = "Ministry of Economy & Tourism", DateTime? stampUtc = null)
    {
        static string When(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var b = new StringBuilder();
        void Line(string s)
        {
            // Lines longer than 75 octets fold onto continuation lines that start with a space.
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length <= 75) { b.Append(s).Append("\r\n"); return; }
            var part = new StringBuilder();
            var size = 0;
            var first = true;
            foreach (var r in s.EnumerateRunes())
            {
                var n = r.Utf8SequenceLength;
                if (size + n > (first ? 75 : 74)) { b.Append(first ? "" : " ").Append(part).Append("\r\n"); part.Clear(); size = 0; first = false; }
                part.Append(r.ToString());
                size += n;
            }
            b.Append(first ? "" : " ").Append(part).Append("\r\n");
        }
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Ministry of Economy & Tourism//HRMS//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:{Escape(uid)}");
        Line($"DTSTAMP:{When(stampUtc ?? DateTime.UtcNow)}");
        Line($"DTSTART:{When(startsUtc)}");
        Line($"DTEND:{When(endsUtc)}");
        Line($"SUMMARY:{Escape(title)}");
        if (!string.IsNullOrWhiteSpace(location)) Line($"LOCATION:{Escape(location)}");
        if (!string.IsNullOrWhiteSpace(description)) Line($"DESCRIPTION:{Escape(description)}");
        Line($"X-WR-CALNAME:{Escape(organizer)}");
        Line("BEGIN:VALARM");
        Line("TRIGGER:-PT30M");
        Line("ACTION:DISPLAY");
        Line($"DESCRIPTION:{Escape(title)}");
        Line("END:VALARM");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return b.ToString();
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
}
