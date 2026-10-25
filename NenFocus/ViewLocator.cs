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

            var viewName = ViewNameFor(param.GetType());
            var type = Type.GetType($"{viewName}, NenFocus");

            if (type != null)
                return (Control)Activator.CreateInstance(type)!;

            return new TextBlock { Text = "Not Found: " + viewName };
        }

        /// <summary>
        /// Views mirror ViewModels folder for folder: NenFocus.ViewModels.Planner.PlannerPageViewModel
        /// → NenFocus.Views.Planner.PlannerPageView (Gap 89 Slice B; Avalonia's own convention).
        /// </summary>
        public static string ViewNameFor(Type viewModelType)
        {
            const string viewModels = "NenFocus.ViewModels.", views = "NenFocus.Views.", suffix = "ViewModel";
            var name = viewModelType.FullName!;
            if (name.StartsWith(viewModels, StringComparison.Ordinal))
                name = views + name[viewModels.Length..];
            return name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] + "View" : name;
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
