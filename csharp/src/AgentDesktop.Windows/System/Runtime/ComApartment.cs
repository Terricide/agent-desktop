namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// Which COM apartment a thread is in for this product's purposes. <c>OwnedMta</c> means this process
/// initialized the thread and may balance it with <c>CoUninitialize</c>. <c>BorrowedFromHostMode</c> means
/// another component already chose the thread's mode, so the thread is used as-is and never uninitialized.
/// </summary>
internal enum ComApartment
{
    OwnedMta,
    BorrowedFromHostMode,
}
