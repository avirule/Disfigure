#region

using System;
using System.Runtime.InteropServices;
using Serilog;

#endregion

namespace Disfigure.Cryptography
{
    public static class TinyECDH
    {
        [DllImport("tiny_ecdh.dll", EntryPoint = "ecdh_generate_keys", ExactSpelling = true, SetLastError = true)]
        private static extern int DerivePublicKeyNative(IntPtr publicKey, IntPtr privateKey);

        [DllImport("tiny_ecdh.dll", EntryPoint = "ecdh_shared_secret", ExactSpelling = true, SetLastError = true)]
        private static extern int DeriveSharedKeyNative(IntPtr privateKey, IntPtr remotePublicKey, IntPtr derivedKey);

        public static int DerivePublicKey(IntPtr publicKey, IntPtr privateKey)
        {
            int result = DerivePublicKeyNative(publicKey, privateKey);
            int errorCode = Marshal.GetLastWin32Error();

            if (errorCode != 0)
            {
                Log.Warning($"P/Invoke call to TinyECDH.dll failed with error code {errorCode}.");
            }

            return result;
        }

        public static int DeriveSharedKey(IntPtr privateKey, IntPtr remotePublicKey, IntPtr derivedKey)
        {
            int result = DeriveSharedKeyNative(privateKey, remotePublicKey, derivedKey);
            int errorCode = Marshal.GetLastWin32Error();

            if (errorCode != 0)
            {
                Log.Warning($"P/Invoke call to TinyECDH.dll failed with error code {errorCode}.");
            }

            return result;
        }
    }
}
