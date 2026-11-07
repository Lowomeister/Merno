// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text;

namespace Merno.Core;

internal static class BoundedOutput
{
    internal sealed record Result(string Text, bool Truncated);
    internal static async Task<Result> ReadAsync(TextReader reader, Action<string> onLine = null, int limit = 2 * 1024 * 1024)
    {
        var output = new StringBuilder();
        var line = new StringBuilder();
        char[] buffer = new char[4096];
        bool truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            int keep = Math.Min(count, Math.Max(0, limit - output.Length));
            output.Append(buffer, 0, keep);
            if (keep != count) truncated = true;
            if (onLine == null) continue;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] is '\r' or '\n')
                {
                    if (line.Length > 0) { try { onLine(line.ToString()); } catch { } line.Clear(); }
                }
                else if (line.Length < 65536) line.Append(buffer[i]);
                else truncated = true;
            }
        }
        if (line.Length > 0) { try { onLine?.Invoke(line.ToString()); } catch { } }
        return new(output.ToString(), truncated);
    }
}
