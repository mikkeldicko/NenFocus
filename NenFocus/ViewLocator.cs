using Avalonia.Controls;
using Avalonia.Controls.Templates;
using NenFocus.ViewModels;
using System;

namespace NenFocus
{
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            var viewName = param.GetType().Name.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType($"NenFocus.Views.{viewName}, NenFocus");

            if (type != null)
                return (Control)Activator.CreateInstance(type)!;

            return new TextBlock { Text = "Not Found: " + viewName };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
