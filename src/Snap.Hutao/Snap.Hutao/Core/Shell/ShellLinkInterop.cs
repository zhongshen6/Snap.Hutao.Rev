// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.IO;
using Snap.Hutao.Core.ApplicationModel;
using System.IO;

namespace Snap.Hutao.Core.Shell;

[Service(ServiceLifetime.Transient, typeof(IShellLinkInterop))]
internal sealed class ShellLinkInterop : IShellLinkInterop
{
    private static readonly string PowerShellPath = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public bool TryCreateDesktopShortcutForElevatedLaunch()
    {
        string targetLogoPath = HutaoRuntime.GetDataDirectoryFile("ShellLinkLogo.ico");

        try
        {
            InstalledLocation.CopyFileFromApplicationUri("ms-appx:///Assets/Logo.ico", targetLogoPath);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string target = Path.Combine(desktop, $"{SH.FormatAppNameAndVersion(HutaoRuntime.Version)}.lnk");
            string launchTarget = PackageIdentityAdapter.HasPackageIdentity
                ? $"shell:AppsFolder\\{HutaoRuntime.FamilyName}!App"
                : InstalledLocation.GetAbsolutePath("Snap.Hutao.exe");
            string escapedLaunchTarget = launchTarget.Replace("'", "''", StringComparison.Ordinal);
            string arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Start-Process '{escapedLaunchTarget}' -Verb RunAs\"";

            FileSystem.CreateLink(PowerShellPath, arguments, targetLogoPath, target);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
