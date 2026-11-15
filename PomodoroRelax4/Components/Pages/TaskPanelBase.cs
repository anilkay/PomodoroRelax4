using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using PomodoroRelax4.Models;
using PomodoroRelax4.Services;

namespace PomodoroRelax4.Components.Pages;

/// <summary>
/// What the two task views (TaskList under the timer, TodoPanel on the To Do page) share:
/// the signed-in user's tasks, loaded during prerender and carried into the circuit, and the
/// user's own calendar day.
/// </summary>
public abstract class TaskPanelBase : ComponentBase
{
    [Inject] protected ITaskService TaskService { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;

    protected List<PomodoroTask> tasks = new();
    protected string? userId;

    // The user's own calendar day, for "Today" / "Overdue"; UTC until the browser reports its offset
    protected DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

    private bool _askedBrowser;

    // Only set while prerendering (statically, before the circuit exists)
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    // Loaded during prerender and carried into the circuit: the first HTML already lists the
    // tasks, and the circuit starts from the same data instead of querying again
    [PersistentState]
    public List<PomodoroTask>? PrerenderedTasks { get; set; }

    [PersistentState]
    public int? UtcOffsetMinutes { get; set; }

    /// <summary>Prerender only: read this view's own cookies into its [PersistentState] properties.</summary>
    protected virtual void ReadPreferences(HttpContext context) { }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return;

        if (HttpContext is not null)
        {
            UtcOffsetMinutes = ClientPreferences.UtcOffsetMinutes(HttpContext);
            ReadPreferences(HttpContext);
        }
        PrerenderedTasks ??= await TaskService.GetTasksAsync(userId);

        tasks = PrerenderedTasks;
        if (UtcOffsetMinutes is int offset)
            today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(offset));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // No tz cookie yet (first visit): ask the browser once the circuit is up
        if (!firstRender || _askedBrowser || UtcOffsetMinutes is not null) return;
        _askedBrowser = true;

        try
        {
            var offsetMinutes = await JS.InvokeAsync<int>("pomodoroTime.utcOffsetMinutes");
            today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(offsetMinutes));
            StateHasChanged();
        }
        catch (JSException) { /* keep UTC */ }
    }
}
