// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using System.Globalization;
using System.IO;

namespace Snap.Hutao.Service.Game.Island;

internal static class HoYoShadeResourceFileOperations
{
    public static string GetPackageRelativePath(string configuredPath, string expectedDirectory)
    {
        string path = configuredPath.StartsWith("./", StringComparison.Ordinal) || configuredPath.StartsWith(".\\", StringComparison.Ordinal)
            ? configuredPath[2..]
            : configuredPath;
        if (!TryGetArchiveRelativePath(path, out string? relativePath))
        {
            throw new InvalidDataException("Invalid HoYoShade package destination");
        }

        string[] segments = relativePath.Split(Path.DirectorySeparatorChar);
        if (segments.Length < 2
            || !string.Equals(segments[0], "reshade-shaders", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], expectedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Invalid HoYoShade package destination");
        }

        return relativePath;
    }

    public static void AddDirectoryFiles(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlySet<string> deniedEffectFiles,
        IDictionary<string, string> stagedFiles,
        IReadOnlySet<string>? allowedFileExtensions = default)
    {
        ArgumentNullException.ThrowIfNull(deniedEffectFiles);
        ArgumentNullException.ThrowIfNull(stagedFiles);

        foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            if (IsGitMetadataPath(relativePath)
                || (allowedFileExtensions is not null && !allowedFileExtensions.Contains(Path.GetExtension(sourcePath)))
                || (string.Equals(Path.GetExtension(sourcePath), ".fx", StringComparison.OrdinalIgnoreCase) && deniedEffectFiles.Contains(Path.GetFileName(sourcePath))))
            {
                continue;
            }

            // A later catalog package intentionally replaces a prior package at a shared destination.
            stagedFiles[CombineUnderRoot(destinationDirectory, relativePath)] = sourcePath;
        }
    }

    public static void CommitStagedFiles(IReadOnlyDictionary<string, string> stagedFiles, string transactionDirectory)
    {
        ArgumentNullException.ThrowIfNull(stagedFiles);
        string backupDirectory = Path.Combine(transactionDirectory, "backup");
        List<(string Destination, string Backup)> replacedFiles = [];
        List<string> createdFiles = [];
        string? copyInProgressDestination = default;

        try
        {
            int backupIndex = 0;
            foreach ((string destinationPath, string sourcePath) in stagedFiles.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
                EnsureNoReparsePointAncestors(destinationPath);
                Directory.CreateDirectory(destinationDirectory);

                if (File.Exists(destinationPath))
                {
                    Directory.CreateDirectory(backupDirectory);
                    string backupPath = Path.Combine(backupDirectory, backupIndex++.ToString(CultureInfo.InvariantCulture));
                    File.AppendAllText(Path.Combine(backupDirectory, "destinations.txt"), $"{Path.GetFileName(backupPath)}\t{destinationPath}{Environment.NewLine}");
                    File.Move(destinationPath, backupPath);
                    replacedFiles.Add((destinationPath, backupPath));
                }

                copyInProgressDestination = destinationPath;
                File.Copy(sourcePath, destinationPath, false);
                createdFiles.Add(destinationPath);
                copyInProgressDestination = default;
            }
        }
        catch (Exception commitException)
        {
            List<Exception> rollbackExceptions = [];
            if (copyInProgressDestination is not null)
            {
                TryRecover(() => DeleteFileIfPresent(copyInProgressDestination), rollbackExceptions);
            }

            foreach (string destinationPath in createdFiles.AsEnumerable().Reverse())
            {
                TryRecover(() => DeleteFileIfPresent(destinationPath), rollbackExceptions);
            }

            foreach ((string destinationPath, string backupPath) in replacedFiles.AsEnumerable().Reverse())
            {
                TryRecover(() =>
                {
                    if (File.Exists(backupPath))
                    {
                        EnsureNoReparsePointAncestors(destinationPath);
                        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
                        Directory.CreateDirectory(destinationDirectory);
                        DeleteFileIfPresent(destinationPath);
                        File.Move(backupPath, destinationPath);
                    }
                }, rollbackExceptions);
            }

            if (rollbackExceptions.Count > 0)
            {
                throw new HoYoShadeResourceTransactionException(transactionDirectory, commitException, new AggregateException(rollbackExceptions));
            }

            throw;
        }
    }

    public static string? FindEffectDirectory(string extractedDirectory)
    {
        return FindNamedDirectory(extractedDirectory, "Shaders")
            ?? FindShortestContainingDirectory(extractedDirectory, ["*.fx", "*.addonfx", "*.fxh"]);
    }

    public static string? FindTextureDirectory(string extractedDirectory)
    {
        return FindNamedDirectory(extractedDirectory, "Textures")
            ?? FindShortestContainingDirectory(extractedDirectory, ["*.png", "*.jpg", "*.jpeg"]);
    }

    private static string? FindNamedDirectory(string rootDirectory, string directoryName)
    {
        return Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(path), directoryName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => GetPathDepth(rootDirectory, path))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? FindShortestContainingDirectory(string rootDirectory, string[] searchPatterns)
    {
        return searchPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(rootDirectory, pattern, SearchOption.AllDirectories))
            .Select(static path => Path.GetDirectoryName(path))
            .OfType<string>()
            .OrderBy(path => GetPathDepth(rootDirectory, path))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool TryGetArchiveRelativePath(string entryName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? relativePath)
    {
        relativePath = default;
        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
        {
            return false;
        }

        string[] segments = entryName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is 0 || segments.Any(static segment => !IsSafeWindowsPathSegment(segment)))
        {
            return false;
        }

        relativePath = Path.Combine(segments);
        return true;
    }

    private static bool IsSafeWindowsPathSegment(string segment)
    {
        if (segment is "." or ".."
            || segment.EndsWith('.')
            || segment.EndsWith(' ')
            || segment.Any(static character => char.IsControl(character) || character is '<' or '>' or '"' or '|' or ':' or '*' or '?' or '\\' or '/'))
        {
            return false;
        }

        string deviceName = segment.Split('.', 2)[0].TrimEnd(' ', '.');
        return !deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            && !deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            && !deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            && !deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            && !IsNumberedDeviceName(deviceName, "COM")
            && !IsNumberedDeviceName(deviceName, "LPT");
    }

    private static bool IsNumberedDeviceName(string value, string prefix)
    {
        return value.Length is 4
            && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && value[3] is >= '1' and <= '9';
    }

    private static bool IsGitMetadataPath(string relativePath)
    {
        return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));
    }

    private static string CombineUnderRoot(string rootDirectory, string relativePath)
    {
        string root = Path.GetFullPath(rootDirectory);
        string destination = Path.GetFullPath(Path.Combine(root, relativePath));
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Invalid HoYoShade path");
        }

        EnsureNoReparsePointAncestors(destination);
        return destination;
    }

    private static void EnsureNoReparsePointAncestors(string path)
    {
        string currentPath = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(currentPath))
        {
            if (File.Exists(currentPath) || Directory.Exists(currentPath))
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) is not 0)
                {
                    throw new InvalidDataException("HoYoShade destination contains a reparse point");
                }
            }

            string? parentPath = Directory.GetParent(currentPath)?.FullName;
            if (string.IsNullOrEmpty(parentPath) || string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentPath = parentPath;
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryRecover(Action action, List<Exception> exceptions)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }
    }

    private static int GetPathDepth(string rootDirectory, string path)
    {
        return Path.GetRelativePath(rootDirectory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
    }
}

internal sealed class HoYoShadeResourceTransactionException : IOException
{
    public HoYoShadeResourceTransactionException(string transactionDirectory, Exception commitException, Exception rollbackException)
        : base($"HoYoShade resource transaction rollback was incomplete. Recovery data was retained at: {transactionDirectory}", rollbackException)
    {
        TransactionDirectory = transactionDirectory;
        Data[nameof(commitException)] = commitException;
    }

    public string TransactionDirectory { get; }
}
