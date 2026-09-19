// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core;
using Snap.Hutao.Core.ExceptionService;
using System.Diagnostics;
using System.IO;

namespace Snap.Hutao.Service.Game.Island;

internal static class HoYoShadeRuntime
{
    internal const string LibraryName = "ReShade64.dll";

    private const string RuntimeDirectoryName = "HoYoShade";
    private const string VersionFileName = "Runtime.version";
    private const string ConfigurationFileName = "ReShade.ini";
    private const string ConfigurationBuilderRelativePath = "LauncherResource\\INIBuild.exe";

    public static void Initialize()
    {
        string sourceDirectory = Path.Combine(AppContext.BaseDirectory, RuntimeDirectoryName);
        string runtimeDirectory = Path.Combine(HutaoRuntime.DataDirectory, RuntimeDirectoryName);
        EnsureRuntime(sourceDirectory, runtimeDirectory);
    }

    public static string GetRuntimeDirectory()
    {
        Initialize();
        return Path.Combine(HutaoRuntime.DataDirectory, RuntimeDirectoryName);
    }

    public static string Prepare(LaunchOptions options)
    {
        string? gamePath = options.GamePathEntry.Value?.Path;
        ArgumentException.ThrowIfNullOrEmpty(gamePath);

        string? gameDirectory = Path.GetDirectoryName(gamePath);
        ArgumentException.ThrowIfNullOrEmpty(gameDirectory);

        string runtimeDirectory = Path.Combine(HutaoRuntime.DataDirectory, RuntimeDirectoryName);
        Initialize();

        string gameConfigurationPath = Path.Combine(gameDirectory, ConfigurationFileName);
        if (!File.Exists(gameConfigurationPath))
        {
            GenerateConfiguration(runtimeDirectory);
            File.Copy(Path.Combine(runtimeDirectory, ConfigurationFileName), gameConfigurationPath);
        }

        string libraryPath = Path.Combine(runtimeDirectory, LibraryName);
        if (!File.Exists(libraryPath))
        {
            throw HutaoException.InvalidOperation($"Missing {LibraryName}: {libraryPath}");
        }

        return libraryPath;
    }

    public static void ResetConfiguration(LaunchOptions options)
    {
        string? gamePath = options.GamePathEntry.Value?.Path;
        ArgumentException.ThrowIfNullOrEmpty(gamePath);

        string? gameDirectory = Path.GetDirectoryName(gamePath);
        ArgumentException.ThrowIfNullOrEmpty(gameDirectory);

        string runtimeDirectory = GetRuntimeDirectory();
        GenerateConfiguration(runtimeDirectory);
        File.Copy(Path.Combine(runtimeDirectory, ConfigurationFileName), Path.Combine(gameDirectory, ConfigurationFileName), true);
    }

    private static void EnsureRuntime(string sourceDirectory, string runtimeDirectory)
    {
        string sourceVersionPath = Path.Combine(sourceDirectory, VersionFileName);
        if (!File.Exists(sourceVersionPath))
        {
            throw HutaoException.InvalidOperation($"Missing HoYoShade runtime version: {sourceVersionPath}");
        }

        string sourceVersion = File.ReadAllText(sourceVersionPath).Trim();
        string runtimeVersionPath = Path.Combine(runtimeDirectory, VersionFileName);
        if (File.Exists(runtimeVersionPath) && string.Equals(sourceVersion, File.ReadAllText(runtimeVersionPath).Trim(), StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(runtimeDirectory);
        CopyFile(sourceDirectory, runtimeDirectory, LibraryName);
        CopyFile(sourceDirectory, runtimeDirectory, "LICENSE");
        CopyFile(sourceDirectory, runtimeDirectory, "ReShade_LICENSE");
        CopyDirectory(sourceDirectory, runtimeDirectory, "LauncherResource");
        CopyDirectory(sourceDirectory, runtimeDirectory, "InjectResource");
        CopyDirectoryIfPresent(sourceDirectory, runtimeDirectory, "Presets");
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "Presets"));
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "reshade-shaders", "Addons"));
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "reshade-shaders", "Shaders"));
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "reshade-shaders", "Textures"));
        Directory.CreateDirectory(Path.Combine(runtimeDirectory, "ScreenShot"));
        File.WriteAllText(runtimeVersionPath, sourceVersion);
    }

    private static void GenerateConfiguration(string runtimeDirectory)
    {
        string builderPath = Path.Combine(runtimeDirectory, ConfigurationBuilderRelativePath);
        if (!File.Exists(builderPath))
        {
            throw HutaoException.InvalidOperation($"Missing HoYoShade configuration builder: {builderPath}");
        }

        using Process process = Process.Start(new ProcessStartInfo(builderPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = runtimeDirectory,
        }) ?? throw HutaoException.InvalidOperation($"Failed to start HoYoShade configuration builder: {builderPath}");

        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            process.Kill();
            throw HutaoException.InvalidOperation("HoYoShade configuration builder timed out");
        }

        if (process.ExitCode != 0)
        {
            throw HutaoException.InvalidOperation($"HoYoShade configuration builder failed with exit code {process.ExitCode}");
        }

        string configurationPath = Path.Combine(runtimeDirectory, ConfigurationFileName);
        if (!File.Exists(configurationPath))
        {
            throw HutaoException.InvalidOperation($"HoYoShade configuration builder did not create {configurationPath}");
        }
    }

    private static void CopyFile(string sourceDirectory, string runtimeDirectory, string relativePath)
    {
        string sourcePath = Path.Combine(sourceDirectory, relativePath);
        if (!File.Exists(sourcePath))
        {
            throw HutaoException.InvalidOperation($"Missing HoYoShade runtime file: {sourcePath}");
        }

        string destinationPath = Path.Combine(runtimeDirectory, relativePath);
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        File.Copy(sourcePath, destinationPath, true);
    }

    private static void CopyDirectory(string sourceDirectory, string runtimeDirectory, string relativePath)
    {
        string sourcePath = Path.Combine(sourceDirectory, relativePath);
        if (!Directory.Exists(sourcePath))
        {
            throw HutaoException.InvalidOperation($"Missing HoYoShade runtime directory: {sourcePath}");
        }

        foreach (string filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string relativeFilePath = Path.GetRelativePath(sourceDirectory, filePath);
            CopyFile(sourceDirectory, runtimeDirectory, relativeFilePath);
        }
    }

    private static void CopyDirectoryIfPresent(string sourceDirectory, string runtimeDirectory, string relativePath)
    {
        if (Directory.Exists(Path.Combine(sourceDirectory, relativePath)))
        {
            CopyDirectory(sourceDirectory, runtimeDirectory, relativePath);
        }
    }
}
