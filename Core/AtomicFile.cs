// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text;

namespace Merno.Core;

internal static class AtomicFile
{
    internal static void Write(string path, string text, Action<string, string> replace = null)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            if (replace == null) File.Move(temp, path, overwrite: true);
            else replace(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
