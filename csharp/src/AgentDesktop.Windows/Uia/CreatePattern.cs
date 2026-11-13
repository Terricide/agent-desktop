namespace AgentDesktop.Windows.Uia;

/// <summary>Binds a pattern pointer an element returned into the pattern interface a call
/// site asked for. The bound pattern owns the pointer.</summary>
internal delegate object CreatePattern(IntPtr pointer);
