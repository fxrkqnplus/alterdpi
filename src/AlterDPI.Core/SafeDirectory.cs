using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace AlterDPI.Core;

public static class SafeDirectory
{
    // SECURITY_ATTRIBUTES applies the final descriptor at creation, before any other user can write.
    public static void CreateNew(string path, DirectorySecurity security)
    {
        var bytes = security.GetSecurityDescriptorBinaryForm(); var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length); var attributes = new Attributes { Length = Marshal.SizeOf<Attributes>(), Descriptor = pointer };
            if (!CreateDirectory(Path.GetFullPath(path), ref attributes)) throw new IOException("INSTALL_ROOT", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateDirectory(string path, ref Attributes attributes);
}
