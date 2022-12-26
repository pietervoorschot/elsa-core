using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Elsa.Common.Extensions;
using Elsa.Workflows.Core.Attributes;
using Elsa.Workflows.Core.Models;
using Elsa.Workflows.Core.Services;

namespace Elsa.Workflows.Core.Activities;

/// <summary>
/// Branch execution into multiple branches.
/// </summary>
[Activity("Elsa", "Control Flow", "Branch execution into multiple branches.")]
public class Fork : ActivityBase
{
    /// <inheritdoc />
    [JsonConstructor]
    public Fork([CallerFilePath] string? source = default, [CallerLineNumber] int? line = default) : base(source, line)
    {
    }
    
    /// <summary>
    /// Controls when this activity yields control back to its parent activity.
    /// </summary>
    [Input]
    public ForkJoinMode JoinMode { get; set; } = ForkJoinMode.WaitAll;

    /// <summary>
    /// The branches to schedule.
    /// </summary>
    [Port]
    public ICollection<IActivity> Branches { get; set; } = new List<IActivity>();

    /// <inheritdoc />
    protected override ValueTask ExecuteAsync(ActivityExecutionContext context) => context.ScheduleActivities(Branches.Reverse(), CompleteChildAsync);

    private async ValueTask CompleteChildAsync(ActivityExecutionContext context, ActivityExecutionContext childContext)
    {
        var completedChildActivityId = childContext.Activity.Id;

        // Append activity to set of completed activities.
        var completedActivityIds = context.UpdateProperty<HashSet<string>>("Completed", set =>
        {
            set ??= new HashSet<string>();
            set.Add(completedChildActivityId);
            return set;
        });

        var allChildActivityIds = Branches.Select(x => x.Id).ToImmutableHashSet();
        var joinMode = JoinMode;

        switch (joinMode)
        {
            case ForkJoinMode.WaitAny:
            {
                // Remove any and all bookmarks from other branches.
                RemoveBookmarks(context);

                // Signal activity completion.
                await CompleteAsync(context);
            }
                break;
            case ForkJoinMode.WaitAll:
            {
                var allSet = allChildActivityIds.All(x => completedActivityIds.Contains(x));

                if (allSet)
                    // Signal activity completion.
                    await CompleteAsync(context);
            }
                break;
        }
    }

    private void RemoveBookmarks(ActivityExecutionContext context)
    {
        // Find all descendants for each branch and remove them as well as any associated bookmarks.
        var workflowExecutionContext = context.WorkflowExecutionContext;
        var forkNode = context.ActivityNode;
        var branchNodes = forkNode.Children;
        var branchDescendantActivityIds = branchNodes.SelectMany(x => x.Flatten()).Select(x => x.Activity.Id).ToHashSet();
        
        workflowExecutionContext.Bookmarks.RemoveWhere(x => branchDescendantActivityIds.Contains(x.ActivityId));
    }
}