// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

namespace Merno.Core;

internal static class SystemOperation
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static IDisposable TryEnter() => Gate.Wait(0) ? new Lease() : null;
    private sealed class Lease : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) Gate.Release(); }
    }
}
