using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Crm.Extract.Http;

/// <summary>
/// Reads a generic credential's password from Windows Credential Manager (§2.2), so an explicit-mode password need
/// not sit in any file. P/Invoke into advapi32 rather than a package: two functions, no dependency.
/// </summary>
public static class WindowsCredentialStore
{
    private const int GenericCredential = 1;

    public static string? TryReadPassword(string target)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(target))
        {
            return null;
        }
        return ReadOnWindows(target);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadOnWindows(string target)
    {
        if (!CredRead(target, GenericCredential, 0, out IntPtr credentialPointer))
        {
            return null;
        }
        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }
            return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / sizeof(char));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    // DllImport rather than LibraryImport: the source-generated form needs AllowUnsafeBlocks for this signature, and
    // two calls at startup do not justify enabling unsafe code in the network project.
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
