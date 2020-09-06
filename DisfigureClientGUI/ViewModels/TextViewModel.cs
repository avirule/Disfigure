using System.Collections.Generic;

namespace Disfigure.ViewModels
{
    public class TextViewModel
    {
        public List<string> Messages { get; }
        public string Message { get; set; }

        public TextViewModel()
        {
            Messages = new List<string>
            {
                "Test", "test1", ":asdasdasdasdasd"
            };
        }
    }
}