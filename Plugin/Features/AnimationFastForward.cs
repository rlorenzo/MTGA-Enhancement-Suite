using System;
using MTGAEnhancementSuite.UI;
using UnityEngine;
using Wotc.Mtga.DuelScene.UXEvents;

namespace MTGAEnhancementSuite.Features
{
    /// <summary>
    /// Press-to-toggle fast-forward for MTGA's per-duel animation queue.
    /// Designed for the "opponent stacks 12 triggers and the client falls
    /// minutes behind the GRE server" scenario — instead of skipping the
    /// animations outright (which is what the prior <c>Drain</c> variant
    /// did) we accelerate playback so the user can still see what's
    /// happening, just compressed into ~1 second.
    ///
    /// Mechanism:
    /// <list type="bullet">
    ///   <item><see cref="Time.timeScale"/> = <see cref="FastForwardMultiplier"/>.
    ///     <c>GameManager.Update_UXEventQueue()</c> passes <c>Time.deltaTime</c>
    ///     straight into <c>UXEventQueue.Update(dt)</c>, so the queue's
    ///     <c>_timeRunning</c> accumulates this much faster and each
    ///     <c>ResolutionEffectUXEvent</c> reaches <c>_duration</c> sooner.
    ///     Unity <c>Animator</c>s and <c>ParticleSystem</c>s also use
    ///     <c>Time.deltaTime</c> by default, so the underlying visuals
    ///     genuinely play through, just faster.</item>
    ///   <item><see cref="Time.maximumDeltaTime"/> bumped to
    ///     <see cref="MaxDeltaTimeWhileFast"/>. Unity clamps
    ///     <c>Time.deltaTime</c> at <c>maximumDeltaTime</c> (default
    ///     0.333s) to keep physics stable — without this bump, a
    ///     timeScale of 100 only delivers ~20x at 60fps because the
    ///     per-frame dt gets clamped. MTGA has no meaningful physics,
    ///     so raising the cap is safe.</item>
    /// </list>
    ///
    /// Toggle UX: F7 starts fast-forward, watches the queue, and
    /// auto-stops the moment the queue goes idle (so the user can
    /// press-and-forget). Pressing F7 again while active cancels early
    /// and snaps back to 1× immediately. Defensive: on scene change /
    /// missing <c>GameManager</c> / plugin destruction the saved timeScale
    /// is restored so we never leave the game stuck at 100× speed.
    /// </summary>
    internal static class AnimationFastForward
    {
        // 100x — what the user asked for. The queue logic and Unity's
        // animators both honor this scale, so trigger animations that
        // normally take ~1.5s each complete in ~15ms. 12 triggers ≈ 180ms
        // visible compression with all animations still rendering.
        private const float FastForwardMultiplier = 100f;

        // Default Time.maximumDeltaTime is 0.333s. We push it high so the
        // 100x scale isn't silently clamped down. 10 seconds is wildly
        // generous — even on a single 1fps frame the queue could
        // accumulate 1000s of game-time, which is fine because the queue
        // has no upper bound check, and saturates naturally when events
        // hit their _duration target.
        private const float MaxDeltaTimeWhileFast = 10f;

        private static bool _active;
        private static float _savedTimeScale;
        private static float _savedMaxDeltaTime;

        public static bool IsActive => _active;

        /// <summary>
        /// F7 entry point. Starts fast-forward if idle, cancels it if
        /// already running. No-ops cleanly outside a duel.
        /// </summary>
        public static void Toggle()
        {
            try
            {
                if (_active) { Stop(reason: "cancelled"); return; }
                Start();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"AnimationFastForward.Toggle failed: {ex}");
                ForceRestore(); // never leave the game stuck at 100x
            }
        }

        /// <summary>
        /// Called every frame from <c>PluginBehaviour.Update</c>. When
        /// fast-forward is active, watches the queue and auto-stops as
        /// soon as it drains — that's the "caught up" signal. Also acts
        /// as a fail-safe: if the duel scene tears down while we're
        /// active, restore <see cref="Time.timeScale"/> immediately so
        /// the main menu doesn't get scrambled.
        /// </summary>
        public static void Tick()
        {
            if (!_active) return;
            var gm = UnityEngine.Object.FindObjectOfType<GameManager>();
            var queue = gm != null ? gm.UXEventQueue : null;
            if (queue == null) { Stop(reason: "duel ended"); return; }
            if (!queue.IsRunning) { Stop(reason: "caught up"); }
        }

        /// <summary>
        /// Always-restore exit point, called from
        /// <c>PluginBehaviour.OnDestroy</c>. Cheap to call when inactive.
        /// </summary>
        public static void ForceRestore()
        {
            if (_active) Stop(reason: "force restore");
        }

        private static void Start()
        {
            var gm = UnityEngine.Object.FindObjectOfType<GameManager>();
            var queue = gm != null ? gm.UXEventQueue : null;
            if (queue == null) return; // not in a duel, ignore

            if (!queue.IsRunning)
            {
                Toast.Info("Animation queue is empty");
                return;
            }

            int pending = queue.PendingEvents?.Count ?? 0;
            int running = queue.RunningEvents?.Count ?? 0;

            _savedTimeScale = Time.timeScale;
            _savedMaxDeltaTime = Time.maximumDeltaTime;
            Time.timeScale = FastForwardMultiplier;
            Time.maximumDeltaTime = MaxDeltaTimeWhileFast;
            _active = true;

            Plugin.Log.LogInfo(
                $"AnimationFastForward: started at {FastForwardMultiplier}x " +
                $"(pending={pending}, running={running})");
            PerPlayerLog.Info(
                $"AnimationFastForward: F7 — fast-forwarding {pending + running} event(s)");
            Toast.Info($"Fast-forward {FastForwardMultiplier:0}x");
        }

        private static void Stop(string reason)
        {
            // Restore even if _active somehow got set without us setting
            // the saved values — Time.timeScale = 1 is a sane fallback.
            Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
            Time.maximumDeltaTime = _savedMaxDeltaTime > 0f ? _savedMaxDeltaTime : 0.333f;
            _active = false;
            Plugin.Log.LogInfo($"AnimationFastForward: stopped ({reason})");
            if (reason == "caught up") Toast.Success("Caught up");
        }
    }
}
