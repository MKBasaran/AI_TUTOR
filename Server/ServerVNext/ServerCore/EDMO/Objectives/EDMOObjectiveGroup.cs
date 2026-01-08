namespace ServerCore.EDMO.Objectives;

/// <summary>
/// An objective group, used to group <see cref="EDMOObjective"/>s based on source or high level objectives.
/// </summary>
/// <param name="Title">The title of the objective group.</param>
/// <param name="Description">An optional description to provide further context for the objective groups.</param>
/// <param name="Objectives">Objectives that belong to this group"/></param>
public record EDMOObjectiveGroup(string Title, string? Description = null, params EDMOObjective[] Objectives);
