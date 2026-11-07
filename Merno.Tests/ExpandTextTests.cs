// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Input;

namespace Merno.Tests;

public class ExpandTextTests
{
    [Fact]
    public void DescriptionClickExpandsAndCollapsesWithoutActivatingParent()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var description = new Merno.ExpandText(string.Concat(Enumerable.Repeat("Длинное описание программы. ", 12)));
                description.Measure(new Size(220, double.PositiveInfinity));
                double collapsedHeight = description.DesiredSize.Height;
                void Click()
                {
                    var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
                    description.RaiseEvent(click);
                    Assert.True(click.Handled);
                    description.Measure(new Size(220, double.PositiveInfinity));
                }
                Click();
                Assert.True(description.IsExpanded);
                Assert.True(description.DesiredSize.Height > collapsedHeight);
                Assert.DoesNotContain("ещё", description.Inner.Text);
                Click();
                Assert.False(description.IsExpanded);
                Assert.Equal(collapsedHeight, description.DesiredSize.Height);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF layout did not finish");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
