using System;
using System.Runtime.InteropServices;

namespace Disfigure.Cryptography
{
    public static class TinyECDH
    {
        [DllImport("tiny_ecdh.dll", EntryPoint = "ecdh_generate_keys")]
        public static extern int GenerateKeys(IntPtr publicKey, IntPtr privateKey);

        [DllImport("tiny_ecdh.dll", EntryPoint = "ecdh_shared_secret")]
        public static extern int GenerateSharedKey(IntPtr privateKey, IntPtr remotePublicKey, IntPtr sharedKey);

    }
}
