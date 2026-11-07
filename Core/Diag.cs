// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Merno.Core;

/// <summary>
/// «Чёрный ящик» для редких подвисаний (например, на 5–10 секунд при закрытии после часа работы).
/// Одна строка в журнал: память, дескрипторы, объекты окон и значков (GDI/USER), потоки.
/// Пишется раз в 15 минут и на каждом шаге выхода — по журналу видно, что растёт и на каком шаге пауза.
/// </summary>
public static class Diag
{
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr hProcess, uint flags);
    static readonly Stopwatch _exit = new();

    public static string Snapshot()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            long gc = GC.GetTotalMemory(false);
            return $"память {p.WorkingSet64 >> 20} МБ (своя {p.PrivateMemorySize64 >> 20}, .NET {gc >> 20}), " +
                   $"дескрипторы {p.HandleCount}, GDI {GetGuiResources(p.Handle, 0)}, USER {GetGuiResources(p.Handle, 1)}, потоки {p.Threads.Count}";
        }
        catch (Exception ex) { return "замер не удался: " + ex.Message; }
    }

    public static void Periodic() => Log.Skip("Состояние Merno: " + Snapshot());

    /// <summary>Шаг выхода: время от начала закрытия + замер.</summary>
    public static void ExitStep(string step)
    {
        if (!_exit.IsRunning) _exit.Start();
        Log.Skip($"Выход: {step} (+{_exit.ElapsedMilliseconds} мс): {Snapshot()}");
    }
}
