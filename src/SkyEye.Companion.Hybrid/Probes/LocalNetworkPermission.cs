namespace SkyEye.Companion.Hybrid.Probes;

/// <summary>Android 17 requires a runtime grant for apps targeting API 37.</summary>
public sealed class LocalNetworkPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(37) ? [("android.permission.ACCESS_LOCAL_NETWORK", true)] : [];
}
