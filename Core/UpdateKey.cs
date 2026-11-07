// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

namespace Merno.Core;

/// <summary>
/// Открытый ключ RSA-3072 для проверки подписи обновлений (создаётся tools\keygen.ps1).
/// Закрытый ключ в репозиторий не входит.
/// </summary>
public static class UpdateKey
{
    public const string PublicXml = "<RSAKeyValue><Modulus>mkGN23bdAYXbXA70+15FoAh4S5+B2m6X23zXPr/1j4Q0GC/E0wWZm3AW/W9g9OZKbLnz/LaHQz/cwudYFjBzvbRfK9AyIeUXAXi3x+7A3O/nRyXeF/kAFArJ65K1mjl1P1lBbbuPrthegmuICHQ5L8iYjfsl39HVUIWPV7ihPEPQGjkMXhftyoH5L0RPbjYNz5vkFOJ6GqziHWsSFHSh2riOGSIk1UHKQ/KD07QUGGkCmXCoyn0mZRY/oxFsWGNF0SeuPN/GaafGJ7cCEVocyNvIPpKlQ9HVmoYy0eusubYisVtMgeDPleFSYv5HUG6ZHf/RF5ki3g9ZsR2nKdmpaUHJ9Zqk6a0fMbmM+/GdwUhUb9XoUfoKS0FTiCo6WtK+O0Lc4T+R1B8VukhW64HvHcu+P3L0qVB9I+4/RpdChHODXUh/4EeqhKa0ZbolfDafEQpLPbMkDyw23Tv64DygaCKrh8ZT/zXacNTmkuyOxQUMhg6q7YGH2y8LDFICDENR</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
}