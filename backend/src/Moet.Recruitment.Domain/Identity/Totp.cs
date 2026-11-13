using System.Security.Cryptography;
using System.Text;

namespace Moet.Recruitment.Domain.Identity;

/// <summary>
/// Time-based one-time codes (RFC 6238: HMAC-SHA1, 6 digits, 30-second steps), as shown by authenticator apps such as
/// Microsoft Authenticator or Google Authenticator. A code is accepted one step early or late, and each step only once.
/// </summary>
public static class Totp
{
    const int Digits = 6;
    const int StepSeconds = 30;
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] secret, long step)
    {
        var counter = new byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xFF); step >>= 8; }
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The step the code matches (within one step of now and after the last one used), or null.</summary>
    public static long? Verify(byte[] secret, string? code, DateTime utc, long lastUsedStep)
    {
        var c = new string((code ?? "").Where(char.IsAsciiDigit).ToArray());
        if (c.Length != Digits) return null;
        var now = StepAt(utc);
        for (var s = now - 1; s <= now + 1; s++)
            if (s > lastUsedStep && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, s)), Encoding.ASCII.GetBytes(c))) return s;
        return null;
    }

    public static string Base32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string s)
    {
        var clean = s.ToUpperInvariant().Where(ch => Alphabet.Contains(ch)).ToArray();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var ch in clean)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(ch);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return bytes.ToArray();
    }

    /// <summary>The link an authenticator app reads from the QR code.</summary>
    public static string Uri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={Base32(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}
