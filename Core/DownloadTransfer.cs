// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

namespace Merno.Core;

internal static class DownloadTransfer
{
    internal static async Task<long> CopyAsync(Stream source, Stream destination, long maximum,
        TimeSpan idleTimeout, CancellationToken token, Func<long, Task> progress = null)
    {
        byte[] buffer = new byte[81920];
        long received = 0;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(idleTimeout);
            int count;
            try { count = await source.ReadAsync(buffer.AsMemory(), idle.Token).AsTask().WaitAsync(idle.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Сервер слишком долго не передаёт данные."); }
            if (count == 0) return received;
            if (received > maximum - count) throw new InvalidDataException("Файл больше допустимого размера.");
            received += count;
            await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            if (progress != null) await progress(received).ConfigureAwait(false);
        }
    }
}
