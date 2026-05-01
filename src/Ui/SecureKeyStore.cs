using System;
using System.IO;
using System.Text;

namespace Morpheus.Ui;

// Stores the ElevenLabs API key in an XOR-obfuscated file so it is not
// sitting as plaintext in any config directory.  Not cryptographically
// strong, but prevents casual glancing and keeps the key out of any
// plain-text settings files that might be checked in.
public static class SecureKeyStore
{
    private static readonly byte[] Mask =
        "MORPHEUS_SECURE_KEYSTORE"u8.ToArray();

    public static string? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var b64 = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(b64)) return null;
            var bytes = Convert.FromBase64String(b64);
            Xor(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return null; }
    }

    public static void Save(string path, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        Xor(bytes);
        File.WriteAllText(path, Convert.ToBase64String(bytes));
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void Xor(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] ^= Mask[i % Mask.Length];
    }
}
