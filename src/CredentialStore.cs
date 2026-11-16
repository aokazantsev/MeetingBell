using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MeetingBell
{
    internal static class CredentialStore
    {
        public const string Target = "MeetingBell:exchange";

        private const int GenericType = 1;
        private const int PersistLocalMachine = 2;
        private const int NotFound = 1168;

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static ExchangeCredential Read()
        {
            IntPtr pointer;
            if (!CredRead(Target, GenericType, 0, out pointer))
            {
                int code = Marshal.GetLastWin32Error();
                if (code == NotFound) return null;
                throw new Win32Exception(code);
            }
            try
            {
                var native = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                string password = native.CredentialBlobSize > 0 && native.CredentialBlob != IntPtr.Zero
                    ? Marshal.PtrToStringUni(native.CredentialBlob, native.CredentialBlobSize / 2)
                    : "";
                return new ExchangeCredential
                {
                    UserName = native.UserName == IntPtr.Zero ? "" : Marshal.PtrToStringUni(native.UserName),
                    Password = password
                };
            }
            finally
            {
                CredFree(pointer);
            }
        }

        public static string TryRead(out ExchangeCredential credential)
        {
            try
            {
                credential = Read();
                return null;
            }
            catch (Win32Exception error)
            {
                credential = null;
                return error.Message;
            }
        }

        public static string Write(ExchangeCredential credential)
        {
            IntPtr target = Marshal.StringToCoTaskMemUni(Target);
            IntPtr user = Marshal.StringToCoTaskMemUni(credential.UserName);
            IntPtr blob = Marshal.StringToCoTaskMemUni(credential.Password);
            try
            {
                var native = new NativeCredential
                {
                    Type = GenericType,
                    TargetName = target,
                    UserName = user,
                    CredentialBlob = blob,
                    CredentialBlobSize = credential.Password.Length * 2,
                    Persist = PersistLocalMachine
                };
                if (CredWrite(ref native, 0)) return null;
                return new Win32Exception(Marshal.GetLastWin32Error()).Message;
            }
            finally
            {
                Marshal.ZeroFreeCoTaskMemUnicode(blob);
                Marshal.FreeCoTaskMem(user);
                Marshal.FreeCoTaskMem(target);
            }
        }

        public static string Delete()
        {
            if (CredDelete(Target, GenericType, 0)) return null;
            int code = Marshal.GetLastWin32Error();
            return code == NotFound ? null : new Win32Exception(code).Message;
        }
    }
}
