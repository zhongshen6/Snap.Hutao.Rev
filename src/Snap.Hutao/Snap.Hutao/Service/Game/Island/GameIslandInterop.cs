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
    private const uint InputSwitchEnabledFlag = 1U;

    // 纪念性保留：本功能完成后的次日，官方宣布后续版本原生支持键鼠与手柄热切换。
    // 这条注入路线不再具有长期维护价值，仅用于旧版本兼容并记录这段研究工作。
    private const uint InputSwitchSettingsActionBridge = 0x12079D10;
    private const uint InputSwitchPageManagerOpen = 0x0AA1F340;
    private const uint InputSwitchGIGIILOCMMA = 0x08CF2330;
    private const uint InputSwitchCurrentUiModeOffset = 0x320;
    private const uint InputSwitchClosePageVtableSlot = 75;

    private static ReadOnlySpan<uint> EnvironmentReservedSignature
    {
        get
        {
            return
            [
                0x01661520, 0x17D036D0, 0x01138D60, 0x01138D50, 0x0DF1EE40,
                0x066EE0D0, 0x0045CDA0, 0x12464640, 0x0B560070, 0x0B55EF60,
                0x1275B9B0, 0x0FD74170, 0x105AAD50, 0x08BD0290, 0x01132490,
                0x01131C50, 0x0C14AA06, 0x12424A60, 0x08D00130,
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
        pIslandEnvironment->InputSwitchFlags = options.GamepadHotSwitchEnabled.Value ? InputSwitchEnabledFlag : 0U;
        pIslandEnvironment->InputSwitchSettingsActionBridge = InputSwitchSettingsActionBridge;
        pIslandEnvironment->InputSwitchPageManagerOpen = InputSwitchPageManagerOpen;
        pIslandEnvironment->InputSwitchGIGIILOCMMA = InputSwitchGIGIILOCMMA;
        pIslandEnvironment->InputSwitchCurrentUiModeOffset = InputSwitchCurrentUiModeOffset;
        pIslandEnvironment->InputSwitchClosePageVtableSlot = InputSwitchClosePageVtableSlot;
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
        if (sizeof(IslandEnvironment) != 112)
        {
            throw HutaoException.InvalidOperation($"IslandEnvironment layout mismatch, expected 112 but got {sizeof(IslandEnvironment)}");
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
