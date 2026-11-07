// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;
using Xunit;

namespace Merno.Tests;

/// <summary>Код устройства без драйвера: из DeviceID берём VEN/DEV и производителя, чтобы подсказать, что это за устройство.</summary>
public class HwIdTests
{
    [Theory]
    [InlineData(@"PCI\VEN_80EE&DEV_CAFE&SUBSYS_00000000&REV_00\3&267A616A&0&20", "VEN_80EE&DEV_CAFE", "VirtualBox")]
    [InlineData(@"PCI\VEN_8086&DEV_A370&SUBSYS_00748086&REV_10\3&11583659&1&A0", "VEN_8086&DEV_A370", "Intel")]
    [InlineData(@"USB\VID_046D&PID_C52B\5&2A3B4C5D&0&1", "VID_046D&PID_C52B", "Logitech")]
    [InlineData(@"pci\ven_1234&dev_abcd\x", "VEN_1234&DEV_ABCD", "")]
    public void ParsesVendorAndDevice(string id, string hw, string maker)
    {
        var (h, m) = Drivers.HwId(id);
        Assert.Equal(hw, h);
        Assert.Equal(maker, m);
    }

    [Theory]
    [InlineData(@"ACPI\INT33A1\1", "INT33A1")]
    [InlineData(@"HID\ELAN0001\4&1", "ELAN0001")]
    public void ParsesAcpiAndHid(string id, string hw) => Assert.Equal(hw, Drivers.HwId(id).Id);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"ROOT\SYSTEM\0001")]
    public void UnknownGivesEmpty(string id) => Assert.Equal("", Drivers.HwId(id).Id);

    [Fact]
    public void SearchUrlIsEscapedAndNotYandex()
    {
        var url = Drivers.HwSearchUrl("VEN_80EE&DEV_CAFE");
        Assert.StartsWith("https://www.google.com/search?q=", url);
        Assert.Contains("VEN_80EE%26DEV_CAFE", url);
        Assert.DoesNotContain("yandex", url, StringComparison.OrdinalIgnoreCase);
    }
}
