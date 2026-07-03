// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Snap.Hutao.Service.Game.Island;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct IslandEnvironment
{
    public float FieldOfView;
    public uint TargetFrameRate;
    public uint Flags;
}
