namespace AgentDesktop.Windows.System;

/// <summary>
/// Thread-scoped test hooks that let the keyboard dispatch path be driven into states that the live
/// desktop does not produce on demand. Every flag and hook is per thread, matching the Rust thread-local
/// storage, so concurrent dispatches on other threads observe no change.
/// </summary>
internal static class KeyDispatchHooks
{
    [global::System.ThreadStatic]
    private static bool _forceHigherIntegrityActive;

    [global::System.ThreadStatic]
    private static bool _forceFocusLostActive;

    [global::System.ThreadStatic]
    private static global::System.Action? _betweenVerifyAndInjectHook;

    [global::System.ThreadStatic]
    private static uint _synthesisProbeCount;

    [global::System.ThreadStatic]
    private static bool _forceKeyboardFocusDeniedActive;

    private static TResult WithFlag<TResult>(ref bool flag, bool value, global::System.Func<TResult> run)
    {
        var previous = flag;
        flag = value;
        try
        {
            return run();
        }
        finally
        {
            flag = previous;
        }
    }

    internal static class ForceHigherIntegrity
    {
        public static bool IsActive() => _forceHigherIntegrityActive;

        public static TResult With<TResult>(global::System.Func<TResult> run) =>
            WithFlag(ref _forceHigherIntegrityActive, true, run);
    }

    internal static class ForceFocusLost
    {
        public static bool IsActive() => _forceFocusLostActive;

        public static void Arm() => _forceFocusLostActive = true;

        public static TResult WithArmedAfterHook<TResult>(
            global::System.Action hook,
            global::System.Func<TResult> run)
        {
            global::System.Action combined = () =>
            {
                hook();
                Arm();
            };
            BetweenVerifyAndInject.Install(combined);
            try
            {
                return run();
            }
            finally
            {
                _forceFocusLostActive = false;
                BetweenVerifyAndInject.Clear();
            }
        }
    }

    internal static class BetweenVerifyAndInject
    {
        public static void Run()
        {
            _betweenVerifyAndInjectHook?.Invoke();
        }

        public static void Install(global::System.Action hook) => _betweenVerifyAndInjectHook = hook;

        public static void Clear() => _betweenVerifyAndInjectHook = null;
    }

    internal static class SynthesisProbe
    {
        public static void Note() => _synthesisProbeCount++;

        public static uint Count() => _synthesisProbeCount;

        public static void Reset() => _synthesisProbeCount = 0;
    }

    internal static class ForceKeyboardFocusDenied
    {
        public static bool IsActive() => _forceKeyboardFocusDeniedActive;

        public static TResult With<TResult>(global::System.Func<TResult> run) =>
            WithFlag(ref _forceKeyboardFocusDeniedActive, true, run);
    }
}
