using System.Collections.Generic;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Tests.ViewModels;

[TestFixture]
public class OverlayHostViewModelTests
{
    [Test]
    public void Show_SetsCurrentOverlay_AndOpensTheLayer()
    {
        var host = new OverlayHostViewModel();
        var overlay = new object();

        host.Show(overlay);

        Assert.That(host.CurrentOverlay, Is.SameAs(overlay));
        Assert.That(host.IsOverlayOpen, Is.True);
    }

    [Test]
    public void Close_ClearsCurrentOverlay_AndFiresCallbackOnce()
    {
        var host = new OverlayHostViewModel();
        int closedCount = 0;
        host.Show(new object(), onClosed: () => closedCount++);

        host.Close();
        host.Close();

        Assert.That(host.IsOverlayOpen, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
    }

    [Test]
    public void Close_WithNoOverlayOpen_DoesNothing()
    {
        var host = new OverlayHostViewModel();

        Assert.DoesNotThrow(host.Close);
        Assert.That(host.IsOverlayOpen, Is.False);
    }

    [Test]
    public void Close_StateIsAlreadyCleared_WhenCallbackRuns()
    {
        // A flow's onClosed may immediately Show the next overlay — the host must not
        // null out state after the callback, or it would wipe the new overlay.
        var host = new OverlayHostViewModel();
        var next = new object();
        host.Show(new object(), onClosed: () => host.Show(next));

        host.Close();

        Assert.That(host.CurrentOverlay, Is.SameAs(next));
    }

    [Test]
    public void Show_OverAnOpenOverlay_ReplacesIt_AndFiresThePreviousCallback()
    {
        var host = new OverlayHostViewModel();
        int firstClosedCount = 0;
        var second = new object();
        host.Show(new object(), onClosed: () => firstClosedCount++);

        host.Show(second);

        Assert.That(host.CurrentOverlay, Is.SameAs(second));
        Assert.That(firstClosedCount, Is.EqualTo(1));
    }

    [Test]
    public void Show_DoesNotReuseThePreviousCallback()
    {
        var host = new OverlayHostViewModel();
        int firstClosedCount = 0;
        host.Show(new object(), onClosed: () => firstClosedCount++);
        host.Show(new object());

        host.Close();

        // The first overlay's callback fired on replacement, not again for the second.
        Assert.That(firstClosedCount, Is.EqualTo(1));
    }

    [Test]
    public void IsOverlayOpen_RaisesPropertyChanged_WhenCurrentOverlayChanges()
    {
        var host = new OverlayHostViewModel();
        var raised = new List<string?>();
        host.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        host.Show(new object());

        Assert.That(raised, Does.Contain(nameof(OverlayHostViewModel.CurrentOverlay)));
        Assert.That(raised, Does.Contain(nameof(OverlayHostViewModel.IsOverlayOpen)));
    }

    [Test]
    public void TryLightDismiss_ByDefault_ClosesAndFiresCallback()
    {
        var host = new OverlayHostViewModel();
        int closedCount = 0;
        host.Show(new object(), onClosed: () => closedCount++);

        bool dismissed = host.TryLightDismiss();

        Assert.That(dismissed, Is.True);
        Assert.That(host.IsOverlayOpen, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
    }

    [Test]
    public void TryLightDismiss_WhenDisallowed_KeepsTheOverlayOpen()
    {
        var host = new OverlayHostViewModel();
        int closedCount = 0;
        host.Show(new object(), onClosed: () => closedCount++, allowLightDismiss: false);

        bool dismissed = host.TryLightDismiss();

        Assert.That(dismissed, Is.False);
        Assert.That(host.IsOverlayOpen, Is.True);
        Assert.That(closedCount, Is.Zero);
    }

    [Test]
    public void Close_StillWorks_WhenLightDismissIsDisallowed()
    {
        var host = new OverlayHostViewModel();
        int closedCount = 0;
        host.Show(new object(), onClosed: () => closedCount++, allowLightDismiss: false);

        host.Close();

        Assert.That(host.IsOverlayOpen, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
    }

    [Test]
    public void TryLightDismiss_WithNoOverlayOpen_ReturnsFalse()
    {
        var host = new OverlayHostViewModel();

        Assert.That(host.TryLightDismiss(), Is.False);
    }
}
