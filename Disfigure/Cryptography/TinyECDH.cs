#region

using System;
using System.Runtime.InteropServices;

#endregion

namespace Disfigure.Cryptography
{
    public static class TinyECDH
    {
        private const string _DLL_NAME = "tiny_ecdh.dll";

        // native signature
        // note: public_key is a pre-allocated byte array that is filled by native function
        // __declspec(dllexport) int ecdh_generate_keys(uint8_t* public_key, uint8_t* private_key);
        [DllImport(_DLL_NAME, EntryPoint = "ecdh_generate_keys", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe int DerivePublicKeyNative(byte* publicKey, byte* privateKey);

        public static unsafe void DerivePublicKey(byte* publicKey, byte* privateKey)
        {
            int result = DerivePublicKeyNative(publicKey, privateKey);

            if (result == 0) // failure
            {
                throw new Exception($"P/Invoke call to {_DLL_NAME} failed.");
            }
        }

        // native signature
        // note: output is a pre-allocated byte array that is filled by native function
        // __declspec(dllexport) int ecdh_shared_secret(const uint8_t* private_key, const uint8_t* others_pub, uint8_t* output);
        [DllImport(_DLL_NAME, EntryPoint = "ecdh_shared_secret", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe int DeriveSharedKeyNative(byte* privateKey, byte* remotePublicKey, byte* derivedKey);

        public static unsafe void DeriveSharedKey(byte* privateKey, byte* remotePublicKey, byte* derivedKey)
        {
            int result = DeriveSharedKeyNative(privateKey, remotePublicKey, derivedKey);

            if (result == 0) // failure
            {
                throw new Exception($"P/Invoke call to {_DLL_NAME} failed.");
            }
        }
    }
}