#region

using System.Text;

#endregion

namespace Disfigure.Message
{
    public class TextMessage : Message<string>
    {
        public override string Deserialize() => Encoding.Unicode.GetString(Content);
    }
}
