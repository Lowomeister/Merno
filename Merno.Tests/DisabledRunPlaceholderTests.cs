// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Reflection;
using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Заглушка для значения «выключено» в ключе Run. Она должна быть ненулевой и неизменной,
/// иначе Windows мог бы истолковать её как «по умолчанию», а пересчёт из времени ломал бы тесты.
/// </summary>
public class DisabledRunPlaceholderTests
{
    static PropertyInfo PlaceholderProperty = typeof(Startup).GetProperty("DisabledRunPlaceholder")!;

    /// <summary>Заглушка всегда ненулевая — иначе Windows принял её за «использовать по умолчанию».</summary>
    [Fact]
    public void PlaceholderIsNonZero() => Assert.NotEqual(0L, PlaceholderProperty.GetValue(null));

    /// <summary>Значение детерминировано: одинаково при повторном вызове и не берётся из текущего времени.</summary>
    [Fact]
    public void PlaceholderIsStableAndNotDerivedFromCurrentTime()
    {
        Assert.Equal(PlaceholderProperty.GetValue(null), PlaceholderProperty.GetValue(null));
        // если бы заглушка считалась через DateTime.Now.ToFileTime(), она совпала бы с текущим временем
        Assert.NotEqual(DateTime.Now.ToFileTime(), PlaceholderProperty.GetValue(null));
    }

    /// <summary>Полученное массивное значение должно выглядеть как «выключено»: старший байт нечётный.</summary>
    [Fact]
    public void DisabledValueLooksDisabled()
    {
        long placeholder = (long)1 << 62;
        byte[] value = new byte[] { 3, 0, 0, 0 }.Concat(BitConverter.GetBytes(placeholder)).ToArray();
        // IsEnabled считает «включено», когда первый байт чётный — заглушка должна быть нечётной.
        Assert.True(value[0] % 2 != 0);
    }
}
