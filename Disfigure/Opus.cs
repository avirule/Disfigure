#region

using System;

#endregion

namespace Disfigure
{
    public static class Opus
    {
        public static extern IntPtr opus_encoder_create(int fs, int channels, int application, out IntPtr errors);
    }
}
