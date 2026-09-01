using Godot;

namespace FortressRush;

/// <summary>Reusable UI animation helpers using Godot Tweens (zero-allocation, auto-kill).
/// Every method returns the Tween so callers can chain or await.</summary>
public static class UIAnimator
{
    /// <summary>Fade a Control from its current alpha to target over duration seconds.</summary>
    public static Tween Fade(Control c, float targetAlpha, float duration = 0.3f)
    {
        var tw = c.CreateTween();
        tw.TweenProperty(c, "modulate:a", targetAlpha, duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        return tw;
    }

    /// <summary>Fade in: alpha 0→1.</summary>
    public static Tween FadeIn(Control c, float duration = 0.3f)
    {
        c.Modulate = new Color(c.Modulate, 0f);
        return Fade(c, 1f, duration);
    }

    /// <summary>Fade out: alpha 1→0. Optionally frees the node after.</summary>
    public static Tween FadeOut(Control c, float duration = 0.3f, bool freeAfter = false)
    {
        var tw = Fade(c, 0f, duration);
        if (freeAfter) tw.TweenCallback(Callable.From(() => c.QueueFree()));
        return tw;
    }

    /// <summary>Pulse scale: 1.0 → scale → 1.0.</summary>
    public static Tween Pulse(Control c, float scale = 1.08f, float duration = 0.25f)
    {
        c.PivotOffset = c.Size * 0.5f;
        var tw = c.CreateTween().SetParallel(true);
        tw.TweenProperty(c, "scale", Vector2.One * scale, duration * 0.5f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tw.Chain().TweenProperty(c, "scale", Vector2.One, duration * 0.5f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        return tw;
    }

    /// <summary>Slide in from an offset (e.g. new Vector2(0, -100) slides down from top).</summary>
    public static Tween SlideIn(Control c, Vector2 fromOffset, float duration = 0.35f)
    {
        var orig = c.Position;
        c.Position = orig + fromOffset;
        c.Modulate = new Color(c.Modulate, 0f);
        var tw = c.CreateTween().SetParallel(true);
        tw.TweenProperty(c, "position", orig, duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tw.TweenProperty(c, "modulate:a", 1f, duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        return tw;
    }

    /// <summary>Shake: random offset oscillation that returns to origin.</summary>
    public static Tween Shake(Control c, float intensity = 8f, float duration = 0.3f)
    {
        var orig = c.Position;
        var tw = c.CreateTween();
        int steps = Mathf.Max(3, (int)(duration / 0.05f));
        for (int i = 0; i < steps; i++)
        {
            float t = (float)i / steps;
            float decay = 1f - t;
            var offset = new Vector2(
                (Godot.GD.Randf() - 0.5f) * intensity * decay,
                (Godot.GD.Randf() - 0.5f) * intensity * decay);
            tw.TweenProperty(c, "position", orig + offset, duration / steps);
        }
        tw.TweenProperty(c, "position", orig, duration / steps);
        return tw;
    }

    /// <summary>Stagger a list of controls: fade+slide each with increasing delay.</summary>
    public static void StaggerIn(Control[] controls, float delayPer = 0.08f, float duration = 0.3f)
    {
        for (int i = 0; i < controls.Length; i++)
        {
            var c = controls[i];
            c.Modulate = new Color(c.Modulate, 0f);
            c.Position += new Vector2(0, 30f);
            var tw = c.CreateTween().SetParallel(true);
            var t1 = tw.TweenProperty(c, "position:y", c.Position.Y - 30f, duration)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            var t2 = tw.TweenProperty(c, "modulate:a", 1f, duration)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);
            if (i > 0) { t1.SetDelay(delayPer * i); t2.SetDelay(delayPer * i); }
        }
    }
}
