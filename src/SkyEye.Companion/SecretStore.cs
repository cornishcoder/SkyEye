using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;

namespace SkyEye.Companion;

// Personal credentials are entered on the phone, never packaged in either APK.
// No per-use biometric requirement: the gateway must work with the screen locked.
internal static class SecretStore
{
    private const string Alias = "skyeye-personal-credentials-v1";
    private static readonly object Gate = new();
    public static void Set(Context context, string name, string value)
    {
        lock (Gate)
        {
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, Key());
            var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(value))!;
            var envelope = Convert.ToBase64String(cipher.GetIV()!) + ":" + Convert.ToBase64String(encrypted);
            if (!context.GetSharedPreferences("secrets", FileCreationMode.Private)!.Edit()!.PutString(name, envelope)!.Commit())
                throw new IOException("Could not save settings.");
        }
    }

    public static string? Get(Context context, string name)
    {
        lock (Gate)
        {
            var envelope = context.GetSharedPreferences("secrets", FileCreationMode.Private)!.GetString(name, null);
            if (envelope is null) return null;
            var parts = envelope.Split(':');
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, Convert.FromBase64String(parts[0])));
            return Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!);
        }
    }

    private static IKey Key()
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.ContainsAlias(Alias)) return store.GetKey(Alias, null)!;
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetUserAuthenticationRequired(false)
            .Build();
        generator.Init(spec);
        return generator.GenerateKey()!;
    }
}
