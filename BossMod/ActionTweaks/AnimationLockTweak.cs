﻿namespace BossMod;

// Effective animation lock reduction tweak (a-la xivalex/noclippy).
// The game handles instants and casted actions differently:
// * instants: on action request (e.g. on the frame the action button is pressed), animation lock is set to 0.5 (or 0.35 for some specific actions); it then ticks down every frame
//   some time later (ping + server latency, typically 50-100ms if ping is good), we receive action effect packet - the packet contains action's animation lock (typically 0.6)
//   the game then updates animation lock (now equal to 0.5 minus time since request) to the packet data
//   so the 'effective' animation lock between action request and animation lock end is equal to action's animation lock + delay between request and response
//   this tweak reduces effective animation lock by either removing extra delay completely or clamping it to specified min/max values
// * casts: on action request animation lock is not set (remains equal to 0), remaining cast time is set to action's cast time; remaining cast time then ticks down every frame
//   some time later (cast time minus approximately 0.5s, aka slidecast window), we receive action effect packet - the packet contains action's animation lock (typically 0.1)
//   the game then updates animation lock (still 0) to the packet data - however, since animation lock isn't ticking down while cast is in progress, there is no extra delay
//   this tweak does nothing for casts, since they already work correctly
// The tweak also allows predicting the delay based on history (using exponential average).
// TODO: consider adding 'clamped delay' mode that doesn't reduce it straight to zero (a-la xivalex)?
public sealed class AnimationLockTweak
{
    private readonly ActionManagerConfig _config = Service.Config.Get<ActionManagerConfig>();

    public float DelaySmoothing = 0.8f; // TODO tweak
    public float DelayAverage { get; private set; } = 0.1f; // smoothed delay between client request and server response
    public float DelayEstimate => _config.RemoveAnimationLockDelay ? 0 : MathF.Min(DelayAverage * 1.5f, 0.1f); // this is a conservative estimate

    // perform sanity check to detect conflicting plugins: disable the tweak if condition is false
    public void SanityCheck(float originalAnimLock, float modifiedAnimLock)
    {
        if (!_config.RemoveAnimationLockDelay)
            return; // nothing to do, tweak is already disabled
        if (originalAnimLock == modifiedAnimLock && originalAnimLock % 0.01 is <= 0.0005f or >= 0.0095f)
            return; // nothing changed the packet value, and it's original value is reasonable

        Service.Log($"[ALT] Unexpected animation lock {originalAnimLock:f} -> {modifiedAnimLock:f}, disabling anim lock tweak feature");
        _config.RemoveAnimationLockDelay = false; // disable the tweak (but don't save the config, in case this condition is temporary)
    }

    // apply tweak: given the delay, calculate how much it should be reduced
    public float Apply(float current, float delay)
    {
        DelayAverage = delay * (1 - DelaySmoothing) + DelayAverage * DelaySmoothing; // update the average
        // the result will be subtracted from current anim lock (and thus from adjusted lock delay)
        return _config.RemoveCooldownDelay ? Math.Clamp(delay /* - DelayMax */, 0, current) : 0;
    }
}
