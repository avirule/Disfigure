#region

using System;
using System.Text;

#endregion

namespace Disfigure.Message
{
    public class TextMessage : Message<string>
    {
        public TextMessage(Guid author, DateTime utcTimestamp, byte[] content) : base(author, utcTimestamp, content) { }

        public override string Deserialize() => Encoding.Unicode.GetString(Content);
    }
}
