using System;

namespace ServerCore.EDMO.Objectives;

/// <summary>
/// A record that represents an objective for an <see cref="EDMOSession"/>
/// </summary>
/// <param name="Title">The title of the objective</param>
/// <param name="Description">An optional description to provide further detail to the objective.</param>
public record EDMOObjective(string Title, string? Description = null)
{
    private bool completed;

    /// <summary>
    /// Get/Sets the completion state of the objective.
    /// </summary>
    /// <remarks>
    /// Once an objective is completed, it cannot be "uncompleted". This is an intentional design choice to prevent frustration when objectives can be undone out of users' control.
    /// </remarks>
    public bool Completed
    {
        get => completed;
        set
        {
            if (completed)
                return;

            if (!value)
                return;

            completed = value;
            ObjectiveCompleted?.Invoke();
        }
    }

    /// <summary>
    /// An event that is fired when an objective is completed, allowing consumers to revalidate.
    /// </summary>
    public Action? ObjectiveCompleted { get; set; }
}
