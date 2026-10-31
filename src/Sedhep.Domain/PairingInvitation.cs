// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Sedhep.Domain;

/// <summary>
/// A short-lived invitation to send this device a cookbook, carried in a QR code. It says which
/// device is offering to receive, where it is listening, and carries a one-time token: whoever can
/// read the code has stood in front of the screen, which is the consent the invitation stands for.
/// </summary>
public sealed record PairingInvitation(
    string Host,
    int Port,
    string Token,
    string DeviceName,
    DateTimeOffset ExpiresAt)
{
    /// <summary>Every code this app writes starts here; anything else is not ours to read.</summary>
    public const string Prefix = "sedhep://pair";

    /// <summary>No invitation should outlive a glance at the screen.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    // Crockford's alphabet: no I, L, O or U, so a code read aloud cannot be misheard.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int TokenLength = 8;

    /// <summary>Guards against a device with a wild clock asking for a code that never expires.</summary>
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(1);

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>What the QR code holds. A user can copy this and paste it on the other device.</summary>
    public string Code
    {
        get
        {
            var builder = new StringBuilder(Prefix);
            builder.Append("?v=1");
            builder.Append("&h=").Append(Uri.EscapeDataString(Host));
            builder.Append("&p=").Append(Port);
            builder.Append("&t=").Append(Token);
            builder.Append("&e=").Append(ExpiresAt.ToUnixTimeSeconds());
            builder.Append("&n=").Append(Uri.EscapeDataString(DeviceName ?? string.Empty));
            return builder.ToString();
        }
    }

    public static string NewToken()
    {
        var chars = new char[TokenLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Tokens travel through screens and are sometimes typed, so casing and dashes go away.</summary>
    public static string NormaliseToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(token.Length);

        foreach (var raw in token.ToUpperInvariant())
        {
            switch (raw)
            {
                // The letters Crockford leaves out are read as the digits they look like.
                case 'I' or 'L':
                    builder.Append('1');
                    break;
                case 'O':
                    builder.Append('0');
                    break;
                case '-' or ' ' or '_':
                    break;
                default:
                    if (Alphabet.IndexOf(raw) >= 0)
                    {
                        builder.Append(raw);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a code scanned from a picture or pasted as text. A code that is malformed returns
    /// false rather than throwing: the caller is holding user input, not a promise.
    /// </summary>
    public static bool TryParse(string? text, out PairingInvitation? invitation)
    {
        invitation = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().Trim('"', '\'');

        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "sedhep", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "pair", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var values = ParseQuery(uri.Query);

        if (values.TryGetValue("v", out var version) &&
            !string.Equals(version, "1", StringComparison.Ordinal))
        {
            return false;
        }

        if (!values.TryGetValue("h", out var host) || string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (!values.TryGetValue("p", out var portText) ||
            !int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            return false;
        }

        var token = NormaliseToken(values.TryGetValue("t", out var rawToken) ? rawToken : null);
        if (token.Length < 6 || token.Length > 32)
        {
            return false;
        }

        if (!values.TryGetValue("e", out var expiryText) ||
            !long.TryParse(expiryText, out var expirySeconds) || expirySeconds <= 0)
        {
            return false;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(expirySeconds);
        if (expires - DateTimeOffset.UtcNow > MaximumLifetime)
        {
            return false;
        }

        var name = values.TryGetValue("n", out var deviceName) ? deviceName : string.Empty;

        invitation = new PairingInvitation(
            host,
            port,
            token,
            string.IsNullOrWhiteSpace(name) ? "Another phone" : name,
            expires);

        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.StartsWith('?') ? query[1..] : query;

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..split]);
            var value = Uri.UnescapeDataString(pair[(split + 1)..]);
            values[key] = value;
        }

        return values;
    }
}

/// <summary>
/// The outcome of a transfer that started from a QR code, reported to the device that showed it.
/// </summary>
public sealed record PairingReceipt(
    string SenderName,
    bool Success,
    string Message,
    int RecipeCount,
    int PhotosRestored);
