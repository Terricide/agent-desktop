using System;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>
/// The device-context half of <c>GdiSurface</c>: the pair every Legacy capture surface opens before it selects its
/// own bitmap into the memory DC, and the order the pair is torn down in.
/// </summary>
internal static partial class GdiSurface
{
    /// <summary>
    /// The screen DC and its paired compatible memory DC, released together.
    /// <para>
    /// Owns exactly the two GDI objects every Legacy capture surface opens before selecting its own bitmap into
    /// <see cref="MemoryDc"/>. The bitmap stays with the caller, since a compatible bitmap and a DIB section are read
    /// back through different APIs.
    /// </para>
    /// </summary>
    internal sealed class GdiDcPair : IDisposable
    {
        private GdiDcPair(IntPtr screenDc, IntPtr memoryDc)
        {
            ScreenDc = screenDc;
            MemoryDc = memoryDc;
        }

        internal IntPtr ScreenDc { get; }

        internal IntPtr MemoryDc { get; }

        /// <summary>
        /// Opens the screen DC and pairs it with a compatible memory DC. The screen DC is taken for the whole desktop,
        /// so a capture region is addressed by coordinates, not by a window. Each DC acquires on creation; a pairing
        /// that fails after the screen DC is taken returns that DC to the platform before the error is raised.
        /// </summary>
        internal static GdiDcPair Create(string context)
        {
            var screenDc = Native.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastWin32Error(), $"{context} GetDC failed");
            }

            GdiBalance.Acquire();
            var memoryDc = Native.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                GdiBalance.Release();
                throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastWin32Error(), $"{context} CreateCompatibleDC failed");
            }

            GdiBalance.Acquire();
            return new GdiDcPair(screenDc, memoryDc);
        }

        /// <summary>
        /// Tears the pair down in the order the platform expects: the memory DC first, then the screen DC back to the
        /// desktop. Each release decrements the balance it acquired, so a pair that is torn down leaves nothing counted.
        /// </summary>
        public void Dispose()
        {
            Native.DeleteDC(MemoryDc);
            GdiBalance.Release();
            Native.ReleaseDC(IntPtr.Zero, ScreenDc);
            GdiBalance.Release();
        }
    }
}
