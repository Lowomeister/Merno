// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// «Инструменты → Интернет»: netsh int ip reset выходит с кодом 1 даже от администратора, потому что один раздел
/// Windows сбросить не даёт. Выводы взяты с Windows 11 (английский интерфейс) и Windows 10 (русский).
/// </summary>
public class NetResetTests
{
    const string English = """
        Resetting Compartment Forwarding, OK!
        Resetting Compartment, OK!
        Resetting Control Protocol, OK!
        Resetting Echo Sequence Request, OK!
        Resetting Global, OK!
        Resetting Interface, OK!
        Resetting Anycast Address, OK!
        Resetting Multicast Address, OK!
        Resetting Unicast Address, OK!
        Resetting Neighbor, OK!
        Resetting Path, OK!
        Resetting Potential, OK!
        Resetting Prefix Policy, OK!
        Resetting Proxy Neighbor, OK!
        Resetting Route, OK!
        Resetting Site Prefix, OK!
        Resetting Subinterface, OK!
        Resetting Wakeup Pattern, OK!
        Resetting Resolve Neighbor, OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , failed.
        Access is denied.

        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Resetting , OK!
        Restart the computer to complete this action.
        """;

    const string Russian = """
        Сброс Пересылка секций - OK!
        Сброс Секция - OK!
        Сброс Управляющий протокол - OK!
        Сброс Запрос эхо-последовательности - OK!
        Сброс Глобальный - OK!
        Сброс Интерфейс - OK!
        Сброс Адрес произвольной передачи - OK!
        Сброс Адрес многоадресной рассылки - OK!
        Сброс Адрес одноадресной рассылки - OK!
        Сброс Сосед - OK!
        Сброс Путь - OK!
        Сброс Потенциальный - OK!
        Сброс Политика префиксов - OK!
        Сброс Сосед прокси - OK!
        Сброс Маршрут - OK!
        Сброс Префикс сайта - OK!
        Сброс Подчиненный интерфейс - OK!
        Сброс Шаблон пробуждения - OK!
        Сброс Разрешить соседа - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - сбой.
        Отказано в доступе.

        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Сброс  - OK!
        Для завершения этого действия требуется перезагрузка.
        """;

    [Fact]
    public void OneDeniedSectionIsNotAFailure()
    {
        Assert.True(Tools.NetIpResetOk(1, English, out var en));
        Assert.Contains("failed", en);
        Assert.True(Tools.NetIpResetOk(1, Russian, out var ru));
        Assert.Contains("сбой", ru);
        Assert.True(Tools.NetIpResetOk(0, "", out var none));
        Assert.Null(none);
    }

    [Fact]
    public void RealFailuresStayFailures()
    {
        Assert.False(Tools.NetIpResetOk(1, English + "\nResetting , failed.", out _));   // две секции не сброшены
        Assert.False(Tools.NetIpResetOk(1, Russian + "\nСброс  - сбой.", out _));
        Assert.False(Tools.NetIpResetOk(1, "The requested operation requires elevation (Run as administrator).", out _));
        Assert.False(Tools.NetIpResetOk(1, "", out _));
        Assert.False(Tools.NetIpResetOk(-1, "timeout", out _));
        Assert.False(Tools.NetIpResetOk(2, English, out _));
    }
}
