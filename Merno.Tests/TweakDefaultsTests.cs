// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Выключить настройку, которую включили до Merno или в самой Windows (копии «как было» нет):
/// Merno возвращает значения Windows по умолчанию (Catalog.DefaultsPlan).
/// </summary>
public class TweakDefaultsTests
{
    private static Tweak Get(string id) => Catalog.All().Single(t => t.Id == id);

    [Fact]
    public void Анимации_возвращаются_к_Windows_по_умолчанию()
    {
        var plan = Catalog.DefaultsPlan(Get("visual_fx"), null);
        Assert.NotNull(plan);
        Assert.Contains(plan, p => p.Op.Name == "MinAnimate" && (string)p.Value == "1");
        Assert.Contains(plan, p => p.Op.Name == "TaskbarAnimations" && Equals(p.Value, 1L));
        // параметры с Cond = false Merno не меняет — и назад их не трогаем
        Assert.DoesNotContain(plan, p => p.Op.Name == "UserPreferencesMask");
    }

    [Fact]
    public void Политика_по_умолчанию_не_задана()
    {
        var tweak = Catalog.All().First(t => t.Do == null && t.Svc.Count == 0 && t.Tasks.Count == 0 &&
                                             t.Reg.Count > 0 && t.Reg.All(o => o.Cond == null && o.Path.Contains(@"\Policies\")));
        var plan = Catalog.DefaultsPlan(tweak, null);
        Assert.NotNull(plan);
        Assert.All(plan, p => Assert.Same(Catalog.Absent, p.Value));
    }

    [Fact]
    public void Без_значений_по_умолчанию_только_намеренные_исключения()
    {
        // зависят от железа или прежних настроек — честно говорим «не знаем»
        var exempt = new HashSet<string> { "vbs_off", "hags", "sticky_keys" };
        var unknown = Catalog.All()
            .Where(t => t.CanRevert && t.Do == null && t.Svc.Count == 0 && t.Tasks.Count == 0 && t.Reg.Any(o => o.Cond == null))
            .Where(t => Catalog.DefaultsPlan(t, null) == null)
            .Select(t => t.Id).Where(id => !exempt.Contains(id)).ToList();
        Assert.Empty(unknown);
    }

    [Fact]
    public void Настройки_со_своим_действием_так_не_откатываются() =>
        Assert.All(Catalog.All().Where(t => t.Do != null), t => Assert.Null(Catalog.DefaultsPlan(t, null)));
}
