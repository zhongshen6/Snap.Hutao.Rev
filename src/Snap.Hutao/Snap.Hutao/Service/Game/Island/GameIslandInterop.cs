// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.Diagnostics;
using Snap.Hutao.Core.ExceptionService;
using Snap.Hutao.Core.LifeCycle.InterProcess.FullTrust;
using Snap.Hutao.Core.Setting;
using Snap.Hutao.Factory.Process;
using Snap.Hutao.Service.Game.Launching.Context;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Windows.Devices.Input;

namespace Snap.Hutao.Service.Game.Island;

internal sealed class GameIslandInterop : IGameIslandInterop
{
    private const string IslandEnvironmentName = "4F3E8543-40F7-4808-82DC-21E48A6037A7";
    internal const string IslandLibraryName = "nvd3dump.dll";

    private static ReadOnlySpan<uint> EnvironmentReservedSignature
    {
        get
        {
            return
            [
                0x015946C0, 0x15FA41D0, 0x01097AD0, 0x01097AC0, 0x0CC45B70,
                0x061651D0, 0x00417CC0, 0x074A59B0, 0x0F87D050, 0x0F879E20,
                0x078AE580, 0x09D24A70, 0x0EFC9F90, 0x09EB61F0, 0x01091400,
                0x01090BE0, 0x0FCDDA80, 0x10A8D900, 0x0A1EEA40,
            ];
        }
    }

    private readonly bool resume;
    private readonly TaskCompletionSource injectionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? islandPath;

    public GameIslandInterop(bool resume)
    {
        ValidateEnvironmentLayout();
        this.resume = resume;
    }

    public static bool IsIslandLibraryAvailable()
    {
        return File.Exists(Path.Combine(AppContext.BaseDirectory, IslandLibraryName));
    }

    public ValueTask BeforeAsync(BeforeLaunchExecutionContext context)
    {
        if (resume)
        {
            return ValueTask.CompletedTask;
        }

        islandPath = Path.Combine(AppContext.BaseDirectory, IslandLibraryName);
        return ValueTask.CompletedTask;
    }

    public ValueTask WaitForInjectionReadyAsync(CancellationToken token = default)
    {
        return token.CanBeCanceled
            ? new(injectionReady.Task.WaitAsync(token))
            : new(injectionReady.Task);
    }

    public async ValueTask WaitForExitAsync(LaunchExecutionContext context, CancellationToken token = default)
    {
        try
        {
            MemoryMappedFile file;
            if (resume)
            {
                try
                {
                    file = MemoryMappedFile.OpenExisting(IslandEnvironmentName);
                }
                catch (FileNotFoundException)
                {
                    injectionReady.TrySetResult();
                    return;
                }
            }
            else
            {
                file = MemoryMappedFile.CreateOrOpen(IslandEnvironmentName, 1024);
            }

            using (file)
            using (MemoryMappedViewAccessor accessor = file.CreateViewAccessor())
            {
                nint handle = accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
                InitializeIslandEnvironment(handle, context.LaunchOptions);

                if (!resume)
                {
                    ArgumentException.ThrowIfNullOrEmpty(islandPath);
                    if (!File.Exists(islandPath))
                    {
                        throw HutaoException.InvalidOperation($"Missing {IslandLibraryName}: {islandPath}");
                    }

                    if (context.Process is FullTrustProcess fullTrustProcess)
                    {
                        fullTrustProcess.LoadLibrary(FullTrustLoadLibraryRequest.Create("Island", islandPath));
                    }
                    else
                    {
                        try
                        {
                            DllInjectionUtilities.InjectUsingRemoteThread(islandPath, context.Process.Id);
                        }
                        catch (Exception ex)
                        {
                            SentrySdk.AddBreadcrumb(
                                $"Island DLL injection failed: {ex.Message}",
                                category: "island.injection",
                                level: Sentry.BreadcrumbLevel.Error);

                            throw HutaoException.Throw($"Island DLL injection failed: {ex.Message}", ex);
                        }
                    }
                }

                injectionReady.TrySetResult();
                await PeriodicUpdateIslandEnvironmentAsync(context, handle, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            injectionReady.TrySetException(ex);
            throw;
        }
    }

    private static unsafe void InitializeIslandEnvironment(nint handle, LaunchOptions options)
    {
        IslandEnvironment* pIslandEnvironment = (IslandEnvironment*)handle;
        MemoryMarshal.AsBytes(EnvironmentReservedSignature).CopyTo(new Span<byte>(pIslandEnvironment->Reserved, 76));
        UpdateIslandEnvironment(handle, options);
    }

    private static unsafe void UpdateIslandEnvironment(nint handle, LaunchOptions options)
    {
        IslandEnvironment* pIslandEnvironment = (IslandEnvironment*)handle;
        bool usingTouchScreen = LocalSetting.Get(SettingKeys.LaunchForceUsingTouchScreen, false)
            ? IsIntegratedTouchPresent()
            : options.UsingTouchScreen.Value;

        pIslandEnvironment->FieldOfView = options.TargetFov.Value;
        pIslandEnvironment->TargetFrameRate = (uint)Math.Max(options.TargetFps.Value, 0);
        pIslandEnvironment->Flags = BuildFlags(options, usingTouchScreen);
    }

    private static uint BuildFlags(LaunchOptions options, bool usingTouchScreen)
    {
        uint flags = 0U;
        SetFlag(ref flags, 0, options.IsSetFieldOfViewEnabled.Value);
        SetFlag(ref flags, 1, options.FixLowFovScene.Value);
        SetFlag(ref flags, 2, options.DisableFog.Value);
        SetFlag(ref flags, 3, options.IsSetTargetFrameRateEnabled.Value);
        SetFlag(ref flags, 4, options.RemoveOpenTeamProgress.Value);
        SetFlag(ref flags, 5, options.HideQuestBanner.Value);
        SetFlag(ref flags, 6, options.DisableEventCameraMove.Value);
        SetFlag(ref flags, 7, options.DisableShowDamageText.Value);
        SetFlag(ref flags, 8, usingTouchScreen);
        SetFlag(ref flags, 9, options.RedirectCombineEntry.Value);
        SetFlag(ref flags, 10, options.ResinListItemId000106Allowed.Value);
        SetFlag(ref flags, 11, options.ResinListItemId000201Allowed.Value);
        SetFlag(ref flags, 12, options.ResinListItemId107009Allowed.Value);
        SetFlag(ref flags, 13, options.ResinListItemId107012Allowed.Value);
        SetFlag(ref flags, 14, options.ResinListItemId220007Allowed.Value);
        SetFlag(ref flags, 15, options.HideUid.Value);
        return flags;
    }

    private static void SetFlag(ref uint flags, int bit, bool value)
    {
        uint mask = 1U << bit;
        if (value)
        {
            flags |= mask;
        }
        else
        {
            flags &= ~mask;
        }
    }

    private static unsafe void ValidateEnvironmentLayout()
    {
        if (sizeof(IslandEnvironment) != 88)
        {
            throw HutaoException.InvalidOperation($"IslandEnvironment layout mismatch, expected 88 but got {sizeof(IslandEnvironment)}");
        }
    }

    private static bool IsIntegratedTouchPresent()
    {
        IReadOnlyList<PointerDevice> devices = PointerDevice.GetPointerDevices();
        for (int i = 0; i < devices.Count; i++)
        {
            PointerDevice device = devices[i];
            if (device is { PointerDeviceType: PointerDeviceType.Touch, IsIntegrated: true })
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask PeriodicUpdateIslandEnvironmentAsync(LaunchExecutionContext context, nint handle, CancellationToken token)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            if (!context.Process.IsRunning)
            {
                break;
            }

            UpdateIslandEnvironment(handle, context.LaunchOptions);
        }
    }
}
