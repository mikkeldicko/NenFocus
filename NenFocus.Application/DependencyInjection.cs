using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.DTOs;
using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Application.Queries.GetActiveSession;
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.Queries.GetSessionHistory;
using NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Application.UseCases.Sessions.StartBreak;
using NenFocus.Application.UseCases.Sessions.StartSession;

namespace NenFocus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // ── Session commands ───────────────────────────────────────────────
        services.AddScoped<ICommandHandler<StartSessionCommand, StartSessionResult>, StartSessionCommandHandler>();
        services.AddScoped<ICommandHandler<EndSessionCommand, EndSessionResult>, EndSessionCommandHandler>();
        services.AddScoped<ICommandHandler<StartBreakCommand, StartBreakResult>, StartBreakCommandHandler>();
        services.AddScoped<ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult>, ResumeFromBreakCommandHandler>();
        services.AddScoped<ICommandHandler<SaveReflectionCommand, SaveReflectionResult>, SaveReflectionCommandHandler>();

        // ── Block profile commands ─────────────────────────────────────────
        services.AddScoped<ICommandHandler<CreateBlockProfileCommand, CreateBlockProfileResult>, CreateBlockProfileCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBlockProfileCommand, UpdateBlockProfileResult>, UpdateBlockProfileCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteBlockProfileCommand>, DeleteBlockProfileCommandHandler>();
        services.AddScoped<ICommandHandler<DuplicateBlockProfileCommand, DuplicateBlockProfileResult>, DuplicateBlockProfileCommandHandler>();

        // ── Planner commands ───────────────────────────────────────────────
        services.AddScoped<ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>, CreatePlannedBlockCommandHandler>();
        services.AddScoped<ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>, UpdatePlannedBlockCommandHandler>();
        services.AddScoped<ICommandHandler<DeletePlannedBlockCommand>, DeletePlannedBlockCommandHandler>();

        // ── Queries ────────────────────────────────────────────────────────
        services.AddScoped<IQueryHandler<ComputeActivePolicySliceQuery, PolicySliceDto>, ComputeActivePolicySliceQueryHandler>();
        services.AddScoped<IQueryHandler<GetActiveSessionQuery, SessionDto?>, GetActiveSessionQueryHandler>();
        services.AddScoped<IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>>, GetAllBlockProfilesQueryHandler>();
        services.AddScoped<IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>>, GetPlannedBlocksForDayQueryHandler>();
        services.AddScoped<IQueryHandler<GetSessionHistoryQuery, IReadOnlyList<SessionSummaryDto>>, GetSessionHistoryQueryHandler>();

        // ── Event handlers ─────────────────────────────────────────────────
        services.AddScoped<SessionStartedEventHandler>();
        services.AddScoped<SessionEndedEventHandler>();
        services.AddScoped<BreakStartedEventHandler>();
        services.AddScoped<BlockProfileCreatedEventHandler>();

        return services;
    }
}