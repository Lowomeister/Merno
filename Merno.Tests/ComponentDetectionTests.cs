// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Определение уже установленных компонентов (Core/Apps.cs).
/// Проверяемые случаи: PhysX на русской Windows и Java у разных производителей, а не только у Temurin.
/// </summary>
public class ComponentDetectionTests
{
    // ---------- PhysX ----------

    [Fact]
    public void PhysX_находится_по_русскому_названию()
    {
        var names = new[] { "NVIDIA Системное программное обеспечение PhysX" };
        Assert.True(Apps.PhysXDetected(names, loader64: false, loader32: false));
    }

    [Fact]
    public void PhysX_находится_по_английскому_названию()
    {
        var names = new[] { "NVIDIA PhysX System Software" };
        Assert.True(Apps.PhysXDetected(names, false, false));
    }

    [Fact]
    public void Драйвер_NVIDIA_без_PhysX_не_считается_PhysX()
    {
        var names = new[] { "NVIDIA Graphics Driver 566.36", "NVIDIA HD Audio Driver" };
        Assert.False(Apps.PhysXDetected(names, false, false));
    }

    [Fact]
    public void PhysX_находится_по_файлу_загрузчика()
    {
        // Записи в «Программах» может не быть (портативная установка) — остаётся файл.
        Assert.True(Apps.PhysXDetected(Array.Empty<string>(), loader64: true, loader32: false));
        Assert.True(Apps.PhysXDetected(Array.Empty<string>(), loader64: false, loader32: true));
    }

    [Fact]
    public void Слово_PhysX_без_NVIDIA_не_считается_PhysX()
    {
        Assert.False(Apps.PhysXDetected(new[] { "PhysX SDK" }, false, false));
    }

    // ---------- Java: версии по названиям ----------

    [Theory]
    [InlineData("Eclipse Temurin JRE with Hotspot 17.0.12", 17)]
    [InlineData("Microsoft Build of OpenJDK with Hotspot 21.0.5", 21)]
    [InlineData("Java 8 Update 401", 8)]
    [InlineData("Java(TM) SE Development Kit 21.0.2", 21)]
    [InlineData("Java SE 17", 17)]
    [InlineData("Zulu JDK 17", 17)]
    [InlineData("Amazon Corretto 21", 21)]
    [InlineData("Liberica JDK 21", 21)]
    [InlineData("IBM Semeru Runtime 11", 11)]
    [InlineData("Java 1.8.0_402", 8)]
    public void Java_находится_у_любого_производителя(string name, int expected)
    {
        Assert.Contains(expected, Apps.JavaMajorsFromNames(new[] { name }));
    }

    [Theory]
    [InlineData("JavaScript Runtime")]
    [InlineData("Java Auto Updater")]
    [InlineData("Java 3")]
    [InlineData("Java 99")]
    [InlineData("Просто программа")]
    public void Не_Java_не_попадает_в_список(string name)
    {
        Assert.Empty(Apps.JavaMajorsFromNames(new[] { name }));
    }

    [Fact]
    public void Несколько_версий_Java_возвращаются_вместе()
    {
        var majors = Apps.JavaMajorsFromNames(new[]
        {
            "Java 8 Update 401",
            "Eclipse Temurin JRE with Hotspot 17.0.12",
            "Microsoft Build of OpenJDK with Hotspot 21.0.5"
        });

        Assert.Equal(new[] { 8, 17, 21 }, majors.OrderBy(x => x));
    }

    [Fact]
    public void Null_и_пустой_список_не_ломают_проверку()
    {
        Assert.Empty(Apps.JavaMajorsFromNames(null));
        Assert.Empty(Apps.JavaMajorsFromNames(Array.Empty<string>()));
        Assert.Empty(Apps.JavaMajorsFromNames(new string[] { null }));
    }

    [Theory]
    [InlineData("1.8.0_402", 8)]
    [InlineData("21.0.2", 21)]
    [InlineData("17", 17)]
    [InlineData("17.0.12", 17)]
    [InlineData("zulu17", 0)]
    [InlineData("JavaScript", 0)]
    [InlineData("99", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void JavaMajorOf_разбирает_номер_версии(string value, int expected)
    {
        Assert.Equal(expected, Apps.JavaMajorOf(value));
    }

    // ---------- Java: Id пакетов Temurin ----------

    [Theory]
    [InlineData("EclipseAdoptium.Temurin.17.JRE", 17)]
    [InlineData("EclipseAdoptium.Temurin.8.JDK", 8)]
    [InlineData("EclipseAdoptium.Temurin.21.JRE", 21)]
    [InlineData("Microsoft.OpenJDK.21", 0)]
    [InlineData("EclipseAdoptium.Temurin.99.JRE", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void JavaMajorFromWinget_понимает_только_Temurin(string id, int expected)
    {
        Assert.Equal(expected, Apps.JavaMajorFromWinget(id));
    }

    // ---------- Сопоставление названий ----------

    [Fact]
    public void Match_сравнивает_начало_названия()
    {
        var names = new[] { "NVIDIA Graphics Driver 566.36", "Mozilla Firefox" };
        Assert.True(Apps.NameMatches(names, "NVIDIA"));
        Assert.True(Apps.NameMatches(names, "Mozilla Firefox"));
    }

    [Fact]
    public void Короткий_кусок_в_середине_названия_не_считается()
    {
        // Именно поэтому для Java/PhysX есть отдельная проверка ExtraDetect.
        Assert.False(Apps.NameMatches(new[] { "My PhysX Runtime" }, "PhysX"));
        Assert.True(Apps.NameMatches(new[] { "My PhysX Runtime" }, "My PhysX Runtime"));
    }

    [Fact]
    public void Match_понимает_несколько_вариантов_через_черту()
    {
        Assert.True(Apps.NameMatches(new[] { "Bar App" }, "Foo|Bar"));
        Assert.False(Apps.NameMatches(new[] { "Baz App" }, "Foo|Bar"));
    }

    [Fact]
    public void Пустой_match_ничего_не_находит()
    {
        Assert.False(Apps.NameMatches(new[] { "Что угодно" }, ""));
        Assert.False(Apps.NameMatches(new[] { "Что угодно" }, null));
        Assert.False(Apps.NameMatches(null, "Что угодно"));
    }
}
