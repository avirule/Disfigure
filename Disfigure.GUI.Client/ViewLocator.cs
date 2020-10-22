// Copyright (c) The Avalonia Project. All rights reserved.
// Licensed under the MIT license. See licence.md file in the project root for full license information.

using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Disfigure.GUI.Client.ViewModels;

namespace Disfigure.GUI.Client
{
    public class ViewLocator : IDataTemplate
    {
        public bool SupportsRecycling => false;

        public IControl Build(object data)
        {
            string? name = data.GetType().FullName!.Replace("ViewModel", "View");
            Type? type = Type.GetType(name);

            if (type != null) return (Control)Activator.CreateInstance(type)!;
            else
            {
                return new TextBlock
                {
                    Text = "Not Found: " + name
                };
            }
        }

        public bool Match(object data) => data is ViewModelBase;
    }
}
