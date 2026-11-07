// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Merno.Core;

/// <summary>Итог проверки version.json. Срок и часы оцениваются только у данных с верной подписью.</summary>
internal enum MetaCheck { Ok, Invalid, Expired, ClockBehind }

internal static class UpdateMetadata
{
    /// <summary>Наибольший срок подписи данных о выпуске (release.ps1 ставит 90 дней).</summary>
    internal const long MaxLifetime = 90 * 86400L;

    internal static byte[] Payload(UpdateInfo info) => Encoding.UTF8.GetBytes(
        $"v2|{info.Version}|{info.Url}|{info.Sha256}|{info.Size}|{info.Format}|{info.Published}|{info.Expires}|{info.Notes}");

    internal static MetaCheck Check(UpdateInfo info, RSA key, long now)
    {
        try
        {
            if (info.Format != "installed-kit-v1" || !Regex.IsMatch(info.Url ?? "", @"\AMerno-\d+\.\d+\.\d+\.bin\z") ||
                info.Notes.Length > 300 || info.Expires <= info.Published || info.Expires - info.Published > MaxLifetime) return MetaCheck.Invalid;
            if (!key.VerifyData(Payload(info), Convert.FromBase64String(info.SigMeta), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return MetaCheck.Invalid;
            if (info.Published > now + 300) return MetaCheck.ClockBehind;
            if (info.Expires <= now) return MetaCheck.Expired;
            return MetaCheck.Ok;
        }
        catch { return MetaCheck.Invalid; }
    }

    /// <summary>Строгая проверка: подпись верна и срок не истёк.</summary>
    internal static bool Verify(UpdateInfo info, RSA key, long now) => Check(info, key, now) == MetaCheck.Ok;

    /// <summary>
    /// Принимать ли данные. По HTTPS с проверенным сертификатом (GitHub) старый ответ не подсунуть, поэтому истёкший
    /// срок и неверные часы компьютера не мешают (иначе без выпусков дольше срока у всех «Не удалось проверить»).
    /// Без такого соединения (pinned = false) срок обязателен: посредник мог бы вечно отдавать старый подписанный ответ.
    /// </summary>
    internal static bool Accept(MetaCheck result, bool pinned) =>
        result == MetaCheck.Ok || (pinned && (result == MetaCheck.Expired || result == MetaCheck.ClockBehind));
}
