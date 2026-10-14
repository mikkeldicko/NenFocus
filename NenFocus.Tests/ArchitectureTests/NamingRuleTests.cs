using Avalonia.Data.Converters;
using FluentAssertions;
using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;
using NenFocus.Domain.Events;
using NenFocus.Domain.Services;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>MODULE_STANDARDS → Naming Conventions: a name says what a thing is, so it can be found by name.</summary>
public class NamingRuleTests
{
    private static readonly Type[] ApplicationTypes = typeof(Notifier).Assembly.GetTypes();
    private static readonly Type[] DomainTypes = typeof(PolicyEngine).Assembly.GetTypes();
    private static readonly Type[] UiTypes = typeof(ShellViewModel).Assembly.GetTypes();

    /// <summary>StartSessionCommand → StartSessionCommandHandler; GetActiveSessionQuery → GetActiveSessionQueryHandler.</summary>
    [Fact]
    public void HandlersAreNamedAfterWhatTheyHandle()
    {
        var violations = ApplicationTypes
            .Where(t => !t.IsInterface && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType
                            && (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
                .Select(i => i.GetGenericArguments()[0])
                .Where(request => t.Name != request.Name + "Handler")
                .Select(request => $"{t.FullName} handles {request.Name}, should be {request.Name}Handler"));

        KnownExceptions.Check(violations, []);
    }

    /// <summary>
    /// A generic class that is itself a handler wraps every handler of its kind — a decorator, not a
    /// handler (Gap 112): TransactionCommandHandlerDecorator, ValidationCommandHandlerDecorator.
    /// </summary>
    [Fact]
    public void DecoratorsAreNamedAfterTheConcernAndWhatTheyWrap()
    {
        var decorators = ApplicationTypes
            .Where(t => t.IsGenericTypeDefinition && !t.IsInterface && !t.IsAbstract)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType
                            && (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
                .Select(i => (Type: t, Suffix: i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                    ? "CommandHandlerDecorator" : "QueryHandlerDecorator")))
            .ToList();

        decorators.Should().NotBeEmpty("the transaction and validation decorators live in Application");
        var violations = decorators
            .Where(d => !d.Type.Name[..d.Type.Name.IndexOf('`')].EndsWith(d.Suffix, StringComparison.Ordinal))
            .Select(d => $"{d.Type.FullName} should be named <Concern>{d.Suffix}");

        KnownExceptions.Check(violations, []);
    }

    /// <summary>StartSessionCommand → StartSessionResult.</summary>
    [Fact]
    public void CommandResultsAreNamedAfterTheirCommand()
    {
        var violations = ApplicationTypes
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>))
                .Select(i => i.GetGenericArguments()[0])
                .Where(result => result.Name != t.Name[..^"Command".Length] + "Result")
                .Select(result => $"{t.FullName} returns {result.Name}"));

        KnownExceptions.Check(violations, []);
    }

    [Fact]
    public void DomainEventsEndInEvent()
    {
        var violations = DomainTypes
            .Where(t => !t.IsAbstract && typeof(DomainEvent).IsAssignableFrom(t) && !t.Name.EndsWith("Event", StringComparison.Ordinal))
            .Select(t => t.FullName!);

        KnownExceptions.Check(violations, []);
    }

    /// <summary>A Domain service is named for the role it plays (-Engine, -Classifier, -Calculator…);
    /// "-Service" is reserved for Infrastructure and the Application↔UI bridges.</summary>
    [Fact]
    public void NothingInTheDomainIsNamedService()
    {
        var violations = DomainTypes
            .Where(t => t.Name.EndsWith("Service", StringComparison.Ordinal))
            .Select(t => t.FullName!);

        KnownExceptions.Check(violations, []);
    }

    /// <summary>Get (fetch) / Compute (derive) / Check (yes/no). A new verb means a table edit first.</summary>
    [Fact]
    public void QueriesStartWithAnAgreedVerb()
    {
        string[] verbs = ["Get", "Compute", "Check"];

        var violations = ApplicationTypes
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)))
            .Where(t => !verbs.Any(v => t.Name.StartsWith(v, StringComparison.Ordinal)))
            .Select(t => t.FullName!);

        KnownExceptions.Check(violations, []);
    }

    /// <summary>A DTO is a read model the Application hands out — one home, Application/DTOs/.</summary>
    [Fact]
    public void DtosLiveInTheDtosFolder()
    {
        var violations = ApplicationTypes.Concat(UiTypes).Concat(typeof(SystemClock).Assembly.GetTypes())
            .Where(t => t.Name.EndsWith("Dto", StringComparison.Ordinal) && t.Namespace != "NenFocus.Application.DTOs")
            .Select(t => t.FullName!);

        KnownExceptions.Check(violations, []);
    }

    private static readonly string[] ConverterNameExceptions =
    [
        // Static formatters named like converters: ViewModels call them to build display strings,
        // which the ViewModel Forbidden Zone bans (No String Formatting). → Gap 89 (ViewModel audit).
        "NenFocus.Converters.DateRangeToLabelConverter: named …Converter but not an IValueConverter",
        "NenFocus.Converters.DateTimeRangeToLabelConverter: named …Converter but not an IValueConverter",
        "NenFocus.Converters.WorkHoursRangeToLabelConverter: named …Converter but not an IValueConverter",
    ];

    [Fact]
    public void ConvertersAndIValueConverterGoTogether()
    {
        // Static classes count too: .NET marks them abstract + sealed.
        var concrete = UiTypes.Where(t => !t.IsInterface && (!t.IsAbstract || t.IsSealed)).ToList();

        var violations = concrete
            .Where(t => typeof(IValueConverter).IsAssignableFrom(t) && !t.Name.EndsWith("Converter", StringComparison.Ordinal))
            .Select(t => $"{t.FullName}: IValueConverter but not named …Converter")
            .Concat(concrete
                .Where(t => t.Name.EndsWith("Converter", StringComparison.Ordinal) && !typeof(IValueConverter).IsAssignableFrom(t))
                .Select(t => $"{t.FullName}: named …Converter but not an IValueConverter"));

        KnownExceptions.Check(violations, ConverterNameExceptions);
    }

    [Fact]
    public void ObservableUiTypesAreViewModels()
    {
        var violations = UiTypes
            .Where(t => typeof(ObservableObject).IsAssignableFrom(t) && t != typeof(ViewModelBase)
                        && !t.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .Select(t => t.FullName!);

        KnownExceptions.Check(violations, []);
    }
}
