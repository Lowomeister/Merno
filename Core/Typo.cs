// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.RegularExpressions;

namespace Merno.Core;

/// <summary>Типографика: короткие предлоги и союзы не остаются в конце строки.</summary>
public static class Typo
{
    private static readonly Regex Short = new(@"(?<=(^|[\s(«""—-]))(в|во|на|с|со|к|ко|о|об|у|и|а|но|по|за|из|от|до|не|ни|для|без|при|про|над|под|что|как|или|же|ли|это)[ \t]",RegexOptions.IgnoreCase);
    public static string Nbsp(string s) => string.IsNullOrEmpty(s) ? s : Short.Replace(s, m => m.Value.TrimEnd() + "\u00A0");
}
